using Backlog.Infrastructure.Sync;
using Backlog.Infrastructure.Sync.Extensions;
using Backlog.Infrastructure.Sync.Sessions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI.Adapters;
using Backlog.Modules.Sessions.UI.Extensions;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The merge that puts this machine's activity and the other environments' in one
/// log, and the composition that makes the Dashboard get it without asking.
/// <para>
/// The failure this file is about is a figure that reads as measured and is not:
/// a machine whose intervals arrived and were left out of the log is a machine
/// the Dashboard shows as idle all week, and a threshold taken from the wrong
/// source is a sentence on screen naming a number nobody used.
/// </para>
/// </summary>
public sealed class CompositeAgentActivitySourceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task Every_sources_sessions_and_subagents_are_in_the_log()
    {
        var composite = new CompositeAgentActivitySource(
        [
            new StubSource(Log([Activity("local")], FiveMinutes) with { Subagents = [Spawned("a1", "local")] }),
            new StubSource(Log([Activity("remote")], TimeSpan.Zero))
        ]);

        var log = await composite.GetActivityAsync(Noon.AddDays(-7), TestContext.Current.CancellationToken);

        Assert.Equal(["local", "remote"], log.Sessions.Select(session => session.Id));
        Assert.Equal(["a1"], log.Subagents.Select(agent => agent.Id));
    }

    /// <summary>
    /// <c>Unreadable</c> is the union, so a folder one source could not read is
    /// still named after the merge, and named once — two sources naming the same
    /// agent is one agent that could not be read.
    /// </summary>
    [Fact]
    public async Task Unreadable_is_the_union_and_names_each_source_once()
    {
        var composite = new CompositeAgentActivitySource(
        [
            new StubSource(Log([], FiveMinutes, "Claude", "Copilot")),
            new StubSource(Log([], TimeSpan.Zero, "Copilot"))
        ]);

        var log = await composite.GetActivityAsync(Noon.AddDays(-7), TestContext.Current.CancellationToken);

        Assert.Equal(["Claude", "Copilot"], log.Unreadable);
    }

    /// <summary>
    /// The threshold on the merged log is the one source that has an opinion, and a
    /// source answering zero is a source with none. The replicated source cannot
    /// know what threshold the pushing machine folded with and says so with zero;
    /// the local one folded with a real number and the sentence on screen names
    /// it. A merge that took the first, the minimum or the last would name zero
    /// minutes, or name the right number only when the lines happened to be in the
    /// right order.
    /// </summary>
    [Fact]
    public async Task IdleAfter_is_the_first_source_with_an_opinion_whatever_the_order()
    {
        var replicatedFirst = new CompositeAgentActivitySource(
        [
            new StubSource(Log([], TimeSpan.Zero)),
            new StubSource(Log([], FiveMinutes))
        ]);

        var localFirst = new CompositeAgentActivitySource(
        [
            new StubSource(Log([], FiveMinutes)),
            new StubSource(Log([], TimeSpan.Zero))
        ]);

        Assert.Equal(FiveMinutes, (await replicatedFirst.GetActivityAsync(Noon, TestContext.Current.CancellationToken)).IdleAfter);
        Assert.Equal(FiveMinutes, (await localFirst.GetActivityAsync(Noon, TestContext.Current.CancellationToken)).IdleAfter);
    }

    /// <summary>
    /// <c>Since</c> is the horizon the caller asked for, whatever each source
    /// echoed back. A surface reporting a floor reads it off the log, and a merged
    /// log that carried one source's idea of the horizon would be reporting a floor
    /// only that source was measured against.
    /// </summary>
    [Fact]
    public async Task Since_is_the_horizon_that_was_asked_for()
    {
        var asked = Noon.AddDays(-28);

        var composite = new CompositeAgentActivitySource(
        [
            new StubSource(new AgentActivityLog([], [], Noon.AddDays(-84), FiveMinutes))
        ]);

        var log = await composite.GetActivityAsync(asked, TestContext.Current.CancellationToken);

        Assert.Equal(asked, log.Since);
    }

    /// <summary>A host that composed one source gets a composite of one, which
    /// behaves exactly as that source did. That is what lets the merged registration
    /// be unconditional rather than something a host has to opt into.</summary>
    [Fact]
    public async Task A_single_source_is_answered_unchanged()
    {
        var only = Log([Activity("local")], FiveMinutes, "Copilot") with { Subagents = [Spawned("a1", "local")] };

        var log = await new CompositeAgentActivitySource([new StubSource(only)])
            .GetActivityAsync(Noon.AddDays(-7), TestContext.Current.CancellationToken);

        Assert.Equal(only.Sessions, log.Sessions);
        Assert.Equal(only.Subagents, log.Subagents);
        Assert.Equal(only.Unreadable, log.Unreadable);
        Assert.Equal(only.IdleAfter, log.IdleAfter);
    }

    // --- Composition -----------------------------------------------------------

    /// <summary>
    /// The local reader alone. A host that never composed session replication asks
    /// for the port and gets a merged source of one, so nothing about the Dashboard
    /// changed for it.
    /// </summary>
    [Fact]
    public void A_host_with_only_the_reader_still_resolves_the_port()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceIdentitySource>(new StubDeviceIdentity());
        services.AddAgentActivitySource();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<CompositeAgentActivitySource>(provider.GetRequiredService<IAgentActivitySource>());
        Assert.Single(provider.GetKeyedServices<IAgentActivitySource>(KeyedService.AnyKey));
    }

    /// <summary>
    /// Adding session replication contributes a second keyed activity source and
    /// nothing else changes: the unkeyed port the Dashboard's adapter injects is
    /// the composite, so the other machines' figures arrive without a line of its
    /// own. The registration calls are made the wrong way round on purpose, for
    /// the reason <c>CompositeAgentSessionSourceTests</c> gives.
    /// </summary>
    [Fact]
    public void Adding_session_replication_contributes_a_second_source_in_either_order()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceIdentitySource>(new StubDeviceIdentity());
        services.AddSingleton<IAppFeatureSettings>(new StubFeatures());
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSyncClient(new Uri("https://sync.example"));
        services.AddSessionSyncClient(new Uri("https://sync.example"));
        services.AddSessionSyncStores(Path.Combine(
            Path.GetTempPath(), $"backlog-session-sync-{Guid.NewGuid():N}"));
        services.AddAgentSessionSource();
        services.AddAgentActivitySource();

        using var provider = services.BuildServiceProvider();

        var contributors = provider.GetKeyedServices<IAgentActivitySource>(KeyedService.AnyKey).ToList();

        Assert.Equal(2, contributors.Count);
        Assert.Contains(contributors, source => source is ReplicatedAgentActivitySource);
        Assert.IsType<CompositeAgentActivitySource>(provider.GetRequiredService<IAgentActivitySource>());
    }

    private static AgentActivityLog Log(
        AgentSessionActivity[] sessions,
        TimeSpan idleAfter,
        params string[] unreadable) =>
        new(sessions, unreadable, Noon.AddDays(-7), idleAfter);

    private static AgentSessionActivity Activity(string id) =>
        new(
            id,
            AgentSessionKind.Claude,
            "11111111-1111-1111-1111-111111111111",
            "Workshop PC",
            [new AgentActivityRun(Noon.AddMinutes(-10), Noon)],
            []);

    private static SubagentActivity Spawned(string id, string sessionId) =>
        new(
            id,
            sessionId,
            AgentSessionKind.Claude,
            "11111111-1111-1111-1111-111111111111",
            "Workshop PC",
            [new AgentActivityRun(Noon.AddMinutes(-10), Noon)]);

    private sealed class StubSource(AgentActivityLog log) : IAgentActivitySource
    {
        public Task<AgentActivityLog> GetActivityAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
            Task.FromResult(log);
    }

    private sealed class StubDeviceIdentity : IDeviceIdentitySource
    {
        public DeviceIdentity Current { get; } = new(Guid.Empty, "Workshop PC");
    }

    private sealed class StubFeatures : IAppFeatureSettings
    {
        public event Action? Changed;

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "in memory";

        public bool IsEnabled(string key) => true;

        public string? SetEnabled(string key, bool enabled)
        {
            Changed?.Invoke();
            return null;
        }
    }
}
