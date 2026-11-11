using Backlog.Modules.Events.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// <c>AddEventsModule()</c>, the module's one registration point (guideline ADR 0005), resolved
/// through a real container. The repositories are the host's to register, so the module adds
/// only what it would otherwise lack: the clock.
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

        Assert.NotNull(provider.GetRequiredService<Backlog.Modules.Events.Abstractions.IKnowledgeEvents>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<Backlog.Modules.Events.Abstractions.IEventsPane>());
    }

    private sealed class NoEvents : IKnowledgeEventRepository
    {
        public Task<DomainModels.KnowledgeEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<DomainModels.KnowledgeEvent?>(null);

        public Task<IReadOnlyList<DomainModels.KnowledgeEvent>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DomainModels.KnowledgeEvent>>([]);

        public Task SaveAsync(DomainModels.KnowledgeEvent knowledgeEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoSeries : IEventSeriesRepository
    {
        public Task<DomainModels.EventSeries?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<DomainModels.EventSeries?>(null);

        public Task<IReadOnlyList<DomainModels.EventSeries>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DomainModels.EventSeries>>([]);

        public Task SaveAsync(DomainModels.EventSeries series, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public void A_null_service_collection_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => EventsModuleRegistration.AddEventsModule(null!));
    }

    private sealed class PinnedClock : TimeProvider;
}
