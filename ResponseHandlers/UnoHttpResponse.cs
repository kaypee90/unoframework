using System.Net;

namespace unoframework.ResponseHandlers;

internal sealed class UnoHttpResponse
{
   private readonly HttpListenerContext _context;
   public UnoHttpResponse(HttpListenerContext context)
   {
      _context = context;
   }
   
   public async Task View(string body)
      => await HandleResponse(body, "text/html");

   public async Task Json(string jsonString, StatusCodes statusCode = StatusCodes.Ok)
      => await HandleResponse(jsonString, "application/json", statusCode);

   private async Task HandleResponse(string responseContent, string contentType, StatusCodes statusCode = StatusCodes.Ok)
   {
      SetStatusCode(statusCode);
      var response = _context.Response;
      var buffer = System.Text.Encoding.UTF8.GetBytes(responseContent);

      response.ContentLength64 = buffer.Length;
      response.ContentType = contentType;
      var output = response.OutputStream;
      await output.WriteAsync(buffer);
      output.Close();  
   }
   
   public async Task HandleInvalidRequests(string responseText, StatusCodes statusCode)
   {
      SetStatusCode(statusCode);
      await _context.Response.OutputStream.WriteAsync(
         System.Text.Encoding.UTF8.GetBytes(responseText)
      );
      _context.Response.Close();
   }
   
   private void SetStatusCode(StatusCodes statusCode) => _context.Response.StatusCode = (int)statusCode; 
}