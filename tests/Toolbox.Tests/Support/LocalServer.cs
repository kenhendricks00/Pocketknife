using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Toolbox.Tests.Support;

/// <summary>A loopback HTTP server that serves byte arrays at fixed paths and counts requests.</summary>
public sealed class LocalServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private int _requests;

    private LocalServer(WebApplication app) => _app = app;

    public int Requests => _requests;

    public Uri BaseUri { get; private set; } = null!;

    public static async Task<LocalServer> StartAsync(IReadOnlyDictionary<string, (byte[] Body, string ContentType)> files)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var server = new LocalServer(app);
        app.Use(async (context, next) =>
        {
            Interlocked.Increment(ref server._requests);
            await next(context);
        });
        foreach (var (path, (body, contentType)) in files)
        {
            app.MapGet(path, () => Results.File(body, contentType, enableRangeProcessing: true));
        }

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        server.BaseUri = new Uri(address);
        return server;
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();
}
