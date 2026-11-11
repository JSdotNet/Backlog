using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.Services;
using Backlog.Modules.Events.DomainModels;
using Backlog.Modules.Events.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// <c>AddEventsModule()</c>, the module's one registration point (guideline ADR 0005), resolved
/// through a real container. The repositories are the host's to register, so the module adds
/// only what it would otherwise lack — the clock — and what it publishes: the contract the
/// Inbox's To Event route reaches Events through, and the events read contract.
/// </summary>
public sealed class EventsModuleRegistrationTests
{
    [Fact]
    public void The_module_resolves_through_a_real_container()
    {
        using var provider = new ServiceCollection().AddEventsModule().BuildServiceProvider();

        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void Adding_the_module_twice_registers_nothing_twice()
    {
        var services = new ServiceCollection().AddEventsModule();
        var once = services.Count;

        services.AddEventsModule();

        Assert.Equal(once, services.Count);
    }

    [Fact]
    public void A_clock_the_host_registered_first_is_kept()
    {
        var pinned = new PinnedClock();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(pinned);

        using var provider = services.AddEventsModule().BuildServiceProvider();

        Assert.Same(pinned, provider.GetRequiredService<TimeProvider>());
    }

    /// <summary>The published contracts — the one the Inbox's To Event route reaches Events
    /// through, and the pane's — resolve once the host has registered the repositories and the
    /// pane's two outbound adapters.</summary>
    [Fact]
    public void The_published_contract_resolves_over_the_hosts_repositories()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IKnowledgeEventRepository>(new NoEvents());
        services.AddSingleton<IEventSeriesRepository>(new NoSeries());
        services.AddScoped<Backlog.Modules.Events.Abstractions.IEventTaskTarget, FakeEventTaskTarget>();
        services.AddSingleton<Backlog.Modules.Events.Abstractions.IEventBrainNotes, FakeEventBrainNotes>();

        using var provider = services.AddEventsModule().BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(provider.GetRequiredService<IKnowledgeEvents>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IEventsPane>());
    }

    /// <summary>
    /// The events read contract (local ADR 0050 §7) is the module's to publish: it resolves
    /// over the repository the host registered.
    /// </summary>
    [Fact]
    public async Task The_events_read_contract_resolves_over_the_hosts_repository()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IKnowledgeEventRepository>(new NoEvents());

        using var provider = services.AddEventsModule().BuildServiceProvider();

        var reader = provider.GetRequiredService<IKnowledgeEventReader>();
        Assert.Empty(await reader.ListDecidedAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Adding_the_module_twice_registers_one_events_read_contract()
    {
        var services = new ServiceCollection().AddEventsModule().AddEventsModule();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IKnowledgeEventReader));
    }

    [Fact]
    public void A_null_service_collection_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => EventsModuleRegistration.AddEventsModule(null!));
    }

    private sealed class PinnedClock : TimeProvider;

    private sealed class NoEvents : IKnowledgeEventRepository
    {
        public Task<KnowledgeEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<KnowledgeEvent?>(null);

        public Task<IReadOnlyList<KnowledgeEvent>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeEvent>>([]);

        public Task SaveAsync(KnowledgeEvent knowledgeEvent, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoSeries : IEventSeriesRepository
    {
        public Task<EventSeries?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<EventSeries?>(null);

        public Task<IReadOnlyList<EventSeries>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EventSeries>>([]);

        public Task SaveAsync(EventSeries series, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
