using Backlog.Modules.Work.Abstractions.Tasks.Services;
using Backlog.Modules.Work.Tasks;
using Backlog.Modules.Work.Plans;
using Backlog.Modules.Work.Abstractions.Plans.Services;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// What the roadmap plan asks of the world outside Work, answered by adapters that
/// may see both sides: a roadmap item rolls up the backlog entries and devbook
/// chapters it gathers (<see cref="IRoadmapItemRollup"/>), the reader's typed pace is
/// read from the settings file (<see cref="IPlanningVelocitySettings"/>), and the
/// actual hours a day head shows come from the agents' activity
/// (<see cref="IRoadmapActualHours"/>). Where the task and the plan meet inside Work
/// is the module's own, composed by <c>AddWorkJoins</c> (local ADR 0073 §1).
/// <para>
/// Registered here — in one place both hosts and the scope-validation guard call —
/// so the lifetimes cannot drift between the desktop app and the web harness. The
/// rollup captures services the module registers as <c>Scoped</c>
/// (<see cref="IRoadmapPlanning"/> and <see cref="ITaskItems"/>), so it is
/// <c>Scoped</c> too: a singleton over a scoped dependency is a captive dependency
/// that a validating root provider refuses to build.
/// </para>
/// </summary>
public static class RoadmapCrossContextAdapterRegistration
{
    /// <summary>
    /// Registers the roadmap cross-context adapters. Call after
    /// <c>AddTasksModule</c> and <c>AddRoadmapModule</c> (which supply the scoped
    /// ports these adapters capture) and after <see cref="WorkspaceSettingsStore"/>
    /// (which the rollup reads the storage root from).
    /// </summary>
    public static IServiceCollection AddRoadmapCrossContextAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The rollup also captures the storage root, read per call rather than
        // pinned, so it stays a factory — but a scoped one, resolving its scoped
        // ITaskItems from the same scope the request runs in, per call: the backlog's
        // plan import reaches the roadmap importer, which gathers through this, so
        // taking the backlog in the constructor deadlocks the scope, as for the
        // finished work AddWorkJoins registers. The session source is optional: it dates work its entry
        // left undated, and a host without one draws the entries' own dates, as before.
        //
        // The Brain's notes are read through Roadmap's own port (local ADR 0053): none
        // in a host without a Brain, the Brain's database where its adapters replace
        // this empty answer. A singleton either way — it holds no scoped service.
        services.TryAddSingleton<IRoadmapBrainNotes, NoRoadmapBrainNotes>();
        services.AddScoped<IRoadmapItemRollup>(sp =>
            new RoadmapItemRollupService(
                () => sp.GetRequiredService<ITaskItems>(),
                () => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory,
                sp.GetService<IAgentSessionSource>(),
                sp.GetService<TimeProvider>(),
                sp.GetService<IRoadmapBrainNotes>()));

        // A finished plan's project note and its move to the archive (local ADR 0052 §5),
        // scoped as the rollup it reads is. The Brain's areas are its adapter's answer:
        // a host that composed no Brain has none, so nothing is ever finished there.
        services.AddScoped<IRoadmapProjectNoteArchive>(sp =>
            new RoadmapProjectNoteArchive(
                sp.GetRequiredService<IRoadmapPlanning>(),
                sp.GetRequiredService<IRoadmapItemRollup>(),
                sp.GetService<IRoadmapProjectNotes>(),
                sp.GetService<ShellNavigationStore>()));

        // Singleton, unlike the adapters above, and deliberately: this one captures no
        // scoped service — only the settings store, which is a singleton in both
        // hosts — so there is no captive dependency to avoid, and the reader's typed
        // pace is one per-device figure rather than something a request owns. It
        // reads through to the store on every call, so a pace changed on the roadmap
        // is live without a restart.
        services.AddSingleton<IPlanningVelocitySettings, PlanningVelocitySource>();

        // The same file is the pace document that travels between devices (local ADR
        // 0018): Roadmap's replication port reads and writes it through its store
        // port. A singleton over the singleton store, for the reason the line above is.
        services.AddSingleton<IRoadmapReplicaStore>(sp => sp.GetRequiredService<PlanningVelocitySettingsStore>());

        // The actual hours a begun day head shows, read from the merged agent activity
        // the dashboard reads (ADR 0019). A singleton over singletons: the activity
        // source, the clock and the feature switches. Each is optional, so a host that
        // composed no Sessions activity still resolves the port and it answers that it
        // cannot state the hours. Resolved in the factory rather than at this call, so
        // AddAgentActivitySource may come after it.
        services.AddSingleton(sp =>
            new RoadmapActualHours(
                sp.GetService<IAgentActivitySource>(),
                sp.GetService<TimeProvider>(),
                sp.GetService<IAppFeatureSettings>(),
                sp.GetService<IAgentSessionSource>()));
        services.AddSingleton<IRoadmapActualHours>(sp => sp.GetRequiredService<RoadmapActualHours>());

        // The stretches behind those hours, which a head's hours open so the person can
        // check the figure. The same adapter, so the report and the head never disagree.
        services.AddSingleton<IRoadmapHoursReport>(sp => sp.GetRequiredService<RoadmapActualHours>());

        // The roadmap's per-device view choices — the Hours switch (ADR 0019, §4) — kept
        // in the shell's own per-device file beside the surface it reopens on. A singleton
        // over a singleton; a host that composed no such file holds the choice in memory.
        services.AddSingleton<IRoadmapViewPreferences>(sp =>
            new RoadmapViewPreferences(sp.GetService<ShellNavigationStore>()));

        return services;
    }
}
