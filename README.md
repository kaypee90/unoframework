# UNO Framework (Not for prod use)

UNO Framework is an experimental micro HTTP framework that utilizes Radix Tree for implementing basic routing functionality.

## Overview

UNO Framework aims to explore the feasibility and performance of using Radix Tree data structure for routing in a micro framework setting. By leveraging the efficiency of Radix Tree, UNO Framework aims to provide a lightweight and high-performance solution for handling HTTP requests and routing them to appropriate handlers.

## Features

- **Radix Tree Routing**: UNO Framework utilizes Radix Tree data structure for efficient routing of HTTP requests based on URL paths.
  
- **Basic Middleware Support**: UNO Framework supports basic middleware functionality for intercepting and processing HTTP requests before they are routed to handlers.

- **Minimalistic Design**: UNO Framework follows a minimalistic design philosophy, focusing on simplicity and performance without sacrificing functionality.

## Routing

Routes are stored in a compressed radix tree (`Routing/RadixTree.cs`): static text lives on edges
that are split on their longest common prefix, so lookups cost O(path length).

```csharp
var app = WebAppBuilder.Build();

app.Get("/books", ListBooks)                 // static
   .Get("/books/:title", GetBook)            // named parameter (one segment)
   .Get("/files/*path", GetFile)             // catch-all (rest of the path, must be last)
   .Post("/create", Create)
   .AddRoute("/any", Any);                   // any HTTP method

await app.RunAsync("http://localhost:7071/", cancellationToken); // blocks until cancelled
// or: await app.Start(url); ... await app.StopAsync();          // Start returns once listening
```

Inside a handler: `Request.GetRouteParam<int>(context, "id")`, `Request.GetQueryParam<string>(context, "q")`,
`Request.GetBody<T>(context)`. Conversions use invariant culture and are strict: decimal digits
only for numbers (no `0x10`), defined names/values only for enums (combinations only for `[Flags]`).

Matching rules:

- Priority at each level is **static → `:param` → `*catchAll`**, with backtracking, so
  `/users/new` and `/users/:id/edit` can coexist.
- A static route that doesn't handle the request method doesn't hide a parameter route that does.
- Unknown path → `404`; known path with the wrong method → `405` with an `Allow` header listing
  every method any matching route supports.
- `HEAD` is answered by the `GET` handler unless a `HEAD` handler is registered; `Results.*`
  then sends the headers without a body. (A handler that writes `context.Response.OutputStream`
  directly must skip the body for `HEAD` itself.) `OPTIONS` is answered automatically with `204`
  and an `Allow` header.
- Trailing slashes are ignored (`/books` == `/books/`). Matching is case-sensitive and runs on the
  percent-encoded path; static parts of patterns are encoded the same way, so `/café` works.
  Parameter values are URL-decoded (`%2F` inside a parameter does not split segments).
- A decoded parameter containing a `.` or `..` segment (e.g. `/files/a%2F..%2Fsecret`) is rejected
  with `400`. Still validate paths yourself before touching the file system.
- Invalid patterns, conflicting parameter names at the same position (`/u/:id` vs `/u/:name`) and
  duplicate method+path registrations throw at startup; a rejected pattern leaves the tree unchanged.

Middlewares run in registration order **around** the route handler and may short-circuit by not
calling `next()`. Unhandled handler exceptions become `500` (`BadRequestException`, e.g. an invalid
JSON body, becomes `400`) *inside* the pipeline, so middlewares see those status codes after
`await next()`. Exceptions thrown by a middleware itself are also turned into `500`.

`StopAsync()` shuts down gracefully: new requests get `503`, in-flight requests get up to 10 s
(configurable) to finish, and anything still running after that is answered with `503`.
A stopped host can't be restarted. Calling `StopAsync()` from inside a handler works (that request
isn't waited for and the listener closes once it completes, within the same deadline), but
cancelling the token passed to `RunAsync` is the cleaner way.

## Tests

```sh
dotnet test
```
