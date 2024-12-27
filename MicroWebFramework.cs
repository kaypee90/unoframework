namespace unoframework;

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

public class MicroWebFramework
{
    private readonly HttpListener _listener = new();
    private readonly RadixTree<RouteHandler> _routeTree = new();
    private readonly List<Middleware> _middlewares = [];
    private readonly MiddlewarePipeline _middlewarePipeline = new();

    public delegate Task RouteHandler(HttpListenerContext context);

    public MicroWebFramework Use(Middleware middleware)
    {
        _middlewares.Add(middleware);
        return this;
    }

    public async Task Start(string url)
    {
        _listener.Prefixes.Add(url);
        _listener.Start();

        foreach (var middleware in _middlewares)
        {
            _middlewarePipeline.Use(middleware);
        }
        
        Console.WriteLine($"Listening on {url}");

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
                    await HandleInvalidRequests(context, StatusCodes.BadRequest, "Invalid request");
                }
                else
                {
                    var handler = _routeTree.Search(path);
                    if (handler == null)
                    {
                        await HandleInvalidRequests(context, StatusCodes.NotFound, "Route not found");
                    }
                    else
                    {
                        await handler(context);
                    }
                }
            }
        });
    }
    public void Stop()
    {
        _listener.Stop();
    }

    public void AddRoute(string path, RouteHandler handler)
    {
        _routeTree.Insert(path, handler);
    }

    private static async Task HandleInvalidRequests(HttpListenerContext context, StatusCodes statusCode, string responseText)
    {
        context.Response.StatusCode = (int)statusCode;
        await context.Response.OutputStream.WriteAsync(
            System.Text.Encoding.UTF8.GetBytes(responseText)
        );
        context.Response.Close();
    }
}
