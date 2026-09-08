using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI.Adapters;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Sessions.UI.Extensions;

/// <summary>
/// Wires the adapters that answer <see cref="IAgentSessionSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// An extension method rather than a public adapter, for the reason the Dashboard's
/// registration gives: composition is the host's decision, and keeping the types
/// internal means the pane cannot reach past its own port to the readers behind it.
/// A test replaces this call rather than having to unpick it.
/// </para>
/// <para>
/// Both hosts call it, and both get the same adapters — unlike
/// <c>IDevToolService</c>, where the desktop shells out to the CLIs and the
/// harness reads the JSON only. There is nothing to differ about here: the sessions
/// are files in the profile of whoever is signed in, and the harness runs as the same
/// person on the same machine, so a "local development" variant would be the same
/// code reading the same folders.
/// </para>
/// <para>
/// Singletons, because neither adapter holds state — no cache, no handle, nothing
/// per-surface. Every call re-reads, which is what makes the pane's refresh mean
/// anything.
/// </para>
/// <para>
/// A host must register <see cref="IDeviceIdentitySource"/> before calling this. The
/// local adapter stamps every session it reads with this device's id, and the identity is
/// the kernel's answer rather than this context's, because a machine name is not an
/// identity and three contexts need the same answer to that.
/// </para>
/// </remarks>
public static class SessionRegistration
{
    /// <summary>
    /// The key the local reader is registered under.
    /// <para>
    /// Its value never has to agree with anything outside this file — the
    /// composite collects <c>KeyedService.AnyKey</c> — so a second contributor in
    /// another project picks its own. What the key does is keep this registration
    /// out of the unkeyed slot the composite occupies, which is what stops the two
    /// from overwriting one another and what stops the composite enumerating
    /// itself.
    /// </para>
    /// </summary>
    private const string LocalSourceKey = "sessions.local";

    /// <summary>
    /// This machine's own session readers, and the merged source every consumer
    /// asks for.
    /// <para>
    /// <strong>The unkeyed registration is a composite from the first day rather
    /// than from the day a second source appeared</strong>, and that is the part
    /// worth arguing. A host that composes only this call gets a composite of one,
    /// which behaves exactly as the local reader did; a host that also composes
    /// session replication gets a composite of two, without either call knowing
    /// about the other and without the order of the two lines mattering — every
    /// part is resolved lazily. The alternative, decorating whatever
    /// <see cref="IAgentSessionSource"/> was registered first, would have made the
    /// merged list depend on which line a host happened to write first, and the
    /// failure mode of getting it wrong is a pane that quietly shows half the
    /// sessions.
    /// </para>
    /// <para>
    /// Both consumers get the merge for free: <c>SessionsPane</c> injects the port,
    /// and the Dashboard reaches the same registration through
    /// <c>AgentSessionAssistantSessionSource</c>. Neither learns that there is more
    /// than one place a session can come from.
    /// </para>
    /// </summary>
    public static IServiceCollection AddAgentSessionSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddKeyedSingleton<IAgentSessionSource>(
            LocalSourceKey,
            (sp, _) => new LocalAgentSessionSource(sp.GetRequiredService<IDeviceIdentitySource>()));

        services.AddSingleton<IAgentSessionSource>(sp =>
            new CompositeAgentSessionSource([.. sp.GetKeyedServices<IAgentSessionSource>(KeyedService.AnyKey)]));

        return services;
    }
}
