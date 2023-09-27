using EndpointPDK;
using Microsoft.AspNetCore.Http;

namespace TestEndpoint;

[Path("get", "/plug/test")]
public class TestEndpoint : IPluginEndpoint
{
    public async Task Execute(HttpContext ctx)
    {
        await ctx.Response.WriteAsync("test 2");
    }
}
