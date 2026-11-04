namespace unoframework.Routing;

/// <summary>
/// A compressed prefix tree (radix tree) for URL route patterns.
/// </summary>
/// <remarks>
/// <para>
/// Static text is stored on edges that are split on their longest common prefix, so a lookup
/// costs O(path length) instead of one dictionary hop per character.
/// </para>
/// <para>Supported pattern syntax (a parameter must occupy a whole path segment):</para>
/// <list type="bullet">
///   <item><c>/users/:id</c> – named parameter, matches exactly one non-empty segment.</item>
///   <item><c>/files/*path</c> – catch-all, matches the (non-empty) remainder; must be last.</item>
/// </list>
/// <para>
/// Matching priority at every node is: static child, then parameter, then catch-all. When a
/// higher-priority branch dead-ends the search backtracks, so <c>/users/new</c> and
/// <c>/users/:id/edit</c> can coexist. Each node can only be reached at one position of a given
/// path, so the backtracking is bounded by the size of the tree.
/// </para>
/// </remarks>
public sealed class RadixTree<T>
{
    private readonly Node _root = new(string.Empty);

    /// <summary>Number of patterns stored in the tree.</summary>
    public int Count { get; private set; }

    /// <summary>Adds a pattern. Throws if the exact pattern is already registered.</summary>
    public void Insert(string pattern, T value)
    {
        var node = GetOrCreateNode(pattern);
        if (node.HasValue)
        {
            throw new InvalidOperationException($"Route pattern '{pattern}' is already registered.");
        }

        Store(node, pattern, value);
    }

    /// <summary>Returns the value stored for <paramref name="pattern"/>, creating it if absent.</summary>
    public T GetOrAdd(string pattern, Func<T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var node = GetOrCreateNode(pattern);
        if (!node.HasValue)
        {
            Store(node, pattern, factory());
        }

        return node.Value!;
    }

    /// <summary>Returns the value of the best matching pattern, or <c>default</c>.</summary>
    public T? Search(string path)
    {
        var match = Match(path);
        return match is null ? default : match.Value;
    }

    /// <summary>
    /// Finds the highest-priority pattern matching <paramref name="path"/> whose value satisfies
    /// <paramref name="accept"/> (all values are accepted when it is <c>null</c>).
    /// Parameter values are returned exactly as they appear in the path (not URL-decoded).
    /// </summary>
    public RadixMatch<T>? Match(string path, Func<T, bool>? accept = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var parameters = new List<KeyValuePair<string, string>>();
        var node = Find(_root, path, 0, parameters, accept);
        return node is null ? null : new RadixMatch<T>(node.Value!, node.Pattern!, parameters);
    }

    private void Store(Node node, string pattern, T value)
    {
        node.Value = value;
        node.HasValue = true;
        node.Pattern = pattern;
        Count++;
    }

    /// <summary>
    /// Parses and fully validates <paramref name="pattern"/> (syntax and conflicts with existing
    /// routes) before touching the tree, so a rejected pattern leaves the tree unchanged.
    /// </summary>
    private Node GetOrCreateNode(string pattern)
    {
        var tokens = Tokenize(pattern);
        CheckWildcardConflicts(pattern, tokens);

        var node = _root;
        foreach (var token in tokens)
        {
            node = token.Kind switch
            {
                TokenKind.Static => InsertStatic(node, token.Text),
                TokenKind.Param => node.Param ??= new Node(string.Empty) { ParamName = token.Text },
                _ => node.CatchAll ??= new Node(string.Empty) { ParamName = token.Text },
            };
        }

        return node;
    }

    private static List<Token> Tokenize(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (pattern.Length == 0 || pattern[0] != '/')
        {
            throw new ArgumentException($"Route pattern '{pattern}' must start with '/'.", nameof(pattern));
        }

        var tokens = new List<Token>();
        var paramNames = new HashSet<string>(StringComparer.Ordinal);
        var pos = 0;
        while (pos < pattern.Length)
        {
            if (!IsParameterStart(pattern, pos))
            {
                var staticEnd = NextParameterStart(pattern, pos);
                tokens.Add(new Token(TokenKind.Static, pattern[pos..staticEnd]));
                pos = staticEnd;
                continue;
            }

            var end = SegmentEnd(pattern, pos);
            var name = pattern[(pos + 1)..end];
            ValidateParameterName(pattern, name, paramNames);

            var isCatchAll = pattern[pos] == '*';
            if (isCatchAll && end != pattern.Length)
            {
                throw new ArgumentException(
                    $"Catch-all '*{name}' must be the last segment of '{pattern}'.",
                    nameof(pattern)
                );
            }

            tokens.Add(new Token(isCatchAll ? TokenKind.CatchAll : TokenKind.Param, name));
            pos = end;
        }

        return tokens;
    }

    /// <summary>
    /// Walks the existing tree along <paramref name="tokens"/> without modifying it and throws if a
    /// wildcard would be registered under a different name than the one already at that position.
    /// </summary>
    private void CheckWildcardConflicts(string pattern, List<Token> tokens)
    {
        Node? node = _root;
        foreach (var token in tokens)
        {
            if (token.Kind == TokenKind.Static)
            {
                node = DescendStatic(node, token.Text);
            }
            else
            {
                var kind = token.Kind == TokenKind.Param ? ':' : '*';
                var existing = token.Kind == TokenKind.Param ? node.Param : node.CatchAll;
                if (existing is not null && existing.ParamName != token.Text)
                {
                    throw new InvalidOperationException(
                        $"Route '{pattern}' declares '{kind}{token.Text}' but '{kind}{existing.ParamName}' "
                            + "is already registered at the same position."
                    );
                }

                node = existing;
            }

            if (node is null)
            {
                return; // The rest of the pattern would create brand-new nodes: nothing to conflict with.
            }
        }
    }

    /// <summary>Follows static edges for <paramref name="text"/>; null if no node ends exactly there.</summary>
    private static Node? DescendStatic(Node node, ReadOnlySpan<char> text)
    {
        while (!text.IsEmpty)
        {
            if (
                !node.Children.TryGetValue(text[0], out var child)
                || !text.StartsWith(child.Prefix, StringComparison.Ordinal)
            )
            {
                return null;
            }

            node = child;
            text = text[child.Prefix.Length..];
        }

        return node;
    }

    private static void ValidateParameterName(string pattern, string name, HashSet<string> seen)
    {
        if (name.Length == 0)
        {
            throw new ArgumentException($"Route '{pattern}' has an unnamed parameter.", nameof(pattern));
        }

        if (name.IndexOfAny([':', '*']) >= 0)
        {
            throw new ArgumentException(
                $"Parameter name '{name}' in route '{pattern}' may not contain ':' or '*'.",
                nameof(pattern)
            );
        }

        if (!seen.Add(name))
        {
            throw new ArgumentException(
                $"Parameter '{name}' is declared more than once in route '{pattern}'.",
                nameof(pattern)
            );
        }
    }

    /// <summary>Walks static edges for <paramref name="text"/>, splitting edges where they diverge.</summary>
    private static Node InsertStatic(Node node, string pattern)
    {
        var text = pattern.AsSpan();
        while (!text.IsEmpty)
        {
            if (!node.Children.TryGetValue(text[0], out var child))
            {
                var leaf = new Node(text.ToString());
                node.Children[text[0]] = leaf;
                return leaf;
            }

            var common = text.CommonPrefixLength(child.Prefix);
            if (common < child.Prefix.Length)
            {
                // Split "child" into "split" (shared prefix) -> "child" (remaining suffix).
                var split = new Node(child.Prefix[..common]);
                child.Prefix = child.Prefix[common..];
                split.Children[child.Prefix[0]] = child;
                node.Children[split.Prefix[0]] = split;
                child = split;
            }

            node = child;
            text = text[common..];
        }

        return node;
    }

    private static Node? Find(
        Node node,
        string path,
        int pos,
        List<KeyValuePair<string, string>> parameters,
        Func<T, bool>? accept
    )
    {
        if (pos == path.Length)
        {
            return node.HasValue && (accept is null || accept(node.Value!)) ? node : null;
        }

        // 1. Static edge (at most one candidate, keyed by its first character).
        if (
            node.Children.TryGetValue(path[pos], out var child)
            && path.AsSpan(pos).StartsWith(child.Prefix, StringComparison.Ordinal)
        )
        {
            var found = Find(child, path, pos + child.Prefix.Length, parameters, accept);
            if (found is not null)
            {
                return found;
            }
        }

        // 2. Named parameter: consumes one non-empty segment.
        if (node.Param is { } param)
        {
            var end = SegmentEnd(path, pos);
            if (end > pos)
            {
                parameters.Add(new(param.ParamName!, path[pos..end]));
                var found = Find(param, path, end, parameters, accept);
                if (found is not null)
                {
                    return found;
                }

                parameters.RemoveAt(parameters.Count - 1);
            }
        }

        // 3. Catch-all: consumes the (non-empty) rest of the path.
        if (node.CatchAll is { HasValue: true } catchAll && (accept is null || accept(catchAll.Value!)))
        {
            parameters.Add(new(catchAll.ParamName!, path[pos..]));
            return catchAll;
        }

        return null;
    }

    private static bool IsParameterStart(string pattern, int pos) =>
        pos > 0 && pattern[pos - 1] == '/' && pattern[pos] is ':' or '*';

    private static int NextParameterStart(string pattern, int from)
    {
        for (var i = Math.Max(from, 1); i < pattern.Length; i++)
        {
            if (IsParameterStart(pattern, i))
            {
                return i;
            }
        }

        return pattern.Length;
    }

    private static int SegmentEnd(string path, int from)
    {
        var end = path.IndexOf('/', from);
        return end < 0 ? path.Length : end;
    }

    private enum TokenKind
    {
        Static,
        Param,
        CatchAll,
    }

    /// <summary>A static run of text, or a wildcard (Text is then the parameter name).</summary>
    private readonly record struct Token(TokenKind Kind, string Text);

    private sealed class Node(string prefix)
    {
        /// <summary>Static text consumed by the edge leading into this node (empty for wildcards).</summary>
        public string Prefix { get; set; } = prefix;

        /// <summary>Static children keyed by the first character of their prefix.</summary>
        public Dictionary<char, Node> Children { get; } = [];

        public Node? Param { get; set; }

        public Node? CatchAll { get; set; }

        /// <summary>Parameter name for <c>:name</c> / <c>*name</c> nodes.</summary>
        public string? ParamName { get; init; }

        public T? Value { get; set; }

        public bool HasValue { get; set; }

        public string? Pattern { get; set; }
    }
}

/// <summary>Result of a successful <see cref="RadixTree{T}.Match"/>.</summary>
public sealed record RadixMatch<T>(
    T Value,
    string Pattern,
    IReadOnlyList<KeyValuePair<string, string>> Parameters
);
