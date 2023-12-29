using System.Net;
using unoframework;

async Task LoggingMiddleware(HttpListenerContext context, Func<Task> next)
{
    Console.WriteLine($"Request: {context.Request.HttpMethod} {context.Request.Url}");
    await next();
    Console.WriteLine($"Response: {context.Response.StatusCode}");
}


var framework = new MicroWebFramework();
framework.AddRoute("/hello", async (context) =>
{
    // Accessing the request object
    var request = context.Request;

    // Accessing specific properties of the request
    var httpMethod = request.HttpMethod;
    var url = request.Url.ToString();
    var userAgent = request.UserAgent;
    // ... other properties and headers can be accessed in a similar way

    // Logic to handle the request
    // ...

    // Preparing and sending a response (as an example)
    var response = context.Response;
    var responseString = "<HTML><BODY> Received: " + httpMethod + " " + url + "</BODY></HTML>";
    byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);

    response.ContentLength64 = buffer.Length;
    var output = response.OutputStream;
    await output.WriteAsync(buffer, 0, buffer.Length);
    output.Close();
});

framework.Use(LoggingMiddleware);

framework.Start("http://localhost:7071/");
Console.WriteLine("Press Enter to quit.");
Console.ReadLine();
framework.Stop();
