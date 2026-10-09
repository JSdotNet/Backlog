using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks;
using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// What the roadmap plan asks of the world outside it, answered by adapters that
/// may see both sides: the backlog's tag picker offers the plan's tags
/// (<see cref="IRoadmapTagSource"/>), Import lays a document's <c>plan</c> entries
/// out on the plan (<see cref="IRoadmapPlanIntake"/>), a roadmap item rolls up the backlog entries
/// and knowledge chapters it gathers (<see cref="IRoadmapItemRollup"/>), the
/// reader's typed pace is read from the settings file
/// (<see cref="IPlanningVelocitySettings"/>), the finished work a measured pace
/// counts comes from the backlog (<see cref="IRoadmapCompletedWork"/>), and the
/// actual hours a day head shows come from the agents' activity
/// (<see cref="IRoadmapActualHours"/>).
/// <para>
/// Registered here — in one place both hosts and the scope-validation guard call —
/// so the lifetimes cannot drift between the desktop app and the web harness. The
/// two cross-context adapters capture services the modules register as
/// <c>Scoped</c> (<see cref="IRoadmapPlanning"/> and <see cref="ITaskItems"/>), so
/// both must be <c>Scoped</c> too: a singleton over a scoped dependency is a
/// captive dependency that a validating root provider refuses to build.
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

        // Constructor injection where it fits: the container hands the scoped
        // IRoadmapPlanning to the constructor, so no factory reaches into the
        // provider for it.
        services.AddScoped<IRoadmapTagSource, RoadmapPlanTagSource>();

        // The same join the other way: Import hands a document's `plan` entries to
        // Roadmap's own command through the scoped IRoadmapPlanning (ADR 0013).
        services.AddScoped<IRoadmapPlanIntake, RoadmapPlanIntake>();

        // The rollup also captures the storage root, read per call rather than
        // pinned, so it stays a factory — but a scoped one, resolving its scoped
        // ITaskItems from the same scope the request runs in, per call: the backlog's
        // plan import reaches the roadmap importer, which gathers through this, so
        // taking the backlog in the constructor deadlocks the scope, as for the
        // finished work below. The session source is optional: it dates work its entry
        // left undated, and a host without one draws the entries' own dates, as before.
        services.AddScoped<IRoadmapItemRollup>(sp =>
            new RoadmapItemRollupService(
                () => sp.GetRequiredService<ITaskItems>(),
                () => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory,
                sp.GetService<IAgentSessionSource>(),
                sp.GetService<TimeProvider>()));

        // The shelf of plans not yet on the roadmap reads the backlog the same way,
        // so it is scoped for the same reason: ITaskItems is.
        services.AddScoped<IImportedPlanSource, ImportedPlanSource>();

        // The Tasks Calendar's plans, read and started through Roadmap's own ports —
        // scoped, because every one of them is. The view choice is kept in the shell's
        // per-device file when the host composed one; the work and pace signals are what
        // the band redraws on, so the Calendar hears them too.
        services.AddScoped<ICalendarPlans>(sp =>
            new RoadmapCalendarPlans(
                sp.GetRequiredService<IRoadmapPlanning>(),
                sp.GetRequiredService<IRoadmapItemRollup>(),
                sp.GetRequiredService<IPlanningVelocity>(),
                sp.GetRequiredService<IImportedPlanSource>(),
                sp.GetService<IAppFeatureSettings>(),
                sp.GetService<ShellNavigationStore>(),
                sp.GetService<IRoadmapWorkChanges>(),
                sp.GetService<IPlanningPace>()));

        // The finished work a measured pace is counted from, read from the backlog —
        // scoped because ITaskItems is, and resolving it per call because the backlog's
        // plan import reaches the roadmap importer, which reaches the pace, which
        // reaches this: taken in the constructor, that loop deadlocks the scope. The
        // repository directory, which turns a task's repository ids into the aliases a
        // pace is kept under, is resolved per call for the same reason.
        services.AddScoped<IRoadmapCompletedWork>(sp =>
            new RoadmapCompletedWork(
                () => sp.GetRequiredService<ITaskItems>(),
                () => sp.GetRequiredService<IRepositoryDirectory>()));

        // Singleton, unlike the adapters above, and deliberately: this one captures no
        // scoped service — only the settings store, which is a singleton in both
        // hosts — so there is no captive dependency to avoid, and the reader's typed
        // pace is one per-device figure rather than something a request owns. It
        // reads through to the store on every call, so a pace changed on the roadmap
        // is live without a restart.
        services.AddSingleton<IPlanningVelocitySettings, PlanningVelocitySource>();

        // The hours a Calendar day is set against: the roadmap's working week and days
        // off, read through the settings port above and never written. A singleton over
        // that singleton. Planned hours go nowhere the other way.
        services.AddSingleton<ICalendarCapacity>(sp =>
            new RoadmapCalendarCapacity(sp.GetRequiredService<IPlanningVelocitySettings>()));

        // The same file is the pace document that travels between devices (local ADR
        // 0018): Roadmap's replication port reads and writes it through its store
        // port. A singleton over the singleton store, for the reason the line above is.
        services.AddSingleton<IRoadmapReplicaStore>(sp => sp.GetRequiredService<PlanningVelocitySettingsStore>());

        // Singleton for the same reason: it holds only the task signal, itself a
        // singleton, and a write in one window has to reach a band open in another.
        services.AddSingleton<IRoadmapWorkChanges>(sp => new RoadmapWorkChanges(sp.GetService<ITaskChangeSignal>()));

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
