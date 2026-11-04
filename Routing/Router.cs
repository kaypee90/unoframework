namespace unoframework.Routing;

/// <summary>Maps (HTTP method, path) pairs to <see cref="RouteHandler"/>s using a <see cref="RadixTree{T}"/>.</summary>
/// <remarks>
/// <para>
/// Paths are compared case-sensitively against the percent-encoded request path
/// (<see cref="Uri.AbsolutePath"/>). Static parts of registered patterns are canonicalized the same
/// way, so <c>/café</c> matches a request for <c>/caf%C3%A9</c>. Parameter values are URL-decoded
/// before being handed to handlers. A trailing slash is ignored (<c>/books</c> == <c>/books/</c>).
/// </para>
/// <para>
/// <c>HEAD</c> falls back to the <c>GET</c> handler when no explicit <c>HEAD</c> handler exists.
/// A decoded parameter value that contains a <c>.</c> or <c>..</c> path segment (e.g. smuggled in
/// as <c>%2F..%2F</c>) is rejected with <see cref="RouteStatus.InvalidPath"/> so handlers can't be
/// tricked into path traversal.
/// </para>
/// <para>Register all routes before resolving concurrently; the router is not thread-safe for writes.</para>
/// </remarks>
public sealed class Router
{
    /// <summary>Pseudo-method for handlers that accept every HTTP method.</summary>
    public const string AnyMethod = "*";

    private readonly RadixTree<RouteEntry> _tree = new();
    private readonly HashSet<string> _registeredMethods = new(StringComparer.Ordinal);

    /// <summary>Registers a handler for every HTTP method.</summary>
    public void AddRoute(string path, RouteHandler routeHandler) =>
        AddRoute(AnyMethod, path, routeHandler);

    public void AddRoute(string method, string path, RouteHandler routeHandler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(routeHandler);

        var normalizedMethod = method.Trim().ToUpperInvariant();
        var pattern = CanonicalizePattern(path);
        var entry = _tree.GetOrAdd(pattern, () => new RouteEntry());
        if (!entry.Handlers.TryAdd(normalizedMethod, routeHandler))
        {
            throw new InvalidOperationException(
                $"A handler for {normalizedMethod} '{pattern}' is already registered."
            );
        }

        _registeredMethods.Add(normalizedMethod);
    }

    /// <summary>Resolves the handler for a request.</summary>
    /// <param name="method">The HTTP method (case-insensitive).</param>
    /// <param name="path">The percent-encoded absolute request path, e.g. <c>Uri.AbsolutePath</c>.</param>
    public RouteResult Resolve(string method, string path)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length == 0 || path[0] != '/')
        {
            return RouteResult.NotFound;
        }

        var normalizedPath = TrimTrailingSlash(path);
        var normalizedMethod = method.ToUpperInvariant();

        // HEAD is served by an explicit HEAD handler, else the GET handler. Both are considered in
        // a single match so a more specific GET route beats a less specific any-method route.
        string[] candidates = normalizedMethod == "HEAD" ? ["HEAD", "GET"] : [normalizedMethod];
        var match = _tree.Match(normalizedPath, e => e.AcceptsAny(candidates));
        if (match is not null)
        {
            var handler = match.Value.GetHandler(candidates);
            return TryDecodeParameters(match.Parameters, out var values)
                ? RouteResult.Found(handler, match.Pattern, values)
                : RouteResult.InvalidPath;
        }

        var allowed = GetAllowedMethods(normalizedPath);
        return allowed.Count == 0 ? RouteResult.NotFound : RouteResult.MethodNotAllowed(allowed);
    }

    /// <summary>
    /// Legacy lookup kept for compatibility with the pre-method-routing API: resolves as a
    /// <c>GET</c> request and returns the handler, or <c>null</c>.
    /// </summary>
    [Obsolete("Use Resolve(method, path), which supports HTTP methods and route parameters.")]
    public RouteHandler? GetRoute(string path) => Resolve("GET", path).Handler;

    /// <summary>Union of methods supported by every pattern matching <paramref name="path"/>.</summary>
    private List<string> GetAllowedMethods(string path)
    {
        var allowed = _registeredMethods
            .Where(m => m != AnyMethod && _tree.Match(path, e => e.Handlers.ContainsKey(m)) is not null)
            .ToList();
        if (allowed.Count == 0)
        {
            return allowed;
        }

        if (allowed.Contains("GET") && !allowed.Contains("HEAD"))
        {
            allowed.Add("HEAD");
        }

        if (!allowed.Contains("OPTIONS"))
        {
            allowed.Add("OPTIONS"); // answered automatically by the host
        }

        allowed.Sort(StringComparer.Ordinal);
        return allowed;
    }

    /// <summary>
    /// Trims the trailing slash and percent-encodes each static segment exactly as
    /// <see cref="Uri.AbsolutePath"/> would encode it in an incoming request.
    /// </summary>
    private static string CanonicalizePattern(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length > 0 && path[0] != '/')
        {
            throw new ArgumentException($"Route pattern '{path}' must start with '/'.", nameof(path));
        }

        if (path.IndexOfAny(['?', '#']) >= 0)
        {
            throw new ArgumentException($"Route pattern '{path}' may not contain '?' or '#'.", nameof(path));
        }

        var segments = TrimTrailingSlash(path).Split('/');
        for (var i = 1; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment is "." or "..")
            {
                throw new ArgumentException($"Route pattern '{path}' may not contain '.' or '..' segments.", nameof(path));
            }

            if (segment.Length > 0 && segment[0] is not (':' or '*'))
            {
                var canonical = new Uri("http://localhost/" + segment).AbsolutePath[1..];
                if (canonical.Length == 0 || canonical is "." or "..")
                {
                    // e.g. "%2E%2E", which Uri treats as a dot segment: unreachable by any request.
                    throw new ArgumentException(
                        $"Route pattern '{path}' contains an (encoded) dot segment '{segment}'.",
                        nameof(path)
                    );
                }

                segments[i] = canonical;
            }
        }

        return string.Join('/', segments);
    }

    private static string TrimTrailingSlash(string path)
    {
        var trimmed = path.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    private static bool TryDecodeParameters(
        IReadOnlyList<KeyValuePair<string, string>> raw,
        out RouteValues values
    )
    {
        values = RouteValues.Empty;
        if (raw.Count == 0)
        {
            return true;
        }

        var decoded = new Dictionary<string, string>(raw.Count, StringComparer.Ordinal);
        foreach (var (key, value) in raw)
        {
            var unescaped = Uri.UnescapeDataString(value);
            if (ContainsDotSegment(unescaped))
            {
                return false;
            }

            decoded[key] = unescaped;
        }

        values = new RouteValues(decoded);
        return true;
    }

    private static bool ContainsDotSegment(string value)
    {
        foreach (var segment in value.Split('/', '\\'))
        {
            if (segment is "." or "..")
            {
                return true;
            }
        }

        return false;
    }

    private sealed class RouteEntry
    {
        public Dictionary<string, RouteHandler> Handlers { get; } = new(StringComparer.Ordinal);

        // The most specific path that accepts the method wins. Static segments still beat
        // parameters, but a static route that lacks the method doesn't shadow a parameter route
        // that has it.
        public bool AcceptsAny(string[] methods) =>
            Handlers.ContainsKey(AnyMethod) || Array.Exists(methods, Handlers.ContainsKey);

        /// <summary>First handler registered for <paramref name="methods"/> (in order), else the any-method one.</summary>
        public RouteHandler GetHandler(string[] methods)
        {
            foreach (var method in methods)
            {
                if (Handlers.TryGetValue(method, out var handler))
                {
                    return handler;
                }
            }

            return Handlers[AnyMethod];
        }
    }
}

public enum RouteStatus
{
    Found,
    NotFound,
    MethodNotAllowed,

    /// <summary>A route matched but a parameter value is unsafe (contains a dot segment).</summary>
    InvalidPath,
}

/// <summary>Outcome of <see cref="Router.Resolve"/>.</summary>
public sealed class RouteResult
{
    public static readonly RouteResult NotFound = new(RouteStatus.NotFound, null, null, RouteValues.Empty, []);

    public static readonly RouteResult InvalidPath = new(RouteStatus.InvalidPath, null, null, RouteValues.Empty, []);

    private RouteResult(
        RouteStatus status,
        RouteHandler? handler,
        string? pattern,
        RouteValues values,
        IReadOnlyList<string> allowedMethods
    )
    {
        Status = status;
        Handler = handler;
        Pattern = pattern;
        Values = values;
        AllowedMethods = allowedMethods;
    }

    public RouteStatus Status { get; }

    public RouteHandler? Handler { get; }

    /// <summary>The registered pattern that matched, e.g. <c>/books/:id</c>.</summary>
    public string? Pattern { get; }

    public RouteValues Values { get; }

    /// <summary>Methods supported by the path when <see cref="Status"/> is MethodNotAllowed.</summary>
    public IReadOnlyList<string> AllowedMethods { get; }

    internal static RouteResult Found(RouteHandler handler, string pattern, RouteValues values) =>
        new(RouteStatus.Found, handler, pattern, values, []);

    internal static RouteResult MethodNotAllowed(IReadOnlyList<string> allowed) =>
        new(RouteStatus.MethodNotAllowed, null, null, RouteValues.Empty, allowed);
}
