using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.Features.AttachWork;
using Backlog.Modules.Progress.Features.ListGoals;
using Backlog.Modules.Progress.Features.ListImpediments;
using Backlog.Modules.Progress.Features.TrackImpediment;
using Backlog.Modules.Progress.Features.ReadStandUp;
using Backlog.Modules.Progress.Features.SetGoal;
using Backlog.Modules.Progress.Services;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Modules.Progress.Extensions;

/// <summary>
/// The module's composition root. A host calls this once and gets every use case the
/// Progress context offers through <see cref="IGoals"/>, <see cref="IImpediments"/> and
/// <see cref="IStandUp"/>; it never registers a handler itself, and never sees a goal.
/// <para>
/// <see cref="IGoalRepository"/>, <see cref="IImpedimentRepository"/> and
/// <see cref="IProgressTaskSource"/> are deliberately not registered here: which adapters
/// answer them is the host's decision. <see cref="IGoalPullRequestReader"/> falls back to
/// <see cref="UnreadPullRequests"/> when the host registered none.
/// </para>
/// <para>
/// Composing the module draws nothing on its own; the ports always answer, since goals,
/// impediments and the stand-up are always on (local ADR 0035 §10).
/// </para>
/// </summary>
public static class ProgressModuleRegistration
{
    public static IServiceCollection AddProgressModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ProgressGate>();
        services.TryAddSingleton<IGoalPullRequestReader, UnreadPullRequests>();

        // Goals (local ADR 0035). The repository and the replica store are the host's.
        services.TryAddSingleton<GoalSaved>();
        services.AddScoped<GoalChanges>();
        services.AddScoped<ListGoalsQueryHandler>();
        services.AddScoped<SetGoalCommandHandler>();
        services.AddScoped<AttachWorkCommandHandler>();
        services.AddScoped<IGoals>(sp => new Goals(
            sp.GetRequiredService<ListGoalsQueryHandler>(),
            sp.GetRequiredService<SetGoalCommandHandler>(),
            sp.GetRequiredService<AttachWorkCommandHandler>(),
            sp.GetRequiredService<GoalSaved>()));

        // The stand-up (local ADR 0035). The iteration setting and the
        // pace are the host's; the module's own answers keep Week in memory and know no pace.
        services.TryAddSingleton<IIterationSettings, InMemoryIterationSettings>();
        services.TryAddSingleton<IStandUpPace, NoStandUpPace>();
        services.AddScoped(sp => new ReadStandUpQueryHandler(
            sp.GetRequiredService<IGoalRepository>(),
            sp.GetRequiredService<IImpedimentRepository>(),
            sp.GetRequiredService<IProgressTaskSource>(),
            sp.GetRequiredService<IGoalPullRequestReader>(),
            sp.GetRequiredService<IIterationSettings>(),
            sp.GetRequiredService<IStandUpPace>(),
            sp.GetService<IWorkingHoursSettings>()));
        services.AddScoped<IStandUp>(sp => new StandUp(
            sp.GetRequiredService<ReadStandUpQueryHandler>(),
            sp.GetRequiredService<IIterationSettings>(),
            sp.GetRequiredService<TimeProvider>()));

        // Each goal as a `goal` document on the task feed (local ADR 0018).
        services.TryAddSingleton<IGoalReplication>(sp => new GoalReplication(
            sp.GetService<IGoalReplicaStore>(),
            sp.GetRequiredService<GoalSaved>(),
            sp.GetRequiredService<ProgressGate>()));

        // Impediments (local ADR 0035 §5). Composed through factories
        // so a host that keeps no impediments still builds: the repository and the
        // blocked-task source are the host's, and are asked for only when a call needs them.
        services.TryAddSingleton<ImpedimentSaved>();
        services.AddScoped(sp => new ImpedimentChanges(
            sp.GetRequiredService<IImpedimentRepository>(),
            sp.GetRequiredService<ProgressGate>(),
            sp.GetRequiredService<ImpedimentSaved>()));
        services.AddScoped(sp => new ListImpedimentsQueryHandler(sp.GetRequiredService<IImpedimentRepository>()));
        services.AddScoped(sp => new ImpedimentCommandHandler(
            sp.GetRequiredService<ImpedimentChanges>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<IImpediments>(sp => new Impediments(
            sp.GetRequiredService<ListImpedimentsQueryHandler>(),
            sp.GetRequiredService<ImpedimentCommandHandler>(),
            sp.GetRequiredService<ImpedimentSaved>()));
        services.AddScoped<IBlockedTaskPolicy>(sp => new BlockedTaskPolicy(
            sp.GetRequiredService<IBlockedTaskSource>(),
            sp.GetRequiredService<ImpedimentChanges>(),
            sp.GetRequiredService<TimeProvider>()));

        // Each impediment as an `impediment` document on the task feed, as each goal is.
        services.TryAddSingleton<IImpedimentReplication>(sp => new ImpedimentReplication(
            sp.GetService<IImpedimentReplicaStore>(),
            sp.GetRequiredService<ImpedimentSaved>(),
            sp.GetRequiredService<ProgressGate>()));

        // What the phone reads the stand-up with and writes impediments through (local
        // ADR 0035 §8): the stand-up context document, and the impediment edits it queues.
        services.TryAddSingleton<IStandUpContextSource>(sp => new StandUpContextSource(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IGoalRepository>(),
            sp.GetRequiredService<IIterationSettings>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<IWorkingHoursSettings>()));
        services.TryAddSingleton<IImpedimentEditIntake>(sp => new ImpedimentEditIntake(
            sp.GetRequiredService<IServiceScopeFactory>()));

        return services;
    }
}
