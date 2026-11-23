using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.Extensions;
using Backlog.Modules.Capture.Services;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Capture.UnitTests;

/// <summary>
/// What the module offers on its own, before a host says whether the Events
/// area is on: every monitorable kind but the event page, which stays
/// withdrawn the way the <c>events</c> flag starts off.
/// </summary>
public sealed class CaptureSourceAvailabilityTests
{
    [Theory]
    [InlineData(CaptureSourceKind.YouTube)]
    [InlineData(CaptureSourceKind.Website)]
    [InlineData(CaptureSourceKind.Email)]
    [InlineData(CaptureSourceKind.Import)]
    public void The_module_default_offers_every_kind_but_the_event_page(CaptureSourceKind kind)
    {
        var availability = new DefaultCaptureSourceAvailability();

        Assert.True(availability.IsOffered(kind));
        Assert.False(availability.IsOffered(CaptureSourceKind.EventPage));
    }

    [Fact]
    public void The_module_registers_its_default_as_a_singleton()
    {
        var services = new ServiceCollection();
        services.AddCaptureModule();

        var registered = Assert.Single(services, service => service.ServiceType == typeof(ICaptureSourceAvailability));
        Assert.Equal(typeof(DefaultCaptureSourceAvailability), registered.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, registered.Lifetime);
    }

    /// <summary>TryAdd, so a host that answered the question first keeps its
    /// own answer.</summary>
    [Fact]
    public void A_hosts_own_answer_registered_first_is_kept()
    {
        var hosts = new HostAvailability();
        var services = new ServiceCollection();
        services.AddSingleton<ICaptureSourceAvailability>(hosts);
        services.AddCaptureModule();

        var registered = Assert.Single(services, service => service.ServiceType == typeof(ICaptureSourceAvailability));
        Assert.Same(hosts, registered.ImplementationInstance);
    }

    private sealed class HostAvailability : ICaptureSourceAvailability
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public bool IsOffered(CaptureSourceKind kind) => true;
    }
}
