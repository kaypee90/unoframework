using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;

namespace unoframework.Routing;

/// <summary>URL-decoded route parameter values captured while matching a request path.</summary>
public sealed class RouteValues : IReadOnlyDictionary<string, string>
{
    public static readonly RouteValues Empty = new(new Dictionary<string, string>());

    private static readonly ConditionalWeakTable<HttpListenerContext, RouteValues> ByContext = new();

    private readonly IReadOnlyDictionary<string, string> _values;

    internal RouteValues(IReadOnlyDictionary<string, string> values) => _values = values;

    public string this[string key] => _values[key];

    public IEnumerable<string> Keys => _values.Keys;

    public IEnumerable<string> Values => _values.Values;

    public int Count => _values.Count;

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) =>
        _values.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Route values the host attached to <paramref name="context"/>, or <see cref="Empty"/>.</summary>
    public static RouteValues For(HttpListenerContext context) =>
        ByContext.TryGetValue(context, out var values) ? values : Empty;

    internal static void Attach(HttpListenerContext context, RouteValues values) =>
        ByContext.AddOrUpdate(context, values);
}
