using Backlog.Modules.Progress.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.DomainModels;

/// <summary>
/// One piece of work an impediment holds back, of one of three kinds
/// (<c>.devbook/domain/progress/domain.md#holds-back</c>): a task by its id, a pull request
/// by its repository and number, or a plan by its tag without the <c>+</c> sigil.
/// <para>
/// Equality is by kind and identity alone — a repository and a plan tag without regard to
/// case — so linking the same item twice is one item, and a task, a pull request and a plan
/// never equal one another. Each factory refuses an item that does not name its whole
/// identity (<c>progress.held_back_item_without_identity</c>), as
/// <see cref="GoalAttachment"/>'s do for a goal.
/// </para>
/// </summary>
public sealed class HeldBackItem : IEquatable<HeldBackItem>
{
    private HeldBackItem(HeldBackKind kind, Guid? taskId, string? repository, int? number, string? planTag)
    {
        Kind = kind;
        TaskId = taskId;
        Repository = repository;
        Number = number;
        PlanTag = planTag;
    }

    public HeldBackKind Kind { get; }

    /// <summary>The task's id, for a <see cref="HeldBackKind.Task"/>.</summary>
    public Guid? TaskId { get; }

    /// <summary>The pull request's repository, as <c>owner/name</c>, for a
    /// <see cref="HeldBackKind.PullRequest"/>.</summary>
    public string? Repository { get; }

    /// <summary>The pull request's number, for a <see cref="HeldBackKind.PullRequest"/>.</summary>
    public int? Number { get; }

    /// <summary>The plan's tag without its sigil, for a <see cref="HeldBackKind.Plan"/>.</summary>
    public string? PlanTag { get; }

    /// <summary>A task by its id; refused for the empty id.</summary>
    public static Result<HeldBackItem> ForTask(Guid taskId) =>
        taskId == Guid.Empty
            ? ProgressErrors.HeldBackItemWithoutIdentity()
            : new HeldBackItem(HeldBackKind.Task, taskId, null, null, null);

    /// <summary>A pull request by its repository and a number above zero; refused otherwise,
    /// because a bare number names a different pull request in every repository.</summary>
    public static Result<HeldBackItem> ForPullRequest(string? repository, int? number)
    {
        var trimmed = repository?.Trim() ?? string.Empty;
        return trimmed.Length == 0 || number is not > 0
            ? ProgressErrors.HeldBackItemWithoutIdentity()
            : new HeldBackItem(HeldBackKind.PullRequest, null, trimmed, number, null);
    }

    /// <summary>A plan by its tag, kept without its sigil; refused for a blank tag or the
    /// sigil alone.</summary>
    public static Result<HeldBackItem> ForPlan(string? planTag)
    {
        var bare = PlanTags.Bare(planTag);
        return bare.Length == 0
            ? ProgressErrors.HeldBackItemWithoutIdentity()
            : new HeldBackItem(HeldBackKind.Plan, null, null, null, bare);
    }

    /// <summary>The item of the kind <paramref name="kind"/> names, from whichever fields
    /// that kind reads; a kind this build does not know names no item.</summary>
    public static Result<HeldBackItem> Of(HeldBackKind kind, Guid? taskId, string? repository, int? number, string? planTag) =>
        kind switch
        {
            HeldBackKind.Task => ForTask(taskId ?? Guid.Empty),
            HeldBackKind.PullRequest => ForPullRequest(repository, number),
            HeldBackKind.Plan => ForPlan(planTag),
            _ => ProgressErrors.HeldBackItemWithoutIdentity(),
        };

    public bool Equals(HeldBackItem? other) =>
        other is not null
        && Kind == other.Kind
        && TaskId == other.TaskId
        && Number == other.Number
        && string.Equals(Repository, other.Repository, StringComparison.OrdinalIgnoreCase)
        && string.Equals(PlanTag, other.PlanTag, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as HeldBackItem);

    public override int GetHashCode() =>
        HashCode.Combine(
            Kind,
            TaskId,
            Number,
            Repository is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Repository),
            PlanTag is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(PlanTag));

    public override string ToString() => Kind switch
    {
        HeldBackKind.Task => $"task {TaskId:D}",
        HeldBackKind.PullRequest => $"{Repository}#{Number}",
        _ => $"+{PlanTag}",
    };
}
