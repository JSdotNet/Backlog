using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.FileSystem.DevPc;

/// <summary>
/// Registers the adapter that answers Dev PC Management's machine registry from the
/// kernel's device identity and Sessions' records (local ADR 0073 §7).
/// </summary>
public static class DevPcCrossContextAdapterRegistration
{
    /// <summary>
    /// A host must register <see cref="IDeviceIdentitySource"/> first. The session
    /// source is optional: a host that composes none still knows this machine.
    /// </summary>
    public static IServiceCollection AddDevPcCrossContextAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IMachineRegistry>(sp => new SessionMachineRegistry(
            sp.GetRequiredService<IDeviceIdentitySource>(),
            sp.GetService<IAgentSessionSource>()));

        return services;
    }
}
