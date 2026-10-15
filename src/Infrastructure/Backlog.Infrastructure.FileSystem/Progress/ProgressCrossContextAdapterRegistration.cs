using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Infrastructure.FileSystem.Progress;

/// <summary>
/// What Progress asks of the world outside it, answered by adapters that may see both
/// sides: the tasks a sprint names, read through Tasks' published
/// <see cref="ITaskItems"/> (<see cref="ISprintTaskSource"/>), and the repositories a
/// series may be given, read from Tasks' <see cref="IRepositoryDirectory"/>
/// (<see cref="ISprintRepositories"/>), and the state, merge day and author of the pull
/// requests member tasks link, read from GitHub (<see cref="ISprintPullRequestReader"/>).
/// <para>
/// Scoped, because <see cref="ITaskItems"/> is: a singleton over a scoped port is a
/// captive dependency a validating provider refuses. Call after <c>AddTasksModule</c>
/// and <c>AddProgressModule</c>. The pull request reader replaces the module's own
/// answer, which reads every pull request as Unknown; it is a singleton that keeps
/// nothing itself — what it has read is kept by <see cref="GitHubIntegration"/>, in the
/// cache the In progress view reads through too — and it resolves it per call, so a
/// host without GitHub gets Unknown answers rather than a failed provider.
/// </para>
/// </summary>
public static class ProgressCrossContextAdapterRegistration
{
    public static IServiceCollection AddProgressCrossContextAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ISprintTaskSource>(sp =>
            new SprintTaskSource(
                () => sp.GetRequiredService<ITaskItems>(),
                () => sp.GetService<IRepositoryDirectory>()));
        services.AddScoped<ISprintRepositories>(sp =>
            new SprintRepositories(() => sp.GetService<IRepositoryDirectory>()));
        services.Replace(ServiceDescriptor.Singleton<ISprintPullRequestReader>(sp =>
            new GitHubSprintPullRequests(() => sp.GetService<GitHubIntegration>())));

        return services;
    }
}
