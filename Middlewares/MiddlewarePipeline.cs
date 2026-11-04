using System.Net;

namespace unoframework.Middlewares;

/// <summary>
/// Runs middlewares in registration order around a terminal delegate (normally the route handler).
/// A middleware short-circuits the request by not calling <c>next</c>.
/// </summary>
public class MiddlewarePipeline
{
    private readonly List<Middleware> _middlewares = [];

    public void Use(Middleware middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _middlewares.Add(middleware);
    }

    public Task Invoke(HttpListenerContext context) => Invoke(context, () => Task.CompletedTask);

    public async Task Invoke(HttpListenerContext context, Func<Task> terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        var next = terminal;
        for (var i = _middlewares.Count - 1; i >= 0; i--)
        {
            var currentMiddleware = _middlewares[i];
            var previousNext = next;
            next = () => currentMiddleware.Invoke(context, previousNext);
        }

        await next();
    }
}
