using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using EndpointPDK;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.UseMiddleware<PluginMiddleware>();

app.MapGet("/", () => "Hello World");

app.Run();

internal class PluginMiddleware
{
    private readonly RequestDelegate _next;

    public PluginMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        
        var alcRef = await Process(context);

        if (!context.Response.HasStarted)
        {
            await _next(context);
        }

        for (int i = 0; i < 10 && alcRef.IsAlive; i++)
        {
            // This is the better place for GC instaed of in Process finally block.
            // We are sure that stack don't keep references to variables because the method is ended.
            // Thus we can clear all the references from the stack.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Console.WriteLine($"Unloading Attempt: {i}");
        }

        Console.WriteLine($"Unloading Successful: {!alcRef.IsAlive}");
    }

    // This is for save reason to not allow dotnet inlining the method and leaking references in parent metohd 'Invoke'
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> Process(HttpContext ctx)
    {
        var path = "/Users/bochen/Tutorials/Dotnet/PluginArchitecture/TestEndpoint/bin/Debug/net6.0/TestEndpoint.dll";

        // This LoadFrom does't allow to rebuilding plugin dll while host is running.
        // Under the hood, it actually uses AssemblyLoadContext but in the default form.
        // var assembly = Assembly.LoadFrom(path);

        // This creates some space/scope in the namespace where we can manage assemblies
        var loadContext = new AssemblyLoadContext(path, isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(path);

            var endpointType = assembly.GetType("TestEndpoint.TestEndpoint");
            var pathInfo = endpointType?.GetCustomAttribute<PathAttribute>();

            if (
                pathInfo is not null
                && pathInfo.Method.Equals(ctx.Request.Method, StringComparison.OrdinalIgnoreCase)
                && pathInfo.Path.Equals(ctx.Request.Path, StringComparison.OrdinalIgnoreCase)
            )
            {
                var endpoint = Activator.CreateInstance(endpointType!) as IPluginEndpoint;

                if (endpoint is not null)
                {
                    await endpoint.Execute(ctx);
                }
            }
        }
        finally
        {
            // Unload() deletes markers/references only but the context will still alive in the memory.
            // Fully unlloaded is done when GC will take controll.
            loadContext.Unload();
        }

        return new WeakReference(loadContext);
    }
}