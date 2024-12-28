namespace unoframework.Routing;

public sealed class Router
{
    private readonly RadixTree<RouteHandler> _routeTree = new();

    public void AddRoute(string path, RouteHandler routeHandler) => _routeTree.Insert(path, routeHandler);
    
    public RouteHandler? GetRoute(string path) => _routeTree.Search(path);
}