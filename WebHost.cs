namespace unoframework;

using System.Collections.Concurrent;
using System.Net;
using Middlewares;
using RequestHandlers;
using ResponseHandlers;
using Routing;

public sealed class WebHost : IAsyncDisposable
{
    /// <summary>How long <see cref="StopAsync()"/> waits for in-flight requests by default.</summary>
    public static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly List<Middleware> _middlewares = [];
    private readonly Router _router = new();
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<HttpListenerContext, Task> _inFlight = new();
    private HttpListener? _listener;
    private MiddlewarePipeline? _pipeline;
    private Task? _acceptLoop;
    private Task? _stopTask;
    private int _stopping; // 0/1; written with Interlocked for a full fence (see AcceptLoopAsync)

    /// <summary>The request whose handler is currently executing on this async flow, if any.</summary>
    private static readonly AsyncLocal<HttpListenerContext?> CurrentRequest = new();

    private bool Stopping => Volatile.Read(ref _stopping) == 1;

    public bool IsRunning => _listener?.IsListening == true && !Stopping;

    public WebHost Use(Middleware middleware)
    {
        EnsureConfigurable();
        _middlewares.Add(middleware);
        return this;
    }

    /// <summary>Registers a handler that accepts every HTTP method.</summary>
    public WebHost AddRoute(string path, RouteHandler handler) => AddRoute(Router.AnyMethod, path, handler);

    public WebHost AddRoute(string method, string path, RouteHandler handler)
    {
        EnsureConfigurable();
        _router.AddRoute(method, path, handler);
        return this;
    }

    public WebHost Get(string path, RouteHandler handler) => AddRoute("GET", path, handler);

    public WebHost Post(string path, RouteHandler handler) => AddRoute("POST", path, handler);

    public WebHost Put(string path, RouteHandler handler) => AddRoute("PUT", path, handler);

    public WebHost Patch(string path, RouteHandler handler) => AddRoute("PATCH", path, handler);

    public WebHost Delete(string path, RouteHandler handler) => AddRoute("DELETE", path, handler);

    /// <summary>
    /// Starts listening on <paramref name="url"/> and returns once the listener is accepting
    /// requests. Requests are processed concurrently in the background until stopped.
    /// Use <see cref="RunAsync"/> to block until shutdown instead.
    /// </summary>
    public Task Start(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        var prefix = url.EndsWith('/') ? url : url + "/";

        lock (_gate)
        {
            EnsureConfigurable();

            var pipeline = new MiddlewarePipeline();
            foreach (var middleware in _middlewares)
            {
                pipeline.Use(middleware);
            }

            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            try
            {
                listener.Start();
            }
            catch
            {
                listener.Close();
                throw;
            }

            _pipeline = pipeline;
            _listener = listener;
            _acceptLoop = AcceptLoopAsync(listener);
        }

        Console.WriteLine($"Listening on {prefix}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts the host and blocks until <paramref name="cancellationToken"/> is cancelled, then
    /// shuts down gracefully.
    /// </summary>
    public async Task RunAsync(string url, CancellationToken cancellationToken)
    {
        await Start(url);
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }

        await StopAsync();
    }

    public void Stop() => StopAsync().GetAwaiter().GetResult();

    public Task StopAsync() => StopAsync(DefaultShutdownTimeout);

    /// <summary>
    /// Graceful shutdown: new requests are answered with 503, in-flight requests get up to
    /// <paramref name="timeout"/> to finish, and any still running after that are answered with
    /// 503 (or have their connection reset if they had already started sending a response).
    /// A stopped host cannot be restarted.
    /// </summary>
    /// <remarks>
    /// May be called from inside a handler: that request is not waited for (it would wait for
    /// itself); the listener is closed as soon as that handler's response has completed.
    /// Prefer cancelling the token passed to <see cref="RunAsync"/> for that, though.
    /// </remarks>
    public Task StopAsync(TimeSpan timeout)
    {
        lock (_gate)
        {
            if (_listener is null)
            {
                return Task.CompletedTask;
            }

            return _stopTask ??= StopCoreAsync(_listener, timeout, CurrentRequest.Value);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task StopCoreAsync(HttpListener listener, TimeSpan timeout, HttpListenerContext? caller)
    {
        // Full fence: pairs with the register-then-check in AcceptLoopAsync, so every accepted
        // context is either rejected with 503 or visible in _inFlight below.
        Interlocked.Exchange(ref _stopping, 1);

        // Requests can still be added while we wait (keep-alive connections, accept races), so
        // keep draining until the set is empty or the deadline passes.
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var pending = OthersInFlight(caller);
            var remaining = deadline - DateTime.UtcNow;
            if (pending.Length == 0 || remaining <= TimeSpan.Zero)
            {
                break;
            }

            try
            {
                await Task.WhenAll(pending).WaitAsync(remaining).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                break;
            }
            catch
            {
                // Request tasks never fault (ProcessRequestAsync catches everything); be defensive anyway.
            }
        }

        // Requests still running past the deadline get an explicit 503. (Response.Abort() and
        // HttpListener.Close() on the managed listener send "200 OK" with an empty body, which
        // looks like success to the client, so they are only a fallback when headers already went.)
        // The straggling handler may still be running and write concurrently; whichever side
        // loses gets an ObjectDisposedException/InvalidOperationException, which both sides catch.
        foreach (var context in _inFlight.Keys)
        {
            if (!ReferenceEquals(context, caller))
            {
                await TryRespondUnavailableAsync(context).ConfigureAwait(false);
            }
        }

        if (caller is not null && _inFlight.TryGetValue(caller, out var callerTask))
        {
            // Called from a handler: close once that handler's response is done (or the same
            // shutdown deadline passes, so a handler that stops the host and then hangs can't keep
            // the listener open forever). Awaiting it here would deadlock: the handler awaits us.
            var callerDeadline = deadline - DateTime.UtcNow;
            _ = CloseAfterAsync(
                callerTask,
                callerDeadline > TimeSpan.Zero ? callerDeadline : TimeSpan.Zero,
                caller,
                listener
            );
            return;
        }

        await CloseAsync(listener).ConfigureAwait(false);
    }

    private async Task CloseAfterAsync(Task callerTask, TimeSpan timeout, HttpListenerContext caller, HttpListener listener)
    {
        if (await Task.WhenAny(callerTask, Task.Delay(timeout)).ConfigureAwait(false) != callerTask)
        {
            await TryRespondUnavailableAsync(caller).ConfigureAwait(false);
        }

        await CloseAsync(listener).ConfigureAwait(false);
    }

    private Task[] OthersInFlight(HttpListenerContext? caller) =>
        _inFlight.Where(kv => !ReferenceEquals(kv.Key, caller)).Select(kv => kv.Value).ToArray();

    private async Task CloseAsync(HttpListener listener)
    {
        listener.Close();
        if (_acceptLoop is not null)
        {
            await _acceptLoop.ConfigureAwait(false);
        }
    }

    private void EnsureConfigurable()
    {
        if (Stopping)
        {
            throw new InvalidOperationException("The host has been stopped and cannot be reconfigured or restarted.");
        }

        if (_listener is not null)
        {
            throw new InvalidOperationException("The host is already running; register routes and middlewares before Start().");
        }
    }

    private async Task AcceptLoopAsync(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                if (!listener.IsListening)
                {
                    break; // Stopped.
                }

                Console.Error.WriteLine($"Failed to accept request: {ex.Message}");
                continue;
            }

            // Register *before* checking for shutdown (and StopCoreAsync sets the flag *before*
            // reading _inFlight), so a request can't slip between the two and be cut off by Close().
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _inFlight[context] = completion.Task;

            // Don't let a slow handler block the next request.
            _ = Task.Run(async () =>
            {
                try
                {
                    if (Stopping)
                    {
                        await TryRespondUnavailableAsync(context).ConfigureAwait(false);
                    }
                    else
                    {
                        CurrentRequest.Value = context;
                        await ProcessRequestAsync(context).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _inFlight.TryRemove(context, out _);
                    completion.SetResult();
                }
            });
        }
    }

    private static async Task TryRespondUnavailableAsync(HttpListenerContext context)
    {
        try
        {
            context.Response.KeepAlive = false;
            await Results.ServiceUnavailable(context, "Server is shutting down").ConfigureAwait(false);
        }
        catch (Exception ex) when (IsResponseGone(ex))
        {
            // Client went away or the response was already completed.
        }
        catch (InvalidOperationException)
        {
            // Headers already sent: cut the connection so the truncated body is detectable.
            TryAbort(context);
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath;
            var route = path is null ? RouteResult.NotFound : _router.Resolve(context.Request.HttpMethod, path);
            RouteValues.Attach(context, route.Values);

            // Errors are mapped to responses *inside* the pipeline so middlewares observe 400/500
            // responses after `await next()` just like any other response.
            await _pipeline!.Invoke(context, () => DispatchSafelyAsync(context, route)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A middleware itself threw.
            await HandleExceptionAsync(context, ex).ConfigureAwait(false);
        }
        finally
        {
            // Guarantees the client gets a response even if a middleware short-circuited without
            // writing one. Close() is a no-op when the response was already closed.
            try
            {
                context.Response.Close();
            }
            catch (Exception ex) when (IsResponseGone(ex) || ex is InvalidOperationException)
            {
                // Connection already gone.
            }
        }
    }

    private async Task DispatchSafelyAsync(HttpListenerContext context, RouteResult route)
    {
        try
        {
            await DispatchAsync(context, route).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex).ConfigureAwait(false);
        }
    }

    private static Task DispatchAsync(HttpListenerContext context, RouteResult route)
    {
        switch (route.Status)
        {
            case RouteStatus.Found:
                return route.Handler!(context);
            case RouteStatus.InvalidPath:
                return Results.BadRequest(context, "Invalid path");
            case RouteStatus.MethodNotAllowed when context.Request.HttpMethod == "OPTIONS":
                context.Response.Headers.Set("Allow", string.Join(", ", route.AllowedMethods));
                return Results.NoContent(context);
            case RouteStatus.MethodNotAllowed:
                return Results.MethodNotAllowed(context, route.AllowedMethods, "Method not allowed");
            default:
                return Results.NotFound(context, "Route not found");
        }
    }

    private async Task HandleExceptionAsync(HttpListenerContext context, Exception ex)
    {
        if (IsResponseGone(ex))
        {
            // The client disconnected, HttpListener already answered (e.g. 411 Length Required),
            // or we're shutting down. Nothing left to send and nothing worth a stack trace.
            return;
        }

        if (ex is BadRequestException badRequest)
        {
            await TryWriteErrorAsync(context, StatusCodes.BadRequest, badRequest.Message).ConfigureAwait(false);
            return;
        }

        Console.Error.WriteLine($"Unhandled exception for {context.Request.HttpMethod} {context.Request.Url}: {ex}");
        await TryWriteErrorAsync(context, StatusCodes.InternalServerError, "Internal server error").ConfigureAwait(false);
    }

    private static bool IsResponseGone(Exception ex) =>
        ex is HttpListenerException
        || ex is ObjectDisposedException { ObjectName: "System.Net.HttpListenerResponse" };

    private static async Task TryWriteErrorAsync(HttpListenerContext context, StatusCodes status, string message)
    {
        try
        {
            if (status == StatusCodes.BadRequest)
            {
                await Results.BadRequest(context, message).ConfigureAwait(false);
            }
            else
            {
                await Results.InternalServerError(context, message).ConfigureAwait(false);
            }
        }
        catch
        {
            // Headers were already sent (or the response is closed): reset the connection rather
            // than leave the client with a truncated body that looks successful.
            TryAbort(context);
        }
    }

    private static void TryAbort(HttpListenerContext context)
    {
        try
        {
            context.Response.Abort();
        }
        catch
        {
            // ignored
        }
    }
}
