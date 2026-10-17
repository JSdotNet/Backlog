using System.Globalization;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Progress.UI.Goals;

/// <summary>
/// The key the board hands back for a piece of work, and the item it stands for: the
/// library's <see cref="Backlog.UI.Components.Progress.GoalsItem.Key"/> is opaque to it, so
/// this is the one place that writes and reads it — <c>plan:&lt;tag&gt;</c>,
/// <c>task:&lt;id&gt;</c> and <c>pr:&lt;owner/name&gt;#&lt;number&gt;</c>.
/// </summary>
public static class GoalsKeys
{
    private const string Plan = "plan:";
    private const string Task = "task:";
    private const string PullRequest = "pr:";

    /// <summary>The key of <paramref name="item"/>.</summary>
    public static string For(GoalItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Kind switch
        {
            GoalAttachmentKind.Plan => Plan + Bare(item.PlanTag),
            GoalAttachmentKind.Task => Task + item.TaskId?.ToString(),
            _ => ForPullRequest(item.Repository ?? string.Empty, item.Number ?? 0)
        };
    }

    /// <summary>The key of the pull request <paramref name="number"/> in
    /// <paramref name="repository"/>.</summary>
    public static string ForPullRequest(string repository, int number) =>
        $"{PullRequest}{repository}#{number.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The key of the task <paramref name="taskId"/>.</summary>
    public static string ForTask(Guid taskId) => Task + taskId;

    /// <summary>The key of the plan <paramref name="tag"/>, the sigil dropped.</summary>
    public static string ForPlan(string tag) => Plan + Bare(tag);

    /// <summary>Reads <paramref name="key"/> back; false for a key that names nothing.</summary>
    public static bool TryRead(string? key, out GoalItemDto item)
    {
        item = default!;
        if (string.IsNullOrWhiteSpace(key)) return false;

        if (key.StartsWith(Plan, StringComparison.Ordinal))
        {
            var tag = Bare(key[Plan.Length..]);
            if (tag.Length == 0) return false;

            item = GoalItemDto.ForPlan(tag);
            return true;
        }

        if (key.StartsWith(Task, StringComparison.Ordinal))
        {
            if (!Guid.TryParse(key[Task.Length..], out var id)) return false;

            item = GoalItemDto.ForTask(id);
            return true;
        }

        if (key.StartsWith(PullRequest, StringComparison.Ordinal))
        {
            var rest = key[PullRequest.Length..];
            var hash = rest.LastIndexOf('#');
            if (hash <= 0
                || !int.TryParse(rest[(hash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                || number <= 0)
            {
                return false;
            }

            item = GoalItemDto.ForPullRequest(rest[..hash], number);
            return true;
        }

        return false;
    }

    /// <summary>A plan tag without its sigil, the way the module stores it.</summary>
    public static string Bare(string? tag) => (tag ?? string.Empty).Trim().TrimStart('+').Trim();

    /// <summary>Whether two plan tags name one plan, sigil and case aside.</summary>
    public static bool SamePlan(string? left, string? right) =>
        string.Equals(Bare(left), Bare(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>A pull request's identity for comparing, repository case aside.</summary>
    public static string PullRequestIdentity(string repository, int number) =>
        ForPullRequest(repository, number).ToUpperInvariant();

    /// <summary><see cref="PullRequestIdentity(string,int)"/> of a reference whose number is
    /// text; null when the number is not one.</summary>
    public static string? PullRequestIdentity(PullRequestRefDto pull)
    {
        ArgumentNullException.ThrowIfNull(pull);
        return int.TryParse(pull.Number, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? PullRequestIdentity(pull.Repository, number)
            : null;
    }
}
