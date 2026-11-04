using System.Net;
using unoframework.Routing;

namespace unoframework.RequestHandlers;

public static class Request
{
    /// <summary>Deserializes the JSON request body (property names are case-insensitive).</summary>
    /// <returns>The body, or <c>null</c> when the body is empty.</returns>
    /// <exception cref="BadRequestException">The body is not valid JSON.</exception>
    public static Task<T?> GetBody<T>(HttpListenerContext context)
        where T : class => GetRequest(context).ReadJsonBodyAsync<T>();

    /// <summary>Returns the query-string value converted to <typeparamref name="T"/>, or default.</summary>
    public static T? GetQueryParam<T>(HttpListenerContext context, string key) =>
        GetRequest(context).GetQueryParam<T>(key, out var value) ? value : default;

    /// <summary>
    /// Returns the route parameter (e.g. <c>id</c> for <c>/books/:id</c>) converted to
    /// <typeparamref name="T"/>, or default when missing or not convertible.
    /// </summary>
    public static T? GetRouteParam<T>(HttpListenerContext context, string key) =>
        GetRequest(context).GetRouteParam<T>(key, out var value) ? value : default;

    /// <summary>All route parameters captured for this request.</summary>
    public static RouteValues GetRouteValues(HttpListenerContext context) => RouteValues.For(context);

    private static UnoHttpRequest GetRequest(HttpListenerContext context) => new(context);
}
