using unoframework.ResponseHandlers;

namespace unoframework;

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Middlewares;
using Routing;

public sealed class WebHost
{
    private readonly HttpListener _listener = new();
    private readonly List<Middleware> _middlewares = [];
    private readonly MiddlewarePipeline _middlewarePipeline = new();
    private readonly Router _router = new();

    public WebHost Use(Middleware middleware)
    {
        _middlewares.Add(middleware);
        return this;
    }

    public WebHost AddRoute(string path, RouteHandler handler)
    {
        _router.AddRoute(path, handler);
        return this;
    }

    public async Task Start(string url) => await StartServer(url);

    public void Stop() => _listener.Stop();

    private async Task StartServer(string url)
    {
        foreach (var middleware in _middlewares)
        {
            _middlewarePipeline.Use(middleware);
        }
        
        Console.WriteLine($"Listening on {url}");
        
        _listener.Prefixes.Add(url);
        _listener.Start();

        await Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                var context = await _listener.GetContextAsync();
                await _middlewarePipeline.Invoke(context);

                Console.WriteLine(context.Request.Url?.ToString());

                // Respond to the request
                var path = context.Request.Url?.AbsolutePath;
                if (path is null)
                {
                    await Results.BadRequest(context, "Invalid request");
                }
                else
                {
                   await HandleRequest(path, context);
                }
            }
        });
    }

    private async Task HandleRequest(string path, HttpListenerContext context)
    {
        var handler = _router.GetRoute(path);
        if (handler == null)
        {
            await Results.NotFound(context, "Route not found");
        }
        else
        {
            await handler(context);
        } 
    }
}
