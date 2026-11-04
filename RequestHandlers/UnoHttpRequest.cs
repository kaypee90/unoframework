using System.Net;
using System.Text.Json;
using unoframework.Routing;

namespace unoframework.RequestHandlers;

internal sealed class UnoHttpRequest
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpListenerContext _context;

    public UnoHttpRequest(HttpListenerContext context)
    {
        _context = context;
    }

    /// <summary>Deserializes the JSON body; returns <c>null</c> for an empty body.</summary>
    /// <exception cref="BadRequestException">The body is not valid JSON for <typeparamref name="T"/>.</exception>
    public async Task<T?> ReadJsonBodyAsync<T>()
        where T : class
    {
        var request = _context.Request;
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var body = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new BadRequestException("Request body is not valid JSON.", ex);
        }
    }

    public bool GetQueryParam<T>(string key, out T? value) =>
        ValueConverter.TryConvert(_context.Request.QueryString[key], out value);

    public bool GetRouteParam<T>(string key, out T? value)
    {
        value = default;
        return RouteValues.For(_context).TryGetValue(key, out var raw)
            && ValueConverter.TryConvert(raw, out value);
    }
}
