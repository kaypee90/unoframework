using unoframework;
using unoframework.Middlewares;
using unoframework.RequestHandlers;
using unoframework.ResponseHandlers;


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

var app = WebAppBuilder.Build();

// GET
app.AddRoute("/", async (context) =>
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
    var responseString = "<html><head><title></title><head><body> <h1>Received: " 
                         + httpMethod + " " + url + "</h1><br><h2>User Agent: " 
                         + userAgent + "</body></html>";
    await Results.View(context, responseString);
}).AddRoute("/health", async (context) =>
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
    var data = new
    {
        userAgent,
        url,
        httpMethod,
        message = "healthy"
    };
    await Results.Ok(context, data);
});

// POST
app.AddRoute("/create", async (context) =>
{
    var request = context.Request;
    if (request.HttpMethod == "POST")
    {
        var body = await Request.GetBody<Person>(context);
        var updatedPerson = new
        {
            body?.Name,
            body?.Age,
            Gender = "male",
            CountryOfBirth = "USA",
            IsAlive = true
        };
        await Results.Ok(context, updatedPerson);
    }
    else
    {
        await Results.Forbidden(context, "Request is not POST");
    }
});

app
    .Use(loggingMiddleware)
    .Use(authMiddleware);

await app.Start("http://localhost:7071/");
Console.WriteLine("Press Enter to quit.");
Console.ReadLine();
app.Stop();

class Person
{
    public string? Name { get; set; }
    public int Age { get; set; }
}
