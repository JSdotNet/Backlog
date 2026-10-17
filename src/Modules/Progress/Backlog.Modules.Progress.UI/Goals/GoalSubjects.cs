using System.Globalization;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.UI.Components.Goals;

namespace Backlog.Modules.Progress.UI.Goals;

/// <summary>
/// The library's <see cref="GoalSubject"/> as the goal port names it. The library draws
/// goal controls without seeing Progress, so its record and the module's item are two
/// shapes of one identity, and this is the one place they meet — the Goals view's
/// rows, matches and No goal included.
/// </summary>
public static class GoalSubjects
{
    /// <summary>The subject the module's <paramref name="item"/> names, a plan's sigil dropped.</summary>
    public static GoalSubject FromItem(GoalItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Kind switch
        {
            GoalAttachmentKind.Plan => GoalSubject.ForPlan(Bare(item.PlanTag)),
            GoalAttachmentKind.Task => GoalSubject.ForTask(item.TaskId ?? Guid.Empty),
            _ => GoalSubject.ForPullRequest(item.Repository ?? string.Empty, item.Number ?? 0)
        };
    }

    public static GoalItemDto ToItem(GoalSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        return subject.Kind switch
        {
            GoalSubjectKind.Plan => GoalItemDto.ForPlan(subject.PlanTag ?? string.Empty),
            GoalSubjectKind.Task => GoalItemDto.ForTask(subject.TaskId ?? Guid.Empty),
            GoalSubjectKind.PullRequest => GoalItemDto.ForPullRequest(subject.Repository ?? string.Empty, subject.Number ?? 0),
            _ => throw new ArgumentOutOfRangeException(nameof(subject), subject.Kind, "A goal holds a plan, a task or a pull request.")
        };
    }

    /// <summary>
    /// The goals the picker offers: every goal not yet reached — one with nothing in it
    /// counts as open — by due date, and the goal that holds the work now whether or not
    /// it is reached, so the picker can mark it Attached.
    /// </summary>
    public static IReadOnlyList<GoalOption> OpenGoals(IReadOnlyList<GoalSummaryDto> goals, Guid? current)
    {
        ArgumentNullException.ThrowIfNull(goals);

        return
        [
            .. goals
                .Where(summary => summary.Progress.Total == 0
                                  || summary.Progress.Done < summary.Progress.Total
                                  || summary.Goal.Id == current)
                .OrderBy(summary => summary.Goal.DueDate)
                .ThenBy(summary => summary.Goal.Title, StringComparer.CurrentCultureIgnoreCase)
                .Select(Option)
        ];
    }

    /// <summary>A plan tag without its sigil, the way the module stores it.</summary>
    public static string Bare(string? tag) => (tag ?? string.Empty).Trim().TrimStart('+').Trim();

    /// <summary>Whether two plan tags name one plan, sigil and case aside.</summary>
    public static bool SamePlan(string? left, string? right) =>
        string.Equals(Bare(left), Bare(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>A pull request's identity for comparing, repository case aside.</summary>
    public static string PullRequestIdentity(string repository, int number) =>
        GoalSubject.ForPullRequest(repository, number).Key.ToUpperInvariant();

    /// <summary><see cref="PullRequestIdentity(string,int)"/> of a reference whose number is
    /// text; null when the number is not one.</summary>
    public static string? PullRequestIdentity(PullRequestRefDto pull)
    {
        ArgumentNullException.ThrowIfNull(pull);
        return int.TryParse(pull.Number, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? PullRequestIdentity(pull.Repository, number)
            : null;
    }

    public static GoalOption Option(GoalSummaryDto summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new GoalOption(summary.Goal.Id, summary.Goal.Title, summary.Progress.Done, summary.Progress.Total, summary.Goal.DueDate);
    }
}
