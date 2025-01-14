using System.Net;
using System.Text.Json;

namespace unoframework.RequestHandlers;

internal sealed class UnoHttpRequest
{
    private readonly HttpListenerContext _context;

    public UnoHttpRequest(HttpListenerContext context)
    {
        _context = context;
    }

    public async Task<T?> PostAsync<T>()
        where T : class
    {
        var request = _context.Request;
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var body = await reader.ReadToEndAsync();

        var data = JsonSerializer.Deserialize<T>(body);
        return data;
    }

    public bool GetQueryParam<T>(string key, out T? value)
    {
        var parameters = _context.Request.QueryString;
        value = default;

        if (!parameters.HasKeys() || parameters[key] == null)
        {
            return false;
        }

        try
        {
            var paramValue = parameters[key];
            if (paramValue == null)
            {
                return false;
            }

            // Attempt to convert to the specified type
            if (typeof(T).IsEnum)
            {
                value = (T)Enum.Parse(typeof(T), paramValue);
            }
            else
            {
                value = (T)Convert.ChangeType(paramValue, typeof(T));
            }

            return true;
        }
        catch
        {
            // If conversion fails, return false
            return false;
        }
    }
}

