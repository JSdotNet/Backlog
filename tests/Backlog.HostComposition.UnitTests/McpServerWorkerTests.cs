using System.Net;
using System.Net.Sockets;

using Backlog.Desktop.Mcp;
using Backlog.Infrastructure.FileSystem;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// The desktop head's MCP listener, held to the one thing its constructor and a
/// port change must not do: build an ASP.NET host on the thread that asked.
/// <para>
/// That thread is the one about to draw the first window, or the Settings screen
/// a person just typed a port into. Building the listener composes a whole
/// <c>WebApplication</c> — Kestrel, routing, the MCP server and the bridge into
/// the head's container — so it belongs on the chain that binds it.
/// </para>
/// <para>
/// The proof is a head container that will not answer until the test lets it:
/// building the listener asks it which ports it composes, so a build on the
/// calling thread would hold that thread until the gate opened.
/// </para>
/// </summary>
public sealed class McpServerWorkerTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcp-worker-tests-" + Guid.NewGuid().ToString("N"));

    private readonly GatedProvider _services = new(new ServiceCollection().BuildServiceProvider());

    [Fact]
    public async Task Constructing_the_worker_returns_before_the_listener_is_built()
    {
        var settings = Settings(FreePort());
        _services.Close();

        var construct = Task.Run(() => new McpServerWorker(_services, settings), TestContext.Current.CancellationToken);
        var returned = await Returned(construct);

        _services.Open();
        Assert.True(returned, "The constructor waited on the head's container, so it built the listener in place.");

        using var worker = await construct;
        await ListeningAsync(worker, settings.McpServerPort);

        Assert.True(_services.Asked > 0);
    }

    [Fact]
    public async Task A_port_change_returns_before_the_new_listener_is_built()
    {
        var settings = Settings(FreePort());
        using var worker = new McpServerWorker(_services, settings);
        await ListeningAsync(worker, settings.McpServerPort);

        var moved = FreePort();
        _services.Close();

        var change = Task.Run(() => settings.SetMcpServerPort(moved), TestContext.Current.CancellationToken);
        var returned = await Returned(change);

        _services.Open();
        Assert.True(returned, "The port change waited on the head's container, so it built the listener in place.");
        Assert.Null(await change);

        await ListeningAsync(worker, moved);
    }

    /// <summary>A port change the window closes on top of is still given back:
    /// the listener built after disposal is let go rather than bound.</summary>
    [Fact]
    public async Task A_listener_built_after_disposal_is_never_bound()
    {
        var port = FreePort();
        var settings = Settings(port);
        _services.Close();

        var worker = new McpServerWorker(_services, settings);

        // Disposed while the build is still held at the gate, and only then let
        // through: the order is fixed, so the build always finds itself superseded.
        // Dispose gives up on the stuck chain after its own drain timeout.
        await Task.Run(worker.Dispose, TestContext.Current.CancellationToken).WaitAsync(Patience, TestContext.Current.CancellationToken);
        _services.Open();

        Assert.False(worker.IsListening);
        Assert.Null(worker.Address);

        using var probe = new TcpListener(IPAddress.Loopback, port);
        probe.Start();
    }

    public void Dispose()
    {
        _services.Open();

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

    private static async Task ListeningAsync(McpServerWorker worker, int port)
    {
        var deadline = DateTime.UtcNow + Patience;
        var changed = new SemaphoreSlim(0);
        worker.Changed += () => changed.Release();

        while (!(worker.IsListening && worker.Address?.Port == port))
        {
            Assert.Null(worker.LastError);
            var left = deadline - DateTime.UtcNow;
            Assert.True(left > TimeSpan.Zero, $"The listener never came up on {port}.");
            await changed.WaitAsync(left < TimeSpan.FromMilliseconds(200) ? left : TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Whether <paramref name="call"/> came back while the head's container
    /// was still refusing to answer.</summary>
    private static async Task<bool> Returned(Task call) =>
        await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)) == call;

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>The head's container, answering only while the gate is open.</summary>
    private sealed class GatedProvider(IServiceProvider inner) : IServiceProvider
    {
        private readonly ManualResetEventSlim _gate = new(initialState: true);
        private int _asked;

        public int Asked => Volatile.Read(ref _asked);

        public void Close() => _gate.Reset();

        public void Open() => _gate.Set();

        public object? GetService(Type serviceType)
        {
            Interlocked.Increment(ref _asked);
            _gate.Wait(Patience);
            return inner.GetService(serviceType);
        }
    }
}
