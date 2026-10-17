namespace Backlog.UI.Components.Goals;

/// <summary>What kind of work a <see cref="GoalSubject"/> names.</summary>
public enum GoalSubjectKind
{
    Plan,
    Task,
    PullRequest
}

/// <summary>
/// A piece of work a goal can hold — a plan, a task or a pull request — named by its
/// identity alone, in plain values
/// (<c>.devbook/domain/progress/features.md#attaching-work-to-a-goal</c>).
/// <para>
/// The library's own record rather than the Progress module's item, because the screens
/// that draw a goal control — the task panel, the plan shelf, the work cards — live
/// here and in contexts that may not see Progress. The host that does see it maps one
/// to the other.
/// </para>
/// </summary>
/// <param name="PlanTag">A plan's tag, with or without its <c>+</c> sigil.</param>
/// <param name="TaskId">A task's id.</param>
/// <param name="Repository">A pull request's repository, as <c>owner/name</c>.</param>
/// <param name="Number">A pull request's number.</param>
public sealed record GoalSubject(
    GoalSubjectKind Kind,
    string? PlanTag = null,
    Guid? TaskId = null,
    string? Repository = null,
    int? Number = null)
{
    public static GoalSubject ForPlan(string planTag) => new(GoalSubjectKind.Plan, PlanTag: planTag);

    public static GoalSubject ForTask(Guid taskId) => new(GoalSubjectKind.Task, TaskId: taskId);

    public static GoalSubject ForPullRequest(string repository, int number) =>
        new(GoalSubjectKind.PullRequest, Repository: repository, Number: number);

    /// <summary>
    /// The subject for a pull request a host keys as <c>owner/name#number</c> — the key
    /// a <see cref="Integrations.WorkPullRequest"/> carries. The repository is the text
    /// before the <c>#</c>; null when the key is not that shape, so a host keying its
    /// pull requests some other way draws no goal control rather than a wrong one.
    /// </summary>
    public static GoalSubject? FromPullRequestKey(string? key, int number)
    {
        if (string.IsNullOrWhiteSpace(key) || number <= 0) return null;

        var hash = key.LastIndexOf('#');
        if (hash <= 0) return null;

        var repository = key[..hash].Trim();
        var slash = repository.IndexOf('/');
        if (slash <= 0 || slash == repository.Length - 1 || repository.IndexOf('/', slash + 1) >= 0) return null;

        return ForPullRequest(repository, number);
    }

    /// <summary>How the picker names the subject in a sentence: "task", "plan +tag",
    /// "pull request #12".</summary>
    public string Noun => Kind switch
    {
        GoalSubjectKind.Plan => $"plan +{PlanTag?.TrimStart('+')}",
        GoalSubjectKind.PullRequest => $"pull request #{Number}",
        _ => "task"
    };
}
