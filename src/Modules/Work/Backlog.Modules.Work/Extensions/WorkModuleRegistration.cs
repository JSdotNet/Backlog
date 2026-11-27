using Backlog.Modules.Work.Abstractions.Plans.Services;
using Backlog.Modules.Work.Abstractions.Tasks.Services;
using Backlog.Modules.Work.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Work.Extensions;

/// <summary>
/// Where the task and the plan meet inside Work (local ADR 0073 §1): the plan's tags
/// offered to the task tag picker (<see cref="IRoadmapTagSource"/>), Import's <c>plan</c>
/// entries laid out on the plan (<see cref="IRoadmapPlanIntake"/>), the plan filter
/// chip's queue (<see cref="IPlanTagSessionRequests"/>), the shelf of imported plans
/// (<see cref="IImportedPlanSource"/>), the finished work a measured pace counts
/// (<see cref="IRoadmapCompletedWork"/>), and the task writes the plan hears
/// (<see cref="IRoadmapWorkChanges"/>).
/// <para>
/// These were adapters in <c>Backlog.Infrastructure.FileSystem</c> while the task and
/// the plan sat in separate modules; both ends are Work now, so the module composes
/// them. Call after <c>AddTasksModule</c> and
/// <c>AddRoadmapModule</c>, which supply the scoped ports these capture
/// (<see cref="IRoadmapPlanning"/> and <see cref="ITaskItems"/>), so each is
/// <c>Scoped</c> too: a singleton over a scoped dependency is a captive dependency that
/// a validating root provider refuses to build.
/// </para>
/// </summary>
public static class WorkModuleRegistration
{
    public static IServiceCollection AddWorkJoins(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Constructor injection where it fits: the container hands the scoped
        // IRoadmapPlanning to the constructor, so no factory reaches into the
        // provider for it.
        services.AddScoped<IRoadmapTagSource, RoadmapPlanTagSource>();

        // The same join the other way: Import hands a document's `plan` entries to
        // the plan's own command through the scoped IRoadmapPlanning (ADR 0013).
        services.AddScoped<IRoadmapPlanIntake, RoadmapPlanIntake>();

        // The plan filter chip's queue icon (local ADR 0038 §2): where a plan is filed,
        // read from the scoped IRoadmapPlanning, and its requests through the plan's
        // own queue port — optional, because a host without a queue registers none,
        // and then the icon is not offered.
        services.AddScoped<IPlanTagSessionRequests>(sp =>
            new RoadmapPlanTagSessionRequests(
                sp.GetRequiredService<IRoadmapPlanning>(),
                sp.GetService<IPlanSessionRequests>()));

        // The shelf of plans not yet on the roadmap reads the backlog, so it is scoped
        // for the same reason: ITaskItems is.
        services.AddScoped<IImportedPlanSource, ImportedPlanSource>();

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

        // A singleton: it holds only the task signal, itself a singleton, and a write in
        // one window has to reach a band open in another.
        services.AddSingleton<IRoadmapWorkChanges>(sp => new RoadmapWorkChanges(sp.GetService<ITaskChangeSignal>()));

        return services;
    }
}
