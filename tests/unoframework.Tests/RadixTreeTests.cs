using unoframework.Routing;

namespace unoframework.Tests;

public class RadixTreeTests
{
    private static Dictionary<string, string> Params(RadixMatch<string> match) =>
        match.Parameters.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void Search_ReturnsValue_ForExactStaticMatch()
    {
        var tree = new RadixTree<string>();
        tree.Insert("/health", "health");

        Assert.Equal("health", tree.Search("/health"));
    }

    [Theory]
    [InlineData("/heal")]
    [InlineData("/healthz")]
    [InlineData("/")]
    [InlineData("")]
    public void Search_ReturnsNull_ForPrefixesAndExtensions(string path)
    {
        var tree = new RadixTree<string>();
        tree.Insert("/health", "health");

        Assert.Null(tree.Search(path));
    }

    [Fact]
    public void Insert_SplitsSharedPrefixes_AndKeepsAllRoutesReachable()
    {
        // These share "/" , "/b", "/boo" etc. and force several edge splits in different orders.
        string[] routes = ["/books", "/book", "/bo", "/b", "/bookstore", "/", "/bar", "/books/new"];
        var tree = new RadixTree<string>();
        foreach (var route in routes)
        {
            tree.Insert(route, route);
        }

        foreach (var route in routes)
        {
            Assert.Equal(route, tree.Search(route));
        }

        Assert.Null(tree.Search("/boo"));
        Assert.Null(tree.Search("/books/"));
        Assert.Equal(routes.Length, tree.Count);
    }

    [Fact]
    public void Insert_ManyRandomRoutes_BehavesLikeADictionary()
    {
        var random = new Random(1234);
        var expected = new Dictionary<string, string>();
        var tree = new RadixTree<string>();
        for (var i = 0; i < 2000; i++)
        {
            var length = random.Next(1, 12);
            var chars = new char[length];
            for (var c = 0; c < length; c++)
            {
                chars[c] = "abc/"[random.Next(4)];
            }

            var route = "/" + new string(chars);
            if (route.Contains("/:") || route.Contains("/*") || !expected.TryAdd(route, route))
            {
                continue;
            }

            tree.Insert(route, route);
        }

        foreach (var (route, value) in expected)
        {
            Assert.Equal(value, tree.Search(route));
        }

        Assert.Null(tree.Search("/zzz"));
        Assert.Equal(expected.Count, tree.Count);
    }

    [Fact]
    public void Match_CapturesNamedParameters()
    {
        var tree = new RadixTree<string>();
        tree.Insert("/users/:userId/posts/:postId", "post");

        var match = tree.Match("/users/42/posts/hello-world");

        Assert.NotNull(match);
        Assert.Equal("post", match.Value);
        Assert.Equal("/users/:userId/posts/:postId", match.Pattern);
        Assert.Equal(new Dictionary<string, string> { ["userId"] = "42", ["postId"] = "hello-world" }, Params(match));
    }

    [Theory]
    [InlineData("/users/")]
    [InlineData("/users")]
    [InlineData("/users/42/extra")]
    [InlineData("/users//")]
    public void Match_Parameter_RequiresExactlyOneNonEmptySegment(string path)
    {
        var tree = new RadixTree<string>();
        tree.Insert("/users/:id", "user");

        Assert.Null(tree.Match(path));
    }

    [Fact]
    public void Match_PrefersStaticOverParameter()
    {
        var tree = new RadixTree<string>();
        tree.Insert("/users/:id", "param");
        tree.Insert("/users/new", "static");

        Assert.Equal("static", tree.Search("/users/new"));
        Assert.Equal("param", tree.Search("/users/newer"));
        Assert.Equal("param", tree.Search("/users/ne"));
    }

    [Fact]
    public void Match_BacktracksFromDeadEndStaticBranch()
    {
        var tree = new RadixTree<string>();
        tree.Insert("/users/new", "static");
        tree.Insert("/users/:id/edit", "edit");

        var match = tree.Match("/users/new/edit");

        Assert.NotNull(match);
        Assert.Equal("edit", match.Value);
        Assert.Equal("new", Params(match)["id"]);
    }

    [Fact]
    public void Match_BacktrackingDiscardsParametersOfAbandonedBranches()
    {
        var tree = new RadixTree<string>();
        tree.Insert("/p/:a/x", "param");
        tree.Insert("/p/*rest", "catch-all");

        var match = tree.Match("/p/1/y");

        Assert.NotNull(match);
        Assert.Equal("catch-all", match.Value);
        Assert.Equal(new Dictionary<string, string> { ["rest"] = "1/y" }, Params(match));
    }

    [Fact]
    public void Match_CatchAll_CapturesRemainderIncludingSlashes()
    {
        var tree = new RadixTree<string>();
        tree.Insert("/files/*path", "files");

        var match = tree.Match("/files/docs/2024/report.pdf");

        Assert.NotNull(match);
        Assert.Equal("docs/2024/report.pdf", Params(match)["path"]);
        Assert.Null(tree.Match("/files/"));
        Assert.Null(tree.Match("/files"));
    }

    [Fact]
    public void Match_Priority_IsStaticThenParameterThenCatchAll()
    {
        var tree = new RadixTree<string>();
        tree.Insert("/a/*rest", "catch-all");
        tree.Insert("/a/:id", "param");
        tree.Insert("/a/b", "static");

        Assert.Equal("static", tree.Search("/a/b"));
        Assert.Equal("param", tree.Search("/a/c"));
        Assert.Equal("catch-all", tree.Search("/a/c/d"));
    }

    [Fact]
    public void Match_RootCatchAll_DoesNotShadowOtherRoutes()
    {
        var tree = new RadixTree<string>();
        tree.Insert("/*path", "spa");
        tree.Insert("/api/health", "health");
        tree.Insert("/", "root");

        Assert.Equal("health", tree.Search("/api/health"));
        Assert.Equal("root", tree.Search("/"));
        Assert.Equal("spa", tree.Search("/api/other"));
    }

    [Fact]
    public void Match_ColonOrStarInsideSegment_IsLiteral()
    {
        var tree = new RadixTree<string>();
        tree.Insert("/v1:batch", "batch");
        tree.Insert("/a*b", "star");

        Assert.Equal("batch", tree.Search("/v1:batch"));
        Assert.Equal("star", tree.Search("/a*b"));
        Assert.Null(tree.Search("/v1x"));
    }

    [Fact]
    public void Match_UsesAcceptPredicate_AndFallsBackToLowerPriorityRoutes()
    {
        var tree = new RadixTree<string>();
        tree.Insert("/users/new", "GET");
        tree.Insert("/users/:id", "POST");

        Assert.Equal("POST", tree.Match("/users/new", v => v == "POST")?.Value);
        Assert.Equal("GET", tree.Match("/users/new", v => v == "GET")?.Value);
        Assert.Null(tree.Match("/users/new", v => v == "PUT"));
    }

    [Fact]
    public void Match_IsCaseSensitive()
    {
        var tree = new RadixTree<string>();
        tree.Insert("/Books", "books");

        Assert.Null(tree.Search("/books"));
    }

    [Fact]
    public void Insert_Throws_OnDuplicatePattern()
    {
        var tree = new RadixTree<string>();
        tree.Insert("/users/:id", "a");

        Assert.Throws<InvalidOperationException>(() => tree.Insert("/users/:id", "b"));
    }

    [Fact]
    public void Insert_Throws_WhenParameterNamesConflictAtSamePosition()
    {
        var tree = new RadixTree<string>();
        tree.Insert("/users/:id", "a");

        Assert.Throws<InvalidOperationException>(() => tree.Insert("/users/:name/posts", "b"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("users")]
    [InlineData("/users/:")]
    [InlineData("/files/*")]
    [InlineData("/files/*path/more")]
    [InlineData("/a/:id/b/:id")]
    [InlineData("/a/:i:d")]
    public void Insert_Throws_OnInvalidPatterns(string pattern)
    {
        var tree = new RadixTree<string>();

        Assert.Throws<ArgumentException>(() => tree.Insert(pattern, "x"));
    }

    [Fact]
    public void GetOrAdd_ReturnsExistingValue()
    {
        var tree = new RadixTree<List<int>>();
        var first = tree.GetOrAdd("/a/:id", () => [1]);
        var second = tree.GetOrAdd("/a/:id", () => [2]);

        Assert.Same(first, second);
        Assert.Equal(1, tree.Count);
    }

    [Theory]
    [InlineData("/x/:a/:a", "/x/:b")]
    [InlineData("/g/:id/*", "/g/:name")]
    [InlineData("/f/*rest/more", "/f/*path")]
    [InlineData("/h/:a/x/:a", "/h/:b/y")]
    public void Insert_RejectedPattern_LeavesTreeUnchanged(string invalid, string validAfterwards)
    {
        var tree = new RadixTree<string>();

        Assert.Throws<ArgumentException>(() => tree.Insert(invalid, "bad"));
        tree.Insert(validAfterwards, "good"); // must not report a phantom conflict

        Assert.Equal(1, tree.Count);
    }

    [Fact]
    public void Insert_ConflictingPattern_LeavesTreeUnchanged()
    {
        var tree = new RadixTree<string>();
        tree.Insert("/u/:id/a", "a");
        tree.Insert("/u/new/:y/*rest", "b");

        // Conflicts deep in the pattern, after new static/param nodes would already be needed.
        Assert.Throws<InvalidOperationException>(() => tree.Insert("/u/:name/b", "x"));
        Assert.Throws<InvalidOperationException>(() => tree.Insert("/u/new/:z/c", "x"));
        Assert.Throws<InvalidOperationException>(() => tree.Insert("/u/new/:y/*path", "x"));

        tree.Insert("/u/:id/b", "c");
        tree.Insert("/u/new/:y/c", "d");
        Assert.Equal("c", tree.Search("/u/1/b"));
        Assert.Equal("d", tree.Search("/u/new/1/c"));
        Assert.Equal(4, tree.Count);
    }

    [Fact]
    public void SupportsValueTypes_AndDefaultValues()
    {
        var tree = new RadixTree<int>();
        tree.Insert("/zero", 0);
        tree.Insert("/one", 1);

        Assert.Equal(0, tree.Match("/zero")?.Value);
        Assert.Equal(1, tree.Search("/one"));
        Assert.Null(tree.Match("/two"));
    }
}
