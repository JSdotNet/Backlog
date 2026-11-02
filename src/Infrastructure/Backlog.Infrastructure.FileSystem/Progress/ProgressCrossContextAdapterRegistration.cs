using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Infrastructure.FileSystem.Progress;

/// <summary>
/// What Progress asks of the world outside it, answered by adapters that may see both
/// sides: the tasks goals and the stand-up name, read through Tasks' published
/// <see cref="ITaskItems"/> (<see cref="IProgressTaskSource"/>), and the state, merge
/// day, checks and approvals of the pull requests goals count and tasks link, read through
/// Repository Management's contract (<see cref="IGoalPullRequestReader"/>, local ADR 0042 §5).
/// <para>
/// Scoped, because <see cref="ITaskItems"/> is: a singleton over a scoped port is a
/// captive dependency a validating provider refuses. Call after <c>AddTasksModule</c>
/// and <c>AddProgressModule</c>. The pull request reader replaces the module's own
/// answer, which reads every pull request as Unknown; it is a singleton that keeps
/// nothing itself — what it has read is kept by the contract's one cache, shared with
/// every other reader of a pull request — and it resolves <see cref="IPullRequests"/> per
/// call, so a host without the module gets Unknown answers rather than a failed provider.
/// </para>
/// </summary>
public static class ProgressCrossContextAdapterRegistration
{
    public static IServiceCollection AddProgressCrossContextAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IProgressTaskSource>(sp =>
            new ProgressTaskSource(
                () => sp.GetRequiredService<ITaskItems>(),
                () => sp.GetService<IRepositoryDirectory>()));
        services.Replace(ServiceDescriptor.Singleton<IGoalPullRequestReader>(sp =>
            new RepositoryManagementGoalPullRequests(
                () => sp.GetService<IPullRequests>(),
                () => sp.GetService<IRegisteredRepositories>())));

        // The pace the stand-up's points tag compares planned effort with: Roadmap
        // Planning's global pace, replacing the module's answer of none. Scoped, as the
        // roadmap's port is.
        services.Replace(ServiceDescriptor.Scoped<IStandUpPace>(sp =>
            new PlanningVelocityStandUpPace(() => sp.GetService<IPlanningVelocity>())));

        return services;
    }
}
