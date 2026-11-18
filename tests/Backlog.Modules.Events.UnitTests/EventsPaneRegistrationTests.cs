using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Extensions;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// <c>AddEventsModule()</c> publishes the pane's port Scoped, because it holds the task target,
/// which is Scoped over Tasks' own scoped port. The repositories and the two targets are the
/// host's to register.
/// </summary>
public sealed class EventsPaneRegistrationTests
{
    [Fact]
    public void The_pane_resolves_per_scope_through_a_validating_container()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IKnowledgeEventRepository, InMemoryKnowledgeEvents>();
        services.AddSingleton<IEventSeriesRepository, InMemoryEventSeries>();
        services.AddScoped<IEventTaskTarget, FakeEventTaskTarget>();
        services.AddSingleton<IEventBrainNotes, FakeEventBrainNotes>();
        services.AddEventsModule();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var pane = first.ServiceProvider.GetRequiredService<IEventsPane>();

        Assert.Same(pane, first.ServiceProvider.GetRequiredService<IEventsPane>());
        Assert.NotSame(pane, second.ServiceProvider.GetRequiredService<IEventsPane>());
    }
}
