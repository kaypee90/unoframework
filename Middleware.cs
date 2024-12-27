using System.Net;

namespace unoframework;

public sealed class Middleware
{
    public Func<HttpListenerContext, Func<Task>, Task> Invoke { get; }

    public Middleware(Func<HttpListenerContext, Func<Task>, Task> invoke)
    {
        Invoke = invoke;
    }
}
