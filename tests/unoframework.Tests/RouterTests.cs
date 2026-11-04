using unoframework.Routing;

namespace unoframework.Tests;

public class RouterTests
{
    private static readonly RouteHandler A = _ => Task.CompletedTask;
    private static readonly RouteHandler B = _ => Task.CompletedTask;

    [Fact]
    public void Resolve_MatchesOnMethod()
    {
        var router = new Router();
        router.AddRoute("GET", "/books", A);
        router.AddRoute("POST", "/books", B);

        Assert.Same(A, router.Resolve("GET", "/books").Handler);
        Assert.Same(B, router.Resolve("post", "/books").Handler);
    }

    [Fact]
    public void Resolve_AnyMethodRoute_AcceptsEveryMethod_ButSpecificWins()
    {
        var router = new Router();
        router.AddRoute("/books", A);
        router.AddRoute("DELETE", "/books", B);

        Assert.Same(A, router.Resolve("GET", "/books").Handler);
        Assert.Same(A, router.Resolve("PATCH", "/books").Handler);
        Assert.Same(B, router.Resolve("DELETE", "/books").Handler);
    }

    [Fact]
    public void Resolve_ReturnsNotFound_ForUnknownPath()
    {
        var router = new Router();
        router.AddRoute("GET", "/books", A);

        var result = router.Resolve("GET", "/authors");

        Assert.Equal(RouteStatus.NotFound, result.Status);
        Assert.Null(result.Handler);
    }

    [Fact]
    public void Resolve_ReturnsMethodNotAllowed_WithUnionOfAllowedMethods()
    {
        var router = new Router();
        router.AddRoute("GET", "/users/new", A);
        router.AddRoute("POST", "/users/:id", B);
        router.AddRoute("PUT", "/users/:id", B);

        var result = router.Resolve("DELETE", "/users/new");

        Assert.Equal(RouteStatus.MethodNotAllowed, result.Status);
        Assert.Equal(["GET", "HEAD", "OPTIONS", "POST", "PUT"], result.AllowedMethods);
    }

    [Fact]
    public void Resolve_Head_FallsBackToGet_ButPrefersExplicitHead()
    {
        var router = new Router();
        router.AddRoute("GET", "/a", A);
        router.AddRoute("GET", "/b", A);
        router.AddRoute("HEAD", "/b", B);

        Assert.Same(A, router.Resolve("HEAD", "/a").Handler);
        Assert.Same(B, router.Resolve("HEAD", "/b").Handler);
    }

    [Fact]
    public void Resolve_Head_PrefersSpecificGetRoute_OverLessSpecificAnyMethodRoute()
    {
        var router = new Router();
        router.AddRoute("GET", "/books", A);
        router.AddRoute("/*rest", B); // e.g. SPA fallback

        var head = router.Resolve("HEAD", "/books");

        Assert.Same(A, head.Handler);
        Assert.Equal("/books", head.Pattern);
        Assert.Same(B, router.Resolve("HEAD", "/other").Handler);
    }

    [Fact]
    public void Resolve_AllowedMethods_OmitHeadWhenNoGet()
    {
        var router = new Router();
        router.AddRoute("POST", "/a", A);

        Assert.Equal(["OPTIONS", "POST"], router.Resolve("GET", "/a").AllowedMethods);
    }

    [Theory]
    [InlineData("/files/a%2F..%2Fsecret")]
    [InlineData("/files/..%5Csecret")]
    [InlineData("/files/.")]
    [InlineData("/users/%2E%2E")]
    public void Resolve_RejectsDotSegmentsInDecodedParameters(string path)
    {
        var router = new Router();
        router.AddRoute("GET", "/files/*path", A);
        router.AddRoute("GET", "/users/:id", A);

        Assert.Equal(RouteStatus.InvalidPath, router.Resolve("GET", path).Status);
    }

    [Theory]
    [InlineData("/files/a..b/c.txt", "a..b/c.txt")]
    [InlineData("/files/.hidden", ".hidden")]
    public void Resolve_AllowsDotsThatAreNotWholeSegments(string path, string expected)
    {
        var router = new Router();
        router.AddRoute("GET", "/files/*path", A);

        Assert.Equal(expected, router.Resolve("GET", path).Values["path"]);
    }

    [Theory]
    [InlineData("/café", "/caf%C3%A9")]
    [InlineData("/a b/:id", "/a%20b/1")]
    [InlineData("/x{y}", "/x%7By%7D")]
    public void AddRoute_CanonicalizesStaticSegmentsLikeIncomingRequests(string pattern, string requestPath)
    {
        var router = new Router();
        router.AddRoute("GET", pattern, A);

        Assert.Same(A, router.Resolve("GET", new Uri("http://localhost" + requestPath).AbsolutePath).Handler);
    }

    [Theory]
    [InlineData("/a?b")]
    [InlineData("/a#b")]
    [InlineData("/a/../b")]
    [InlineData("/a/./b")]
    [InlineData("/a/%2E%2E/b")]
    [InlineData("/a/%2e")]
    public void AddRoute_RejectsPatternsThatCanNeverMatch(string pattern)
    {
        var router = new Router();

        Assert.Throws<ArgumentException>(() => router.AddRoute("GET", pattern, A));
    }

#pragma warning disable CS0618 // legacy API kept for compatibility
    [Fact]
    public void GetRoute_LegacyApi_StillResolvesAnyMethodRoutes()
    {
        var router = new Router();
        router.AddRoute("/health", A);

        Assert.Same(A, router.GetRoute("/health"));
        Assert.Null(router.GetRoute("/missing"));
    }
#pragma warning restore CS0618

    [Fact]
    public void Resolve_StaticRouteWithoutMethod_DoesNotShadowParameterRouteWithMethod()
    {
        var router = new Router();
        router.AddRoute("GET", "/users/new", A);
        router.AddRoute("POST", "/users/:id", B);

        var result = router.Resolve("POST", "/users/new");

        Assert.Same(B, result.Handler);
        Assert.Equal("new", result.Values["id"]);
        Assert.Equal("/users/:id", result.Pattern);
    }

    [Theory]
    [InlineData("/books/", "/books")]
    [InlineData("/books", "/books/")]
    [InlineData("/", "/")]
    public void Resolve_IgnoresTrailingSlash(string registered, string requested)
    {
        var router = new Router();
        router.AddRoute("GET", registered, A);

        Assert.Same(A, router.Resolve("GET", requested).Handler);
    }

    [Fact]
    public void Resolve_UrlDecodesParameterValues_ButNotTheSegmentSeparator()
    {
        var router = new Router();
        router.AddRoute("GET", "/books/:title", A);

        var result = router.Resolve("GET", "/books/War%20%26%20Peace%2FVol1");

        Assert.Equal("War & Peace/Vol1", result.Values["title"]);
    }

    [Fact]
    public void Resolve_ReturnsNotFound_ForNonAbsolutePath()
    {
        var router = new Router();
        router.AddRoute("GET", "/*path", A);

        Assert.Equal(RouteStatus.NotFound, router.Resolve("OPTIONS", "*").Status);
    }

    [Fact]
    public void AddRoute_Throws_OnDuplicateMethodAndPath()
    {
        var router = new Router();
        router.AddRoute("GET", "/books", A);

        Assert.Throws<InvalidOperationException>(() => router.AddRoute("get", "/books/", B));
    }

    [Fact]
    public void AddRoute_Throws_ForRelativePattern()
    {
        var router = new Router();

        Assert.Throws<ArgumentException>(() => router.AddRoute("GET", "books", A));
    }
}
