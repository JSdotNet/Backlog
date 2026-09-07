using System.Diagnostics;
using Backlog.Infrastructure.Cosmos.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

/// <summary>
/// Which activity the Cosmos client is constructed under.
/// <para>
/// It reads like a detail and it is not one. The client is a singleton resolved
/// on the first call that needs Cosmos, and that call is inside a request — a
/// device's first push. The SDK starts a background endpoint refresh when it is
/// constructed, timers capture the <see cref="ExecutionContext"/> they are
/// started on, and an <see cref="Activity"/> travels in it: the refresh five
/// minutes later then reports itself as a child of that one push, and the trace
/// is five minutes long. QA saw exactly that — <c>sync.push_tasks</c> at
/// 300211 ms against a push that took milliseconds — and percentiles read off
/// that are not wrong by a little.
/// </para>
/// <para>
/// The fix is not to construct the client eagerly: it stays lazy so a service
/// whose emulator is still starting answers 503 rather than failing to start.
/// It is to construct it with no ambient activity, which is what this pins.
/// </para>
/// </summary>
public class CosmosClientActivityTests
{
    /// <summary>An account that is never reached. The v3 client constructor does
    /// no IO, so a replica can resolve its container against this and never send
    /// a byte.</summary>
    private const string UnreachableAccount = "https://localhost:8081/";

    private static readonly string PlaceholderKey = Convert.ToBase64String(new byte[64]);

    [Fact]
    public void The_client_is_constructed_outside_the_request_that_needed_it()
    {
        Activity? captured = null;
        var services = new ServiceCollection();
        services.AddSingleton(_ =>
        {
            captured = Activity.Current;
            return new CosmosClient(UnreachableAccount, PlaceholderKey);
        });

        using var provider = services.BuildServiceProvider();
        var replica = new CosmosTaskReplica(
            provider,
            Options.Create(new CosmosOptions()),
            NullLogger<CosmosTaskReplica>.Instance);

        // A plain Activity rather than one from an ActivitySource: a source with
        // no listener hands back null, and then this test would pass without
        // ever having had an ambient activity to leak.
        using var request = new Activity("sync.push_tasks").Start();

        replica.Container();

        Assert.NotNull(Activity.Current);
        Assert.Null(captured);
    }

    /// <summary>The suppression is a loan, not a transfer: the call that needed
    /// Cosmos carries on inside its own span, and the rest of the push has to
    /// stay on the trace.</summary>
    [Fact]
    public void The_request_keeps_its_own_activity()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_ => new CosmosClient(UnreachableAccount, PlaceholderKey));

        using var provider = services.BuildServiceProvider();
        var replica = new CosmosTaskReplica(
            provider,
            Options.Create(new CosmosOptions()),
            NullLogger<CosmosTaskReplica>.Instance);

        using var request = new Activity("sync.push_tasks").Start();

        replica.Container();

        Assert.Same(request, Activity.Current);
    }
}
