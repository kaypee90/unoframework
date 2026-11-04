# UNO Framework

> A small, experimental HTTP framework for .NET built around radix-tree routing.

UNO Framework is a lightweight project for exploring fast, predictable HTTP routing and a
minimal web-hosting API. It is intentionally small and is **not recommended for production use**.

## Highlights

- Radix-tree routing with static, named-parameter, and catch-all segments
- Middleware pipelines with short-circuiting support
- Typed query-string, route, and JSON body helpers
- Automatic `404`, `405`, `OPTIONS`, and `HEAD` handling
- Strict route validation and URL decoding
- Concurrent request processing
- Graceful shutdown with configurable draining of in-flight requests
- No external runtime dependencies

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## Quick start

Clone the repository and run the sample application:

```sh
git clone https://github.com/kaypee90/unoframework.git
cd unoframework
dotnet run
```

The sample server listens on `http://localhost:7071/`.

Try a few requests:

```sh
curl http://localhost:7071/health
curl 'http://localhost:7071/books?q=Book1'
curl http://localhost:7071/books/Book2
curl http://localhost:7071/files/docs/readme.txt
```

## A minimal application

```csharp
using unoframework;
using unoframework.Middlewares;
using unoframework.RequestHandlers;
using unoframework.ResponseHandlers;

var app = WebAppBuilder.Build();

app.Get("/health", async context =>
{
    await Results.Ok(context, new { status = "healthy" });
})
.Get("/books/:title", async context =>
{
    var title = Request.GetRouteParam<string>(context, "title");
    await Results.Ok(context, new { title });
})
.Post("/books", async context =>
{
    var book = await Request.GetBody<Book>(context);
    await Results.Ok(context, book);
});

await app.RunAsync("http://localhost:7071/", CancellationToken.None);

public sealed record Book(string Title);
```

`RunAsync` keeps the server running until its cancellation token is cancelled. If the
application needs to start listening without blocking, use `Start` and later call `StopAsync`:

```csharp
await app.Start("http://localhost:7071/");
// ... application work ...
await app.StopAsync();
```

## Routing

Routes are registered on `WebHost`:

```csharp
app.Get("/books", ListBooks)
   .Get("/books/:title", GetBook)   // one named path segment
   .Get("/files/*path", GetFile)    // remaining path; must be last
   .Post("/books", CreateBook)
   .AddRoute("/health", HealthCheck); // any HTTP method
```

The router stores routes in a compressed radix tree. At each level it tries branches in this
order:

1. Static text
2. Named parameter (`:name`)
3. Catch-all parameter (`*name`)

The matcher backtracks when a higher-priority branch cannot produce a valid route, so static and
parameterized routes can safely coexist.

### Route behavior

- Trailing slashes are ignored (`/books` matches `/books/`).
- Matching is case-sensitive.
- Parameters are URL-decoded and can be read with `Request.GetRouteParam<T>`.
- Query values can be read with `Request.GetQueryParam<T>`.
- `HEAD` falls back to `GET` when no explicit `HEAD` route exists.
- `OPTIONS` is handled automatically with a `204` response and an `Allow` header.
- Unknown paths return `404`.
- Known paths with an unsupported method return `405` and an `Allow` header.
- Invalid patterns, conflicting parameter names, and duplicate method/path registrations fail during
  setup.

## Middleware

Middleware runs in registration order around the route handler. It can add headers, perform
cross-cutting work, or stop the pipeline by not calling `next`.

```csharp
var logging = new Middleware(async (context, next) =>
{
    Console.WriteLine($"{context.Request.HttpMethod} {context.Request.Url}");
    await next();
    Console.WriteLine($"Response: {context.Response.StatusCode}");
});

app.Use(logging);
```

Unhandled handler exceptions become `500` responses. Invalid request data represented by
`BadRequestException` becomes `400`.

## Responses

Use the helpers in `unoframework.ResponseHandlers.Results` for common responses:

```csharp
await Results.Ok(context, new { message = "ok" });
await Results.View(context, "<h1>Hello</h1>");
await Results.NotFound(context, "Book not found");
await Results.BadRequest(context, "Invalid request");
await Results.NoContent(context);
```

Handlers that write directly to `context.Response.OutputStream` are responsible for omitting the
response body for `HEAD` requests.

## Graceful shutdown

`StopAsync` stops accepting new work, returns `503 Service Unavailable` to new requests, and
allows in-flight requests to finish for up to 10 seconds by default:

```csharp
await app.StopAsync(TimeSpan.FromSeconds(5));
```

The timeout is configurable. A stopped host cannot be restarted or reconfigured.

## Development

Build the project:

```sh
dotnet build
```

Run the test suite:

```sh
dotnet test
```

The repository includes focused tests for routing, radix-tree matching, middleware, typed value
conversion, request handling, and host lifecycle behavior.

## Project layout

| Path | Purpose |
| --- | --- |
| `Routing/` | Radix-tree implementation and route matching |
| `Middlewares/` | Middleware and pipeline execution |
| `RequestHandlers/` | Request parsing and typed value conversion |
| `ResponseHandlers/` | Response helpers and status handling |
| `WebHost.cs` | HTTP listener, request dispatch, and shutdown |
| `tests/` | Unit and integration tests |

## Status

UNO Framework is an experimental learning project. APIs and behavior may change without notice,
and the framework has not been hardened or supported for production workloads.
