using Backlog.Desktop.Mcp;
using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.DevPc.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// The desktop head's <see cref="IMcpEndpointSource"/>, held to what the Settings
/// page's MCP server card reads and does through it: whether the listener holds its
/// port and where, the port edited through the workspace settings, and a Retry that
/// reaches the worker. Each over a real <see cref="McpServerWorker"/> on loopback,
/// because what is being proved is that the source answers for the worker rather
/// than for a stand-in of it.
/// </summary>
public sealed class DesktopMcpEndpointSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcp-source-tests-" + Guid.NewGuid().ToString("N"));

    private readonly IServiceProvider _services = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task A_listening_server_is_reported_listening_at_its_loopback_address()
    {
        var settings = Settings(FreePort());
        using var worker = new McpServerWorker(_services, settings);
        using var source = new DesktopMcpEndpointSource(settings, () => worker);

        await ListeningAsync(source, settings.McpServerPort);

        Assert.Equal(new Uri($"http://127.0.0.1:{settings.McpServerPort}/mcp"), source.Endpoint);
        Assert.Null(source.Unavailable);
    }

    [Fact]
    public async Task A_port_set_through_the_source_is_stored_and_the_listener_follows_it()
    {
        var settings = Settings(FreePort());
        using var worker = new McpServerWorker(_services, settings);
        using var source = new DesktopMcpEndpointSource(settings, () => worker);
        await ListeningAsync(source, settings.McpServerPort);

        var moved = FreePort();
        Assert.Null(source.SetPort(moved));

        Assert.Equal(moved, settings.McpServerPort);
        Assert.Equal(moved, source.Port);
        await ListeningAsync(source, moved);
    }

    [Fact]
    public void A_port_outside_the_usable_range_is_refused_with_the_store_s_sentence()
    {
        var port = FreePort();
        var settings = Settings(port);
        using var worker = new McpServerWorker(_services, settings);
        using var source = new DesktopMcpEndpointSource(settings, () => worker);

        Assert.Equal("Choose a port between 1024 and 65535.", source.SetPort(80));
        Assert.Equal(port, source.Port);
    }

    [Fact]
    public async Task Retry_through_the_source_binds_the_port_once_whatever_held_it_has_let_go()
    {
        var port = FreePort();
        var settings = Settings(port);

        McpServerWorker worker;
        DesktopMcpEndpointSource source;
        using (McpServerWorkerTests.Occupy(port))
        {
            worker = new McpServerWorker(_services, settings);
            source = new DesktopMcpEndpointSource(settings, () => worker);
            await McpServerWorkerTests.FailedAsync(worker);

            Assert.False(source.IsListening);
            Assert.Null(source.Endpoint);
            Assert.Contains($"Port {port} is already in use", source.Unavailable, StringComparison.Ordinal);
        }

        using (worker)
        using (source)
        {
            source.Retry();

            await ListeningAsync(source, port);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private WorkspaceSettingsStore Settings(int port)
    {
        var settings = new WorkspaceSettingsStore(Path.Combine(_root, "store-" + Guid.NewGuid().ToString("N")));
        Assert.Null(settings.SetMcpServerPort(port));
        return settings;
    }

    /// <summary>Waits on the source's own <see cref="IMcpEndpointSource.Changed"/>
    /// until it says the listener holds <paramref name="port"/>.</summary>
    private static async Task ListeningAsync(IMcpEndpointSource source, int port)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var changed = new SemaphoreSlim(0);
        void OnChanged() => changed.Release();
        source.Changed += OnChanged;

        try
        {
            while (!(source.IsListening && source.Endpoint?.Port == port))
            {
                Assert.Null(source.Unavailable);
                var left = deadline - DateTime.UtcNow;
                Assert.True(left > TimeSpan.Zero, $"The source never reported a listener on {port}.");
                await changed.WaitAsync(left < TimeSpan.FromMilliseconds(200) ? left : TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            source.Changed -= OnChanged;
        }
    }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}
