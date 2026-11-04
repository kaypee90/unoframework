using System.Net;
using System.Text.Json;

namespace unoframework.ResponseHandlers;

public static class Results
{
    /// <summary>Writes <paramref name="responseBody"/> verbatim as an <c>application/json</c> 200 response.</summary>
    public static Task Ok(HttpListenerContext context, string responseBody = "") =>
        GetResponse(context).Json(responseBody);

    /// <summary>Serializes <paramref name="responseBody"/> to JSON and writes a 200 response.</summary>
    public static Task Ok(HttpListenerContext context, object responseBody) =>
        Ok(context, JsonSerializer.Serialize(responseBody));

    public static Task View(HttpListenerContext context, string responseBody = "") =>
        GetResponse(context).View(responseBody);

    public static Task Forbidden(HttpListenerContext context, string responseBody = "") =>
        GetResponse(context).HandleInvalidRequests(responseBody, StatusCodes.Forbidden);

    public static Task BadRequest(HttpListenerContext context, string responseBody = "") =>
        GetResponse(context).HandleInvalidRequests(responseBody, StatusCodes.BadRequest);

    public static Task NotFound(HttpListenerContext context, string responseBody = "") =>
        GetResponse(context).HandleInvalidRequests(responseBody, StatusCodes.NotFound);

    /// <summary>Writes a 405 response with the mandatory <c>Allow</c> header (RFC 9110 §15.5.6).</summary>
    public static Task MethodNotAllowed(
        HttpListenerContext context,
        IEnumerable<string> allowedMethods,
        string responseBody = ""
    )
    {
        context.Response.Headers.Set("Allow", string.Join(", ", allowedMethods));
        return GetResponse(context).HandleInvalidRequests(responseBody, StatusCodes.MethodNotAllowed);
    }

    public static Task InternalServerError(HttpListenerContext context, string responseBody = "") =>
        GetResponse(context).HandleInvalidRequests(responseBody, StatusCodes.InternalServerError);

    public static Task ServiceUnavailable(HttpListenerContext context, string responseBody = "") =>
        GetResponse(context).HandleInvalidRequests(responseBody, StatusCodes.ServiceUnavailable);

    /// <summary>Writes an empty 204 response.</summary>
    public static Task NoContent(HttpListenerContext context)
    {
        context.Response.StatusCode = (int)StatusCodes.NoContent;
        context.Response.Close();
        return Task.CompletedTask;
    }

    private static UnoHttpResponse GetResponse(HttpListenerContext context) => new(context);
}
