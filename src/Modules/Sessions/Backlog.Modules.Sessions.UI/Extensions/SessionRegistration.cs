using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI.Adapters;
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
/// <c>ICopilotToolService</c>, where the desktop shells out to the CLIs and the
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
/// </remarks>
public static class SessionRegistration
{
    public static IServiceCollection AddAgentSessionSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAgentSessionSource>(_ => new LocalAgentSessionSource());

        return services;
    }
}
