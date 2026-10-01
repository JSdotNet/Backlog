using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Copilot.UnitTests;

/// <summary>
/// What a host gets for calling <c>AddCopilot()</c>: the one launcher every
/// Copilot CLI offer starts through, a singleton, and the host's choice of which —
/// the process launcher on a machine that may start one, the unavailable one on a
/// host that may not.
/// </summary>
public sealed class CopilotRegistrationTests
{
    [Fact]
    public void The_launcher_is_a_singleton_the_host_chooses()
    {
        var launcher = new UnavailableCopilotCliLauncher();
        var services = new ServiceCollection();
        services.AddCopilot(_ => launcher);

        var descriptor = Assert.Single(services);
        Assert.Equal(typeof(ICopilotCliLauncher), descriptor.ServiceType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Same(launcher, descriptor.ImplementationFactory!(null!));
    }
}
