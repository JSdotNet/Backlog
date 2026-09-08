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
/// The merge that puts this machine's sessions and the other environments' in one
/// list, and the composition that makes both consumers get it without asking.
/// <para>
/// The failure this file is about is a list that reads as complete and is not.
/// Every source here answers with a catalog that says how much it left out, and the
/// merge is the one place those numbers can be quietly rounded down — after which
/// the pane has nothing left to say "and 640 more" with.
/// </para>
/// </summary>
public sealed class CompositeAgentSessionSourceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Every_sources_sessions_are_in_the_list()
    {
        var composite = new CompositeAgentSessionSource(
        [
            new StubSource(new AgentSessionCatalog([Session("local")], [], 1)),
            new StubSource(new AgentSessionCatalog([Session("remote")], [], 1))
        ]);

        var catalog = await composite.GetSessionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["local", "remote"], catalog.Sessions.Select(session => session.Id));
    }

    /// <summary>
    /// <c>Discovered</c> is the sum, so a capped source keeps the merged list
    /// visibly capped. Taking the maximum, or counting only what was returned, would
    /// each turn a truncated list into what reads as the whole history — which is
    /// the one thing <c>AgentSessionCatalog.Discovered</c> exists to prevent, and
    /// what the pane's "say how much was left out" rule stands on.
    /// </summary>
    [Fact]
    public async Task Discovered_is_the_sum_so_a_capped_list_stays_capped()
    {
        var composite = new CompositeAgentSessionSource(
        [
            new StubSource(new AgentSessionCatalog([Session("local")], [], 842)),
            new StubSource(new AgentSessionCatalog([Session("remote")], [], 3))
        ]);

        var catalog = await composite.GetSessionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(845, catalog.Discovered);
        Assert.True(catalog.Capped);
    }

    /// <summary>
    /// <c>Unreadable</c> is the union, so a folder one source could not read is
    /// still named after the merge. Dropping it because the other source answered
    /// would present half a picture as the whole one.
    /// </summary>
    [Fact]
    public async Task Unreadable_is_the_union_and_names_each_source_once()
    {
        var composite = new CompositeAgentSessionSource(
        [
            new StubSource(new AgentSessionCatalog([], ["Claude", "Copilot"], 0)),
            new StubSource(new AgentSessionCatalog([], ["Copilot"], 0))
        ]);

        var catalog = await composite.GetSessionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Claude", "Copilot"], catalog.Unreadable);
    }

    /// <summary>A host that composed one source gets a composite of one, which
    /// behaves exactly as that source did. That is what lets the merged registration
    /// be unconditional rather than something a host has to opt into.</summary>
    [Fact]
    public async Task A_single_source_is_answered_unchanged()
    {
        var only = new AgentSessionCatalog([Session("local")], ["Copilot"], 12);

        var catalog = await new CompositeAgentSessionSource([new StubSource(only)])
            .GetSessionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(only.Sessions.Select(session => session.Id), catalog.Sessions.Select(session => session.Id));
        Assert.Equal(only.Unreadable, catalog.Unreadable);
        Assert.Equal(only.Discovered, catalog.Discovered);
    }

    // --- Composition -----------------------------------------------------------

    /// <summary>
    /// The local readers alone. A host that never composed session replication asks
    /// for the port and gets a merged source of one, so nothing about the pane
    /// changed for it.
    /// </summary>
    [Fact]
    public void A_host_with_only_the_readers_still_resolves_the_port()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceIdentitySource>(new StubDeviceIdentity());
        services.AddAgentSessionSource();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IAgentSessionSource>());
        Assert.Single(provider.GetKeyedServices<IAgentSessionSource>(KeyedService.AnyKey));
    }

    /// <summary>
    /// <strong>Both consumers get the merge automatically.</strong> Adding session
    /// replication contributes a second keyed source and nothing else changes: the
    /// unkeyed port every screen injects is the composite, so <c>SessionsPane</c>
    /// and the Dashboard's <c>AgentSessionAssistantSessionSource</c> both see the
    /// other machines' sessions without a line of their own.
    /// <para>
    /// The two registration calls are made in the order a host makes them, and the
    /// order is deliberately the wrong way round here: session replication is added
    /// before the readers it merges with, because a composition that only worked one
    /// way round would be a line-ordering trap nothing else would catch.
    /// </para>
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

        using var provider = services.BuildServiceProvider();

        var contributors = provider.GetKeyedServices<IAgentSessionSource>(KeyedService.AnyKey).ToList();

        Assert.Equal(2, contributors.Count);
        Assert.Contains(contributors, source => source is ReplicatedAgentSessionSource);
        Assert.IsType<CompositeAgentSessionSource>(provider.GetRequiredService<IAgentSessionSource>());
    }

    private static AgentSession Session(string id) =>
        new(
            id,
            AgentSessionKind.Claude,
            "11111111-1111-1111-1111-111111111111",
            "Workshop PC",
            "A session",
            string.Empty,
            null,
            null,
            null,
            Noon,
            AgentSessionState.Running,
            null,
            AgentSessionOrigin.Local);

    private sealed class StubSource(AgentSessionCatalog catalog) : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(catalog);
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
