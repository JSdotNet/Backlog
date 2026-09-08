using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI.Adapters;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Sessions.UI.Extensions;

/// <summary>
/// Wires the adapter that answers <see cref="IAgentSessionSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// An extension method rather than a public adapter, for the reason the Dashboard's
/// registration gives: composition is the host's decision, and keeping the type
/// internal means the pane cannot reach past its own port to the reader behind it.
/// A test replaces this call rather than having to unpick it.
/// </para>
/// <para>
/// Both hosts call it, and both get the same adapter — unlike
/// <c>IDevToolService</c>, where the desktop shells out to the CLIs and the
/// harness reads the JSON only. There is nothing to differ about here: the sessions
/// are files in the profile of whoever is signed in, and the harness runs as the same
/// person on the same machine, so a "local development" variant would be the same
/// code reading the same folders.
/// </para>
/// <para>
/// A singleton, because the adapter holds no state — no cache, no handle, nothing
/// per-surface. Every call re-reads the folders, which is what makes the pane's
/// refresh mean anything.
/// </para>
/// <para>
/// A host must register <see cref="IDeviceIdentitySource"/> before calling this. The
/// adapter stamps every session it reads with this device's id, and the identity is
/// the kernel's answer rather than this context's, because a machine name is not an
/// identity and three contexts need the same answer to that.
/// </para>
/// </remarks>
public static class SessionRegistration
{
    public static IServiceCollection AddAgentSessionSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAgentSessionSource>(sp =>
            new LocalAgentSessionSource(sp.GetRequiredService<IDeviceIdentitySource>()));

        return services;
    }

    /// <summary>
    /// Wires the adapter that answers <see cref="IAgentActivitySource"/>: when the
    /// agents on this machine were actually producing, read out of the bodies of the
    /// transcripts the session source only stats.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A separate call rather than part of <see cref="AddAgentSessionSource"/>, because
    /// the two ports are separate for a reason a shared registration would quietly
    /// undo: a host that wants the session list is not thereby asking for hundreds of
    /// megabytes of transcript to be parsed. One call per port keeps that a decision
    /// somebody made.
    /// </para>
    /// <para>
    /// <see cref="ServiceProviderServiceExtensions.GetService{T}"/> for the cache and
    /// not <c>GetRequiredService</c> — the cache is an optimisation, and a host that has
    /// not registered one still gets correct figures, slowly. A required dependency here
    /// would turn "this host did not compose a cache" into a startup failure over
    /// something that only ever costs time.
    /// </para>
    /// <para>
    /// A singleton for the reason the session source is one, with one addition: the
    /// adapter holds no state of its own, and what state there is lives in the cache
    /// behind the port, which is keyed on files rather than on this object's lifetime.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddAgentActivitySource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAgentActivitySource>(sp => new LocalAgentActivitySource(
            sp.GetRequiredService<IDeviceIdentitySource>(),
            sp.GetService<IAgentActivityCache>()));

        return services;
    }
}
