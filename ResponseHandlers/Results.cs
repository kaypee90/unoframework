using System.Net;
using System.Text.Json;

namespace unoframework.ResponseHandlers;

public static class Results
{
   public static async Task Ok(HttpListenerContext context, string responseBody = "")
   {
      var response = GetResponse(context);
      await response.Json(responseBody);
   }

   public static async Task Ok(HttpListenerContext context, object responseBody)
   {
      var jsonString = JsonSerializer.Serialize(responseBody);
      await Ok(context, jsonString);
   }
   
   public static async Task View(HttpListenerContext context, string responseBody = "")
   {
      var response = GetResponse(context);
      await response.View(responseBody);
   }
   
   public static async Task Forbidden(HttpListenerContext context, string responseBody = "")
   {
      var response = GetResponse(context);
      await response.HandleInvalidRequests(responseBody, StatusCodes.Forbidden);
   }
   
   public static async Task BadRequest(HttpListenerContext context, string responseBody = "")
   {
      var response = GetResponse(context);
      await response.HandleInvalidRequests(responseBody, StatusCodes.BadRequest);
   }
   
   public static async Task NotFound(HttpListenerContext context, string responseBody = "")
   {
      var response = GetResponse(context);
      await response.HandleInvalidRequests(responseBody, StatusCodes.NotFound);
   }
   
   private static UnoHttpResponse GetResponse(HttpListenerContext context) => new(context);
}