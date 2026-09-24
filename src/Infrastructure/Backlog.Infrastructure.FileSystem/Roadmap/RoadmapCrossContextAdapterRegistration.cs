using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// What the roadmap plan asks of the world outside it, answered by adapters that
/// may see both sides: the backlog's tag picker offers the plan's tags
/// (<see cref="IRoadmapTagSource"/>), Import lays a document's <c>plan</c> entries
/// out on the plan (<see cref="IRoadmapPlanIntake"/>), a roadmap item rolls up the backlog entries
/// and knowledge chapters it gathers (<see cref="IRoadmapItemRollup"/>), and the
/// reader's own pace is read from the settings file
/// (<see cref="IPlanningVelocity"/>).
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
        // ITaskItems from the same scope the request runs in.
        services.AddScoped<IRoadmapItemRollup>(sp =>
            new RoadmapItemRollupService(
                sp.GetRequiredService<ITaskItems>(),
                () => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));

        // The shelf of plans not yet on the roadmap reads the backlog the same way,
        // so it is scoped for the same reason: ITaskItems is.
        services.AddScoped<IImportedPlanSource, ImportedPlanSource>();

        // Singleton, unlike the two above, and deliberately: this one captures no
        // scoped service — only the settings store, which is a singleton in both
        // hosts — so there is no captive dependency to avoid, and the reader's pace
        // is one per-device figure rather than something a request owns. It still
        // reads through to the store on every call, so a pace changed on the
        // settings screen is live without a restart.
        services.AddSingleton<IPlanningVelocity, PlanningVelocitySource>();

        // Singleton for the same reason: it holds only the task signal, itself a
        // singleton, and a write in one window has to reach a band open in another.
        services.AddSingleton<IRoadmapWorkChanges>(sp => new RoadmapWorkChanges(sp.GetService<ITaskChangeSignal>()));

        return services;
    }
}
