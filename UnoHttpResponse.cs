using System.Net;

namespace unoframework;

public class UnoHttpResponse
{
   private readonly HttpListenerContext _context;
   public UnoHttpResponse(HttpListenerContext context)
   {
      _context = context;
   }
   
   public async Task View(string body)
      => await HandleResponse(body, "text/html");

   public async Task Json(string jsonString)
      => await HandleResponse(jsonString, "application/json"); 

   private async Task HandleResponse(string responseContent, string contentType)
   {
      var response = _context.Response;
      var buffer = System.Text.Encoding.UTF8.GetBytes(responseContent);

      response.ContentLength64 = buffer.Length;
      response.ContentType = contentType;
      var output = response.OutputStream;
      await output.WriteAsync(buffer);
      output.Close();  
   }
}