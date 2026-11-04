using System.Net;
using System.Runtime.InteropServices;
using unoframework;
using unoframework.Middlewares;
using unoframework.RequestHandlers;
using unoframework.ResponseHandlers;

var loggingMiddleware = new Middleware(
    async (context, next) =>
    {
        Console.WriteLine($"Request: {context.Request.HttpMethod} {context.Request.Url}");
        await next();
        Console.WriteLine($"Response: {context.Response.StatusCode}");
    }
);

var authMiddleware = new Middleware(
    async (context, next) =>
    {
        var userType =
            context.Request.Headers.Get("Authorization") == null ? "Anonymous" : "Authenticated";
        Console.WriteLine($"User type: {userType}");
        context.Response.Headers.Set("x-user-type", userType);
        await next();
    }
);

var app = WebAppBuilder.Build();

var books = new List<Book>
{
    new Book { Title = "Book1", Author = "Author 1" },
    new Book { Title = "Book2", Author = "Author 2" },
    new Book { Title = "Book3", Author = "Author 3" },
};

app.Get(
        "/",
        async (context) =>
        {
            var request = context.Request;
            var httpMethod = request.HttpMethod;
            var url = WebUtility.HtmlEncode(request.Url?.ToString());
            var userAgent = WebUtility.HtmlEncode(request.UserAgent);

            var responseString =
                "<html><head><title></title></head><body><h1>Received: "
                + httpMethod
                + " "
                + url
                + "</h1><br><h2>User Agent: "
                + userAgent
                + "</h2></body></html>";
            await Results.View(context, responseString);
        }
    )
    .Get(
        "/health",
        async (context) =>
        {
            var request = context.Request;
            var data = new
            {
                userAgent = request.UserAgent,
                url = request.Url?.ToString(),
                httpMethod = request.HttpMethod,
                message = "healthy",
            };
            await Results.Ok(context, data);
        }
    )
    // GET /books?q=Book1
    .Get(
        "/books",
        async (context) =>
        {
            var result = books;
            var searchParam = Request.GetQueryParam<string>(context, "q");
            if (!string.IsNullOrEmpty(searchParam))
            {
                result = books
                    .Where(b =>
                        b.Title != null
                        && b.Title.Contains(searchParam, StringComparison.OrdinalIgnoreCase)
                    )
                    .ToList();
            }

            await Results.Ok(context, result);
        }
    )
    // GET /books/Book1 — route parameter
    .Get(
        "/books/:title",
        async (context) =>
        {
            var title = Request.GetRouteParam<string>(context, "title");
            var book = books.FirstOrDefault(b =>
                string.Equals(b.Title, title, StringComparison.OrdinalIgnoreCase)
            );

            if (book is null)
            {
                await Results.NotFound(context, $"Book '{title}' not found");
                return;
            }

            await Results.Ok(context, book);
        }
    )
    // GET /files/docs/readme.txt — catch-all parameter
    .Get(
        "/files/*path",
        async (context) =>
            await Results.Ok(context, new { path = Request.GetRouteParam<string>(context, "path") })
    )
    .Post(
        "/create",
        async (context) =>
        {
            var body = await Request.GetBody<Person>(context);
            if (body is null)
            {
                await Results.BadRequest(context, "Request body is required");
                return;
            }

            var updatedPerson = new
            {
                body.Name,
                body.Age,
                Gender = "male",
                CountryOfBirth = "USA",
                IsAlive = true,
            };
            await Results.Ok(context, updatedPerson);
        }
    );

app.Use(loggingMiddleware).Use(authMiddleware);

// Run until Ctrl+C / SIGTERM. (Doesn't depend on stdin, so it also works under nohup, Docker, systemd.)
// Cancelling the signal keeps the process alive until RunAsync has drained in-flight requests.
using var shutdown = new CancellationTokenSource();
void RequestShutdown(PosixSignalContext signal)
{
    // First signal: graceful shutdown. A second one is let through to force-quit during the drain.
    signal.Cancel = !shutdown.IsCancellationRequested;
    shutdown.Cancel();
}
using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, RequestShutdown);
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, RequestShutdown);

const string url = "http://localhost:7071/";
Console.WriteLine("Press Ctrl+C to quit.");
await app.RunAsync(url, shutdown.Token);

class Person
{
    public string? Name { get; set; }
    public int Age { get; set; }
}

class Book
{
    public string? Title { get; set; }
    public string? Author { get; set; }
}
