using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI.Adapters;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Sessions.UI.Extensions;

/// <summary>
/// Wires the adapters that answer <see cref="IAgentSessionSource"/> and, beside it,
/// <see cref="IDeliveryRunSource"/>.
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

    /// <summary>The key the local activity reader is registered under, on the
    /// same terms as <see cref="LocalSourceKey"/> and for the second port.</summary>
    private const string LocalActivitySourceKey = "activity.local";

    /// <summary>The keys this machine's session records contribute under — to the
    /// session composite and to the activity composite — so a session whose files are
    /// gone is still answered. Optional on the store: a host that composed none
    /// contributes nothing.</summary>
    private const string RecordedSourceKey = "sessions.recorded";

    /// <inheritdoc cref="RecordedSourceKey"/>
    private const string RecordedActivitySourceKey = "activity.recorded";

    /// <summary>
    /// This machine's own session readers, the merged source every consumer asks
    /// for, and the reader of the delivery runs the pane shows against them.
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

        // GetService for the cache, as AddAgentActivitySource does and for the same
        // reason: a host that composed none still gets the right list, slower.
        // ISessionRepositoryResolver is the host's to supply, beside the transcript
        // facts cache: it reads the registered repositories, which live behind an
        // adapter this module may not reference (ModuleBoundaryTests). Optional for
        // the same reason the cache is — a head without a repository list places
        // no session anywhere, which is the true answer there.
        services.AddKeyedSingleton<IAgentSessionSource>(
            LocalSourceKey,
            (sp, _) => new LocalAgentSessionSource(
                sp.GetRequiredService<IDeviceIdentitySource>(),
                sp.GetService<ITranscriptFactsCache>(),
                sp.GetService<ISessionRepositoryResolver>()));

        services.AddKeyedSingleton<IAgentSessionSource>(
            RecordedSourceKey,
            (sp, _) => new RecordedAgentSessionSource(sp.GetService<IAgentSessionRecordStore>()));

        services.AddSingleton<IAgentSessionSource>(sp =>
            new CompositeAgentSessionSource([.. sp.GetKeyedServices<IAgentSessionSource>(KeyedService.AnyKey)]));

        // The delivery runs go in with the sessions rather than behind a call of their
        // own, and that is the opposite of the choice AddAgentActivitySource makes.
        // The activity port is a separate call because a host wanting the session
        // list is not thereby asking for hundreds of megabytes of transcript; the run
        // files are a few tens of megabytes read by the same pane that shows the
        // sessions, and there is no consumer of one port that does not want the
        // other. A host that composed the pane gets both, which is what the pane
        // injects. The port itself stays separate so the two readings can be tested
        // and replaced apart.
        services.AddSingleton<IDeliveryRunSource>(sp =>
            new LocalDeliveryRunSource(sp.GetRequiredService<IDeviceIdentitySource>()));

        // The writing half, beside the reading one. GetService and not
        // GetRequiredService for the activator: it is the shell's own type, composed
        // by a host that has a window, and a headless host recording runs is a host
        // whose open_dashboard honestly answers that there is nothing to bring
        // forward — not a startup failure over a window nobody asked for.
        services.AddSingleton<IDeliverySurfaceLifecycle>(sp =>
            new LocalDeliverySurfaceLifecycle(
                sp.GetRequiredService<IDeviceIdentitySource>(),
                sp.GetService<ISessionsSurfaceActivator>()));

        // And the measured half of the same runs: the hook events a session forwards,
        // attributed to the run it is driving. A singleton because it carries each
        // session's transcript cursors from one event to the next.
        services.AddSingleton<IDeliveryRunTelemetry>(_ => new DeliveryRunTelemetry());

        // The shell's Ask AI port, answered from the merged catalog above and
        // from nothing else — see SessionsAiContentSource for why the transcripts
        // stay out. Scoped for consistency with the other areas' sources, which
        // read per-circuit state; this one holds only the singleton port, so the
        // lifetime costs one object per window.
        services.AddScoped<IAiContentSource, SessionsAiContentSource>();

        return services;
    }

    /// <summary>
    /// Wires the adapter that answers <see cref="IAgentActivitySource"/>: when the
    /// agents on this machine were actually producing, read out of the bodies of the
    /// transcripts the session source only stats — and the merged source every
    /// consumer asks for.
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
    /// The same shape as <see cref="AddAgentSessionSource"/>, for the same argument:
    /// the local reader is keyed and the unkeyed registration is a composite from the
    /// first day. A host that composes only this call gets a composite of one, which
    /// behaves exactly as the reader did; a host that also composes session
    /// replication gets a composite of two, in either order, and the Dashboard's
    /// adapter — the one consumer — sees the other machines' runs and waits without
    /// learning that there is more than one place an interval can come from.
    /// </para>
    /// <para>
    /// Singletons for the reason the session sources are, with one addition: the
    /// local adapter holds no state of its own, and what state there is lives in the
    /// cache behind the port, which is keyed on files rather than on this object's
    /// lifetime.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddAgentActivitySource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddKeyedSingleton<IAgentActivitySource>(
            LocalActivitySourceKey,
            (sp, _) => new LocalAgentActivitySource(
                sp.GetRequiredService<IDeviceIdentitySource>(),
                sp.GetService<IAgentActivityCache>()));

        services.AddKeyedSingleton<IAgentActivitySource>(
            RecordedActivitySourceKey,
            (sp, _) => new RecordedAgentActivitySource(sp.GetService<IAgentSessionRecordStore>()));

        services.AddSingleton<IAgentActivitySource>(sp =>
            new CompositeAgentActivitySource([.. sp.GetKeyedServices<IAgentActivitySource>(KeyedService.AnyKey)]));

        // The keeper reads the local readers and nothing else, so a record is never
        // amended from a replicated reading or from itself. Registered here because it
        // needs both of this module's local readers, and it is a no-op on a host that
        // composed no record store.
        services.AddSingleton<ISessionRecordKeeper>(sp =>
            new SessionRecordKeeper(
                sp.GetRequiredKeyedService<IAgentSessionSource>(LocalSourceKey),
                sp.GetKeyedService<IAgentActivitySource>(LocalActivitySourceKey),
                sp.GetService<IAgentSessionRecordStore>(),
                sp.GetService<TimeProvider>() ?? TimeProvider.System,
                sp.GetService<ISessionRecordPublisher>()));

        return services;
    }
}
