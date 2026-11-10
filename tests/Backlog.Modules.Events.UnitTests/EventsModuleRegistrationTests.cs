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

    [Fact]
    public void A_null_service_collection_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => EventsModuleRegistration.AddEventsModule(null!));
    }

    private sealed class PinnedClock : TimeProvider;
}
