using System.Net;
using System.Text;

namespace unoframework.ResponseHandlers;

internal sealed class UnoHttpResponse
{
    private readonly HttpListenerContext _context;

    public UnoHttpResponse(HttpListenerContext context)
    {
        _context = context;
    }

    public Task View(string body) => HandleResponse(body, "text/html; charset=utf-8", StatusCodes.Ok);

    public Task Json(string jsonString, StatusCodes statusCode = StatusCodes.Ok) =>
        HandleResponse(jsonString, "application/json; charset=utf-8", statusCode);

    public Task HandleInvalidRequests(string responseText, StatusCodes statusCode) =>
        HandleResponse(responseText, "text/plain; charset=utf-8", statusCode);

    private async Task HandleResponse(string responseContent, string contentType, StatusCodes statusCode)
    {
        var response = _context.Response;
        var buffer = Encoding.UTF8.GetBytes(responseContent);

        response.StatusCode = (int)statusCode;
        response.ContentType = contentType;
        response.ContentLength64 = buffer.Length;

        // HEAD responses carry the GET headers (incl. Content-Length) but never a body (RFC 9110
        // §9.3.2). The managed HttpListener would otherwise put the bytes on the wire and corrupt
        // the keep-alive connection. Handlers writing Response.OutputStream directly must check
        // for HEAD themselves (HttpListenerResponse.OutputStream can't be wrapped).
        if (!string.Equals(_context.Request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            await response.OutputStream.WriteAsync(buffer);
        }

        response.Close();
    }
}
