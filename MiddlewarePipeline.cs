using System.Net;

namespace unoframework;


public class MiddlewarePipeline
{
    private readonly List<Middleware> _middlewares = [];

    public void Use(Middleware middleware)
    {
        _middlewares.Add(middleware);
    }

    public async Task Invoke(HttpListenerContext context)
    {
        var next = () => Task.CompletedTask;
        for (var i = _middlewares.Count - 1; i >= 0; i--)
        {
            var currentMiddleware = _middlewares[i];
            var previousNext = next;
            next = () => currentMiddleware.Invoke(context, previousNext);
        }
        await next();
    }
}
