using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// A Cosmos account that is configured but does not answer, and how long the
/// service takes to say so.
/// <para>
/// The shape is the one a local run has with Docker Desktop down: the AppHost's
/// proxy still owns the emulator's port and accepts the connection, and nothing
/// behind it ever replies. The SDK's first call reads the account (<c>GET /</c>)
/// with timeouts of 5, 10 and 20 seconds and no regard for the request's
/// cancellation, so a registration used to hang for a minute or more — long
/// past the desktop's own 10-second attempt timeout, which then reported "the
/// sync service did not answer in time" instead of the coded 503 the client
/// branches on.
/// </para>
/// </summary>
public sealed class UnreachableReplicaEndpointTests : IDisposable
{
    /// <summary>Well inside the desktop's 10-second attempt timeout and well
    /// outside the 2-second readiness budget, so the case fails on the hang
    /// rather than on a slow machine.</summary>
    private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(6);

    private static readonly string PlaceholderKey = Convert.ToBase64String(new byte[64]);

    private readonly TcpListener _silentAccount = new(IPAddress.Loopback, 0);
    private readonly List<TcpClient> _held = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly SyncServiceFactory _service;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public UnreachableReplicaEndpointTests()
    {
        _silentAccount.Start();
        _ = AcceptAndSayNothing();

        var port = ((IPEndPoint)_silentAccount.LocalEndpoint).Port;

        _service = new SyncServiceFactory
        {
            Configuration = (
                "ConnectionStrings:backlog",
                $"AccountEndpoint=https://127.0.0.1:{port}/;AccountKey={PlaceholderKey};"),
        };
    }

    public void Dispose()
    {
        _stop.Cancel();
        _silentAccount.Stop();

        lock (_held)
        {
            foreach (var connection in _held)
            {
                connection.Dispose();
            }
        }

        _service.Dispose();
        _stop.Dispose();
    }

    [Fact]
    public async Task Registering_against_an_account_that_does_not_answer_is_a_prompt_503()
    {
        var client = _service.CreateClient();
        var clock = Stopwatch.StartNew();

        var response = await client.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RegisterDevice),
            new RegisterDeviceRequest("Study desktop"),
            Cancellation).WaitAsync(Promptly * 2, Cancellation);

        clock.Stop();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            SyncErrorCodes.ReplicaUnavailable,
            (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
        Assert.True(clock.Elapsed < Promptly, $"The 503 took {clock.Elapsed.TotalSeconds:0.0} s.");
    }

    /// <summary>The account read that made the first caller wait is still in
    /// flight for the second; the second must not wait for it either.</summary>
    [Fact]
    public async Task A_second_caller_while_the_account_is_still_silent_is_turned_away_as_promptly()
    {
        var client = _service.CreateClient();

        await client.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RegisterDevice), new RegisterDeviceRequest("First"), Cancellation)
            .WaitAsync(Promptly * 2, Cancellation);

        var clock = Stopwatch.StartNew();
        var response = await client.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RegisterDevice), new RegisterDeviceRequest("Second"), Cancellation)
            .WaitAsync(Promptly * 2, Cancellation);

        clock.Stop();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(clock.Elapsed < Promptly, $"The 503 took {clock.Elapsed.TotalSeconds:0.0} s.");
    }

    /// <summary>Accepts every connection and holds it open without a byte —
    /// the proxy with nothing behind it.</summary>
    private async Task AcceptAndSayNothing()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var connection = await _silentAccount.AcceptTcpClientAsync(_stop.Token);

                lock (_held)
                {
                    _held.Add(connection);
                }
            }
        }
        catch (Exception stopped) when (stopped is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // The listener was stopped with the test.
        }
    }
}
