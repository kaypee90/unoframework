using System.Text.Json;
using unoframework;

var loggingMiddleware = new Middleware(async (context, next) =>
{
    Console.WriteLine($"Request: {context.Request.HttpMethod} {context.Request.Url}");
    await next();
    Console.WriteLine($"Response: {context.Response.StatusCode}");
});

var authMiddleware = new Middleware(async (context, next) =>
{
    var userType = context.Request.Headers.Get("Authorization") == null ? "Anonymous" : "Authenticated";
    Console.WriteLine($"User type: {userType}");
    context.Response.Headers.Set("x-user-type", userType);
    await next();
});

var framework = new MicroWebFramework();
framework.AddRoute("/", async (context) =>
{
    // Accessing the request object
    var request = context.Request;

    // Accessing specific properties of the request
    var httpMethod = request.HttpMethod;
    var url = request.Url?.ToString();
    var userAgent = request.UserAgent;
    // ... other properties and headers can be accessed in a similar way

    // Logic to handle the request
    // ...

    // Preparing and sending a response (as an example)
    var response = new UnoHttpResponse(context);
    var responseString = "<html><head><title></title><head><body> <h1>Received: " 
                         + httpMethod + " " + url + "</h1><br><h2>User Agent: " 
                         + userAgent + "</body></html>";
    await response.View(responseString);
});

framework.AddRoute("/health", async (context) =>
{
    // Accessing the request object
    var request = context.Request;

    // Accessing specific properties of the request
    var httpMethod = request.HttpMethod;
    var url = request.Url?.ToString();
    var userAgent = request.UserAgent;
    // ... other properties and headers can be accessed in a similar way

    // Logic to handle the request
    // ...

    // Preparing and sending a response (as an example)
    var response = new UnoHttpResponse(context);
    var jsonString = JsonSerializer.Serialize(new
    {
        userAgent,
        url,
        httpMethod,
        message = "healthy"
    });
    await response.Json(jsonString);
});

framework
    .Use(loggingMiddleware)
    .Use(authMiddleware);

await framework.Start("http://localhost:7071/");
Console.WriteLine("Press Enter to quit.");
Console.ReadLine();
framework.Stop();
