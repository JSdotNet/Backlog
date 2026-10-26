using System.Globalization;
using Backlog.Desktop.UI.Tasks;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.UI.Goals;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.SharedKernel;
using Backlog.UI.Components.Progress;

namespace Backlog.Desktop.UI.InProgress;

/// <summary>
/// The shell's work as the Goals view reads it
/// (<c>.devbook/domain/progress/features.md#the-goals-view</c>): every saved task with its
/// status, plan tags, linked pull requests and whether the header's repository scope holds
/// it; the plan tags Roadmap Planning holds; and the open pull requests the shell's reader
/// found, with the same scope question; and the repositories a bare pull request number in
/// the attach search is read in. The shell builds these because it holds the task
/// list, and Progress UI may not reach Tasks (ADR 0035 §6, local ADR 0028). Read on every
/// asking, never kept.
/// </summary>
internal static class GoalsCandidateSource
{
    public static GoalsCandidates Of(TasksDesktopState state, WorkInProgressReader work)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(work);

        var scoped = state.RowsInRepositoryScope.ToHashSet();
        var aliases = state.SelectedRepositoryAliases;

        return new GoalsCandidates(
            [.. state.Rows.Where(row => row.IsPersisted && row.Id is not null).Select(row => Task(row, scoped.Contains(row)))],
            [.. state.RoadmapTags],
            [.. work.PullRequests.Select(pull => new GoalsPullRequestCandidate(
                pull.RepositoryFullName,
                pull.Number,
                pull.Title,
                InScope(state.RepositoryAliasFor(pull.RepositoryFullName), aliases),
                pull.Url))])
        {
            Repositories = PullRequestRepositories(aliases, state.Repositories)
        };
    }

    /// <summary>
    /// The repositories a bare pull request number in a goal's attach search is read in,
    /// <c>owner/name</c> each: the registered ones the header's scope names, in its order —
    /// the one, when it names one — else every registered one. The Brain names no
    /// repository, so a scope of the Brain alone reads as none.
    /// </summary>
    internal static IReadOnlyList<string> PullRequestRepositories(IReadOnlyList<string> scope, IReadOnlyList<GitHubRepositoryRef> registered)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(registered);

        var scoped = BrainScope.Repositories(scope)
            .Select(alias => registered.FirstOrDefault(repository => string.Equals(repository.Alias, alias, StringComparison.Ordinal)))
            .OfType<GitHubRepositoryRef>()
            .Select(repository => repository.FullName)
            .ToList();

        return scoped.Count > 0 ? scoped : [.. registered.Select(repository => repository.FullName)];
    }

    private static GoalsTaskCandidate Task(EntryRow row, bool inScope) => new(
        row.Id!.Value,
        string.IsNullOrWhiteSpace(row.PreviewTitle) ? "Untitled" : row.PreviewTitle,
        Status(row),
        PlanTagMatch.SlugsCarried(row.PreviewTags),
        [.. row.PullRequestLinks.Select(link => new PullRequestRefDto(link.Repository, link.Number.ToString(CultureInfo.InvariantCulture)))],
        inScope);

    /// <summary>Done and archived read as done; in progress as doing, or blocked while a
    /// <c>blocked:</c> mark holds it.</summary>
    private static GoalTaskStatus Status(EntryRow row) => row.PreviewStatus switch
    {
        EntryStatus.Done or EntryStatus.Archived => GoalTaskStatus.Done,
        EntryStatus.InProgress => row.IsPreviewBlocked ? GoalTaskStatus.Blocked : GoalTaskStatus.Doing,
        _ => GoalTaskStatus.ToDo
    };

    /// <summary>Everything is in scope when nothing is scoped; otherwise the repository's
    /// alias must be one of the scope's. A pull request always comes from a configured
    /// repository, so one with no alias is outside any scope.</summary>
    private static bool InScope(string? alias, IReadOnlyList<string> scope) =>
        scope.Count == 0 || (alias is not null && scope.Contains(alias, StringComparer.Ordinal));
}
