using System.Net;

namespace unoframework.RequestHandlers;

public static class Request
{
    public static async Task<T?> GetBody<T>(HttpListenerContext context) where T : class
    {
        var request = GetRequest(context);
        return await request.PostAsync<T>();
    }

    private static UnoHttpRequest GetRequest(HttpListenerContext context) => new(context);
}