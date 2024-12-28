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

    public async Task<T?> PostAsync<T>() where T : class
    {
        var request = _context.Request;
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var body = await reader.ReadToEndAsync();
        
        var data = JsonSerializer.Deserialize<T>(body);
        return data;
    }
}