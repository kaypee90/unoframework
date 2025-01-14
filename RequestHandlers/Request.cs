using System.Net;

namespace unoframework.RequestHandlers;

public static class Request
{
    public static async Task<T?> GetBody<T>(HttpListenerContext context)
        where T : class
    {
        var request = GetRequest(context);
        return await request.PostAsync<T>();
    }

    public static T? GetQueryParam<T>(HttpListenerContext context, string key)
    {
        var request = GetRequest(context);
        return request.GetQueryParam<T>(key, out var value) ? value : default;
    }

    private static UnoHttpRequest GetRequest(HttpListenerContext context) => new(context);
}

