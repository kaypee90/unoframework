using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using unoframework.Middlewares;
using unoframework.RequestHandlers;
using unoframework.ResponseHandlers;

namespace unoframework.Tests;

public sealed class WebHostIntegrationTests : IAsyncLifetime
{
    private readonly WebHost _host = WebAppBuilder.Build();
    private readonly List<string> _middlewareLog = [];
    private readonly TaskCompletionSource<int> _statusSeenAfterNext = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseSlow = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _slowStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private HttpClient _client = null!;

    public enum Color
    {
        Red,
        Green,
    }

    private sealed record Person(string? Name, int Age);

    public async Task InitializeAsync()
    {
        _host
            .Use(new Middleware(async (ctx, next) =>
            {
                lock (_middlewareLog) _middlewareLog.Add("before");
                ctx.Response.Headers.Set("x-mw", "1");
                await next();
                lock (_middlewareLog) _middlewareLog.Add("after");
                if (ctx.Request.Headers["x-observe"] is not null)
                {
                    _statusSeenAfterNext.TrySetResult(ctx.Response.StatusCode);
                }
            }))
            .Use(new Middleware(async (ctx, next) =>
            {
                if (ctx.Request.Headers["x-block"] is not null)
                {
                    await Results.Forbidden(ctx, "blocked");
                    return;
                }

                await next();
            }))
            .Use(new Middleware(async (ctx, next) =>
            {
                // Middlewares can see route values because routing happens before the pipeline.
                if (Request.GetRouteValues(ctx).TryGetValue("id", out var id))
                {
                    ctx.Response.Headers.Set("x-route-id", id);
                }

                await next();
            }))
            .Get("/books", ctx => Results.Ok(ctx, new { q = Request.GetQueryParam<string>(ctx, "q") }))
            .Get("/books/:id", ctx => Results.Ok(ctx, new { id = Request.GetRouteParam<int>(ctx, "id") }))
            .Get("/books/new", ctx => Results.Ok(ctx, new { page = "new" }))
            .Get("/files/*path", ctx => Results.Ok(ctx, new { path = Request.GetRouteParam<string>(ctx, "path") }))
            .Get("/typed", ctx => Results.Ok(ctx, new
            {
                n = Request.GetQueryParam<int?>(ctx, "n"),
                d = Request.GetQueryParam<double>(ctx, "d"),
                c = Request.GetQueryParam<Color>(ctx, "c"),
                g = Request.GetQueryParam<Guid>(ctx, "g"),
            }))
            .Post("/people", async ctx =>
            {
                var person = await Request.GetBody<Person>(ctx);
                await Results.Ok(ctx, person ?? new Person(null, -1));
            })
            .Get("/boom", _ => throw new InvalidOperationException("kaboom"))
            .Get("/slow", async ctx =>
            {
                _slowStarted.TrySetResult();
                await _releaseSlow.Task;
                await Results.Ok(ctx, new { done = true });
            })
            .Get("/hang", async ctx =>
            {
                _slowStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite);
            })
            .Get("/café", ctx => Results.Ok(ctx, new { page = "café" }))
            .Get("/selfstop-hang", async ctx =>
            {
                await _host.StopAsync(TimeSpan.FromMilliseconds(300));
                await Task.Delay(Timeout.Infinite);
            })
            .Get("/selfstop", async ctx =>
            {
                await _host.StopAsync(TimeSpan.FromSeconds(10));
                await Results.Ok(ctx, new { stopped = true });
            })
            .Post("/books/:id", ctx => Results.Ok(ctx, new { posted = true }))
            .Put("/books/new", ctx => Results.Ok(ctx, new { put = true }))
            .AddRoute("/any", ctx => Results.Ok(ctx, new { method = ctx.Request.HttpMethod }));

        var port = await StartOnFreePortAsync(_host);
        _client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}/"), Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
    }

    [Fact]
    public async Task Start_ReturnsWhileServerKeepsRunning()
    {
        Assert.True(_host.IsRunning);
        var response = await _client.GetAsync("books");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task QueryParameters_AreBound()
    {
        var body = await _client.GetFromJsonAsync<Dictionary<string, string>>("books?q=Book1");
        Assert.Equal("Book1", body!["q"]);
    }

    [Fact]
    public async Task RouteParameters_AreBoundAndConverted()
    {
        var response = await _client.GetAsync("books/42");
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, int>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(42, body!["id"]);
        Assert.Equal("42", response.Headers.GetValues("x-route-id").Single());
    }

    [Fact]
    public async Task StaticRoute_BeatsParameterRoute()
    {
        var body = await _client.GetFromJsonAsync<Dictionary<string, string>>("books/new");
        Assert.Equal("new", body!["page"]);
    }

    [Fact]
    public async Task CatchAll_CapturesDecodedRemainder()
    {
        var body = await _client.GetFromJsonAsync<Dictionary<string, string>>("files/docs/my%20file.txt");
        Assert.Equal("docs/my file.txt", body!["path"]);
    }

    [Fact]
    public async Task TypedQueryParameters_UseInvariantCultureAndSupportNullableEnumGuid()
    {
        var guid = Guid.NewGuid();
        var response = await _client.GetStringAsync($"typed?n=7&d=1.5&c=green&g={guid}");
        Assert.Contains("\"n\":7", response);
        Assert.Contains("\"d\":1.5", response);
        Assert.Contains("\"c\":1", response);
        Assert.Contains(guid.ToString(), response);
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        var response = await _client.GetAsync("nope");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Route not found", await response.Content.ReadAsStringAsync());
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task WrongMethod_Returns405_WithAllowHeader()
    {
        var response = await _client.DeleteAsync("books/1");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(["GET", "HEAD", "OPTIONS", "POST"], response.Content.Headers.Allow.Order());
    }

    [Fact]
    public async Task AnyMethodRoute_AcceptsAllMethods()
    {
        var response = await _client.PutAsync("any", new StringContent(""));
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("PUT", body!["method"]);
    }

    [Fact]
    public async Task JsonBody_IsDeserializedCaseInsensitively()
    {
        var response = await _client.PostAsync(
            "people",
            new StringContent("{\"name\":\"Ada\",\"age\":36}", Encoding.UTF8, "application/json")
        );
        var person = await response.Content.ReadFromJsonAsync<Person>();
        Assert.Equal(new Person("Ada", 36), person);
    }

    [Fact]
    public async Task EmptyJsonBody_IsNull()
    {
        var response = await _client.PostAsync("people", new StringContent(""));
        var person = await response.Content.ReadFromJsonAsync<Person>();
        Assert.Equal(-1, person!.Age);
    }

    [Fact]
    public async Task InvalidJsonBody_Returns400()
    {
        var response = await _client.PostAsync("people", new StringContent("{not json", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task HandlerException_Returns500_AndServerKeepsServing()
    {
        var response = await _client.GetAsync("boom");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("kaboom", await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("books")).StatusCode);
    }

    [Fact]
    public async Task Middleware_WrapsHandler_AndCanSetHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "books");
        request.Headers.Add("x-observe", "1");
        var response = await _client.SendAsync(request);

        Assert.Equal("1", response.Headers.GetValues("x-mw").Single());
        // The middleware's code after `await next()` may run after the client already has the
        // response, so wait for it explicitly instead of racing.
        Assert.Equal(200, await _statusSeenAfterNext.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        lock (_middlewareLog)
        {
            Assert.Equal(["before", "after"], _middlewareLog);
        }
    }

    [Fact]
    public async Task Middleware_ObservesErrorResponses()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "boom");
        request.Headers.Add("x-observe", "1");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(500, await _statusSeenAfterNext.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Middleware_CanShortCircuit()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "books");
        request.Headers.Add("x-block", "yes");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Requests_AreProcessedConcurrently()
    {
        var slow = _client.GetAsync("slow");
        await _slowStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // /slow is blocked on a gate that only this test releases, so if requests were processed
        // serially this would time out.
        var fast = await _client.GetAsync("books").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.OK, fast.StatusCode);
        Assert.False(slow.IsCompleted);

        _releaseSlow.SetResult();
        Assert.Equal(HttpStatusCode.OK, (await slow).StatusCode);
    }

    [Fact]
    public async Task Head_FallsBackToGet_WithoutBody()
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, "books");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Head_SendsNoBodyBytesOnTheWire_AndKeepsConnectionUsable()
    {
        // HttpClient tolerates stray HEAD body bytes, so check the raw wire: after the HEAD
        // response headers nothing else may arrive, and the same keep-alive connection must then
        // serve a normal GET. (Sequential, not pipelined: HttpListener doesn't support pipelining.)
        using var tcp = new TcpClient("localhost", _client.BaseAddress!.Port);
        var stream = tcp.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes("HEAD /books?q=x HTTP/1.1\r\nHost: localhost\r\n\r\n"));
        var head = await ReadAvailableAsync(stream);
        Assert.StartsWith("HTTP/1.1 200", head);
        Assert.Contains("Content-Length: 9", head); // same headers as GET
        Assert.EndsWith("\r\n\r\n", head); // ...and no body bytes after them

        await stream.WriteAsync(Encoding.ASCII.GetBytes("GET /books?q=x HTTP/1.1\r\nHost: localhost\r\n\r\n"));
        var get = await ReadAvailableAsync(stream);
        Assert.StartsWith("HTTP/1.1 200", get);
        Assert.EndsWith("{\"q\":\"x\"}", get);
    }

    /// <summary>Reads until the peer has been quiet for a short while.</summary>
    private static async Task<string> ReadAvailableAsync(NetworkStream stream)
    {
        var text = new StringBuilder();
        var buffer = new byte[4096];
        while (true)
        {
            using var idle = new CancellationTokenSource(text.Length == 0 ? 5000 : 300);
            try
            {
                var read = await stream.ReadAsync(buffer, idle.Token);
                if (read == 0)
                {
                    return text.ToString();
                }

                text.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }
            catch (OperationCanceledException)
            {
                return text.ToString();
            }
        }
    }

    [Fact]
    public async Task StopAsync_CalledFromHandler_DoesNotWaitForItself()
    {
        var started = DateTime.UtcNow;
        var response = await _client.GetAsync("selfstop");
        var elapsed = DateTime.UtcNow - started;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, bool>>();
        Assert.True(body!["stopped"]);
        Assert.True(elapsed < TimeSpan.FromSeconds(5), $"took {elapsed}"); // timeout is 10 s

        // The listener closes right after that response completes.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_host.IsRunning || await IsAcceptingAsync())
        {
            Assert.True(DateTime.UtcNow < deadline, "listener did not close");
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task StopAsync_CalledFromHandlerThatHangs_StillClosesAtTheDeadline()
    {
        var response = await _client.GetAsync("selfstop-hang").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (await IsAcceptingAsync())
        {
            Assert.True(DateTime.UtcNow < deadline, "listener did not close");
            await Task.Delay(50);
        }
    }

    private async Task<bool> IsAcceptingAsync()
    {
        try
        {
            using var fresh = new HttpClient { BaseAddress = _client.BaseAddress, Timeout = TimeSpan.FromSeconds(2) };
            var r = await fresh.GetAsync("books");
            return r.StatusCode != HttpStatusCode.ServiceUnavailable;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    [Fact]
    public async Task Options_Returns204_WithAllowHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "books/new");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["GET", "HEAD", "OPTIONS", "POST", "PUT"], response.Content.Headers.Allow.Order());
    }

    [Fact]
    public async Task MethodNotAllowed_AllowHeader_IsUnionAcrossMatchingPatterns()
    {
        // GET /books/new (static), PUT /books/new (static), POST + GET /books/:id (param).
        var response = await _client.DeleteAsync("books/new");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(["GET", "HEAD", "OPTIONS", "POST", "PUT"], response.Content.Headers.Allow.Order());
    }

    [Fact]
    public async Task ParameterRoute_HandlesMethodTheStaticRouteLacks()
    {
        var response = await _client.PostAsync("books/new", new StringContent(""));
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, bool>>();
        Assert.True(body!["posted"]);
    }

    [Theory]
    [InlineData("files/a%2F..%2F..%2Fsecret")]
    [InlineData("files/a%5C..%5Csecret")]
    [InlineData("files/%2E%2E%2Fsecret")]
    public async Task EncodedDotSegments_InParameters_AreRejected(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NonAsciiStaticRoute_Matches()
    {
        var body = await _client.GetFromJsonAsync<Dictionary<string, string>>("café");
        Assert.Equal("café", body!["page"]);
    }

    [Fact]
    public async Task PostWithoutContentLength_DoesNotBreakServer()
    {
        // HttpClient always sends Content-Length, so use a raw socket. HttpListener answers 411
        // itself; the host must swallow the resulting disposed-response error quietly.
        using (var tcp = new TcpClient("localhost", _client.BaseAddress!.Port))
        {
            var stream = tcp.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes("POST /people HTTP/1.1\r\nHost: localhost\r\n\r\n"));
            var buffer = new byte[256];
            var read = await stream.ReadAsync(buffer);
            Assert.StartsWith("HTTP/1.1 411", Encoding.ASCII.GetString(buffer, 0, read));
        }

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("books")).StatusCode);
    }

    [Fact]
    public async Task ConfiguringOrRestartingAfterStart_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _host.Get("/late", _ => Task.CompletedTask));
        var startAgain = await Assert.ThrowsAsync<InvalidOperationException>(() => _host.Start("http://localhost:1/"));
        Assert.Contains("already running", startAgain.Message);

        await _host.StopAsync();
        var afterStop = Assert.Throws<InvalidOperationException>(() => _host.Use(new Middleware((_, next) => next())));
        Assert.Contains("stopped", afterStop.Message);
    }

    [Fact]
    public async Task StopAsync_StopsListening()
    {
        await _host.StopAsync();
        Assert.False(_host.IsRunning);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => _client.GetAsync("books"));
    }

    [Fact]
    public async Task StopAsync_DrainsInFlightRequests_WithTheirRealResponse()
    {
        var inFlight = _client.GetAsync("slow");
        await _slowStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stop = _host.StopAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(100);
        Assert.False(stop.IsCompleted); // waiting for /slow
        _releaseSlow.SetResult();

        var response = await inFlight;
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, bool>>();
        Assert.True(body!["done"]);
    }

    [Fact]
    public async Task StopAsync_Answers503ToRequestsThatOutliveTheTimeout_InsteadOfFaking200()
    {
        var inFlight = _client.GetAsync("hang");
        await _slowStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await _host.StopAsync(TimeSpan.FromMilliseconds(200)).WaitAsync(TimeSpan.FromSeconds(5));

        var response = await inFlight;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    /// <summary>
    /// Picks a free port and starts the host on it, retrying if another process grabs the port
    /// between probing and binding.
    /// </summary>
    internal static async Task<int> StartOnFreePortAsync(WebHost host)
    {
        for (var attempt = 0; ; attempt++)
        {
            var port = GetFreePort();
            try
            {
                await host.Start($"http://localhost:{port}");
                return port;
            }
            catch (HttpListenerException) when (attempt < 5)
            {
                // Port was taken in the meantime; try another one.
            }
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
