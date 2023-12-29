namespace unoframework;

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

public class MicroWebFramework
{
    private readonly HttpListener _listener;
    private readonly RadixTree<RouteHandler> _routeTree;
    private readonly List<Func<HttpListenerContext, Task>> _middlewares;

    public delegate Task RouteHandler(HttpListenerContext context);
    public delegate Task Middleware(HttpListenerContext context, Func<Task> next);

    public MicroWebFramework()
    {
        _listener = new HttpListener();
        _routeTree = new RadixTree<RouteHandler>();
        _middlewares = new List<Func<HttpListenerContext, Task>>();
    }

    public void Use(Middleware middleware)
    {
        _middlewares.Add(async (context) =>
        {
            await middleware(context, async () => await ProcessRequest(context));
        });
    }

    public void Start(string url)
    {
        _listener.Prefixes.Add(url);
        _listener.Start();
        Console.WriteLine($"Listening on {url}");

        Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                var context = _listener.GetContext();
                await ExecuteMiddlewares(context);
            }
        });
    }

    private async Task ExecuteMiddlewares(HttpListenerContext context)
    {
        foreach (var middleware in _middlewares)
        {
            await middleware(context);
        }
    }

    public void Stop()
    {
        _listener.Stop();
    }

    public void AddRoute(string path, RouteHandler handler)
    {
        _routeTree.Insert(path, handler);
    }

    private async Task ProcessRequest(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath;
        var handler = _routeTree.Search(path);

        if (handler != null)
        {
            await handler(context);
        }
        else
        {
            context.Response.StatusCode = 404;
            await context.Response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("Not Found"));
            context.Response.Close();
        }
    }
}
