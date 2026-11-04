using unoframework.Middlewares;

namespace unoframework.Tests;

public class MiddlewarePipelineTests
{
    [Fact]
    public async Task Invoke_RunsMiddlewaresInOrder_AroundTerminal()
    {
        var log = new List<string>();
        var pipeline = new MiddlewarePipeline();
        pipeline.Use(new Middleware(async (_, next) => { log.Add("a:before"); await next(); log.Add("a:after"); }));
        pipeline.Use(new Middleware(async (_, next) => { log.Add("b:before"); await next(); log.Add("b:after"); }));

        await pipeline.Invoke(null!, () => { log.Add("handler"); return Task.CompletedTask; });

        Assert.Equal(["a:before", "b:before", "handler", "b:after", "a:after"], log);
    }

    [Fact]
    public async Task Invoke_MiddlewareCanShortCircuit()
    {
        var handlerRan = false;
        var pipeline = new MiddlewarePipeline();
        pipeline.Use(new Middleware((_, _) => Task.CompletedTask));

        await pipeline.Invoke(null!, () => { handlerRan = true; return Task.CompletedTask; });

        Assert.False(handlerRan);
    }
}
