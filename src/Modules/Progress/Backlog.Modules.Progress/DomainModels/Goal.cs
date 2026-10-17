using Backlog.Modules.Progress.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.DomainModels;

/// <summary>
/// Something a person is working towards, with a date it should be reached by
/// (<c>.devbook/domain/progress/domain.md#goal</c>, local ADR 0035 §2). The consistency
/// boundary for its own attachments: one item is attached once, and the counting rules
/// see the whole set.
/// <para>
/// Its rules are <c>.devbook/domain/progress/domain.invariants.md#goal</c>, each with a
/// unit test in <c>Backlog.Modules.Progress.UnitTests</c>. The rule across goals — an item
/// belongs to at most one — is <see cref="Services.GoalAssignment"/>'s, which is why
/// <see cref="Attach"/> is internal: nothing else may attach without asking the others.
/// </para>
/// <para>
/// How far it has come and whether it lands in time are not stored; they are read from
/// the attached work on every reading (<see cref="Services.GoalCounting"/>).
/// </para>
/// </summary>
public sealed class Goal
{
    private readonly List<GoalAttachment> _attachments;

    private Goal(Guid id, string title, string? outcome, DateOnly dueDate, List<GoalAttachment> attachments, DateTimeOffset? createdAt)
    {
        Id = id;
        Title = title;
        Outcome = outcome;
        DueDate = dueDate;
        _attachments = attachments;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public string Title { get; private set; }

    /// <summary>One line saying what is true once the goal is reached, or null.</summary>
    public string? Outcome { get; private set; }

    public DateOnly DueDate { get; private set; }

    /// <summary>The plans, tasks and pull requests whose work reaches the goal, in the
    /// order they were attached.</summary>
    public IReadOnlyList<GoalAttachment> Attachments => _attachments;

    /// <summary>When the goal was set, on the desktop that set it; it orders the goals
    /// alike on every device. Null for a goal restored without one.</summary>
    public DateTimeOffset? CreatedAt { get; }

    /// <summary>A new goal holding nothing. Refused without a title
    /// (<c>progress.goal_without_title</c>) or a due date
    /// (<c>progress.goal_without_due_date</c>), or with an outcome that is not one line
    /// (<c>progress.outcome_not_one_line</c>).</summary>
    public static Result<Goal> Create(
        Guid id,
        string? title,
        string? outcome,
        DateOnly? dueDate,
        DateTimeOffset? createdAt = null)
    {
        var cleanTitle = CleanTitle(title);
        if (cleanTitle.IsFailure) return cleanTitle.Error;

        var cleanOutcome = CleanOutcome(outcome);
        if (cleanOutcome.IsFailure) return cleanOutcome.Error;

        if (dueDate is not { } due) return ProgressErrors.GoalWithoutDueDate();

        return new Goal(id, cleanTitle.Value, cleanOutcome.Value, due, [], createdAt);
    }

    /// <summary>Rehydration from storage. Trusted, as every stored aggregate is: the rules
    /// held when it was written. A repeated attachment is kept once.</summary>
    public static Goal Restore(
        Guid id,
        string title,
        string? outcome,
        DateOnly dueDate,
        IEnumerable<GoalAttachment> attachments,
        DateTimeOffset? createdAt = null) =>
        new(id, title, string.IsNullOrWhiteSpace(outcome) ? null : outcome, dueDate, [.. attachments.Distinct()], createdAt);

    public Result Rename(string? title)
    {
        var clean = CleanTitle(title);
        if (clean.IsFailure) return Result.Failure(clean.Error);

        Title = clean.Value;
        return Result.Success();
    }

    /// <summary>Sets the outcome line, or clears it with null or blank text.</summary>
    public Result ChangeOutcome(string? outcome)
    {
        var clean = CleanOutcome(outcome);
        if (clean.IsFailure) return Result.Failure(clean.Error);

        Outcome = clean.Value;
        return Result.Success();
    }

    public Result ChangeDueDate(DateOnly? dueDate)
    {
        if (dueDate is not { } due) return Result.Failure(ProgressErrors.GoalWithoutDueDate());

        DueDate = due;
        return Result.Success();
    }

    /// <summary>Whether the goal holds <paramref name="item"/>, by kind and identity.</summary>
    public bool Holds(GoalAttachment item) => _attachments.Contains(item);

    /// <summary>Attaches <paramref name="item"/>; attaching what the goal holds already
    /// changes nothing, and keeps the day it was first attached. Whether another goal
    /// holds it is <see cref="Services.GoalAssignment"/>'s to check.</summary>
    internal void Attach(GoalAttachment item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!Holds(item)) _attachments.Add(item);
    }

    /// <summary>Detaches <paramref name="item"/>, answering whether the goal held it.
    /// Nothing on the item changes.</summary>
    public bool Detach(GoalAttachment item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _attachments.Remove(item);
    }

    private static Result<string> CleanTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) ? ProgressErrors.GoalWithoutTitle() : title.Trim();

    private static Result<string?> CleanOutcome(string? outcome)
    {
        if (string.IsNullOrWhiteSpace(outcome)) return Result.Success<string?>(null);

        var trimmed = outcome.Trim();
        return trimmed.AsSpan().IndexOfAny('\r', '\n') >= 0
            ? Result.Failure<string?>(ProgressErrors.OutcomeNotOneLine())
            : Result.Success<string?>(trimmed);
    }
}

/// <summary>
/// One piece of work attached to a goal, of one of three kinds
/// (<c>.devbook/domain/progress/domain.md#goal-attachment</c>): a plan by its tag without
/// the <c>+</c> sigil, a task by its id, or a pull request by its repository and number.
/// <para>
/// Equality is by kind and identity alone — a plan tag and a repository without regard to
/// case — so the same item attached on two days is one attachment. It also keeps the day
/// it was made, which equality ignores.
/// </para>
/// </summary>
public sealed class GoalAttachment : IEquatable<GoalAttachment>
{
    private GoalAttachment(GoalAttachmentKind kind, string? plan, Guid? task, string? repository, int? number, DateOnly attachedOn)
    {
        Kind = kind;
        Plan = plan;
        TaskId = task;
        Repository = repository;
        Number = number;
        AttachedOn = attachedOn;
    }

    public GoalAttachmentKind Kind { get; }

    /// <summary>The plan's tag without its sigil, for a <see cref="GoalAttachmentKind.Plan"/>.</summary>
    public string? Plan { get; }

    /// <summary>The task's id, for a <see cref="GoalAttachmentKind.Task"/>.</summary>
    public Guid? TaskId { get; }

    /// <summary>The pull request's repository, as <c>owner/name</c>, for a
    /// <see cref="GoalAttachmentKind.PullRequest"/>.</summary>
    public string? Repository { get; }

    /// <summary>The pull request's number, for a <see cref="GoalAttachmentKind.PullRequest"/>.</summary>
    public int? Number { get; }

    public DateOnly AttachedOn { get; }

    /// <summary>A plan by its tag; refused without one (<c>progress.plan_tag_required</c>).</summary>
    public static Result<GoalAttachment> ForPlan(string? planTag, DateOnly attachedOn)
    {
        var bare = PlanTags.Bare(planTag);
        return bare.Length == 0
            ? ProgressErrors.PlanTagRequired()
            : new GoalAttachment(GoalAttachmentKind.Plan, bare, null, null, null, attachedOn);
    }

    /// <summary>A task by its id; refused for the empty id (<c>progress.task_required</c>).</summary>
    public static Result<GoalAttachment> ForTask(Guid taskId, DateOnly attachedOn) =>
        taskId == Guid.Empty
            ? ProgressErrors.TaskRequired()
            : new GoalAttachment(GoalAttachmentKind.Task, null, taskId, null, null, attachedOn);

    /// <summary>A pull request by its repository and a number above zero; refused
    /// otherwise (<c>progress.pull_request_without_repository</c>) — a bare number names a
    /// different pull request in every repository.</summary>
    public static Result<GoalAttachment> ForPullRequest(string? repository, int? number, DateOnly attachedOn)
    {
        var trimmed = repository?.Trim() ?? string.Empty;
        return trimmed.Length == 0 || number is not > 0
            ? ProgressErrors.PullRequestWithoutRepository()
            : new GoalAttachment(GoalAttachmentKind.PullRequest, null, null, trimmed, number, attachedOn);
    }

    /// <summary>The attachment of the kind <paramref name="kind"/> names, from whichever
    /// fields that kind reads.</summary>
    public static Result<GoalAttachment> Of(
        GoalAttachmentKind kind,
        string? planTag,
        Guid? taskId,
        string? repository,
        int? number,
        DateOnly attachedOn) =>
        kind switch
        {
            GoalAttachmentKind.Plan => ForPlan(planTag, attachedOn),
            GoalAttachmentKind.Task => ForTask(taskId ?? Guid.Empty, attachedOn),
            GoalAttachmentKind.PullRequest => ForPullRequest(repository, number, attachedOn),
            _ => ProgressErrors.AttachmentKindNotAllowed(kind),
        };

    /// <summary>Whether this pull request attachment names the pull request
    /// <paramref name="repository"/> <paramref name="number"/>, as a task records it.</summary>
    public bool IsPullRequest(string? repository, string? number) =>
        Kind == GoalAttachmentKind.PullRequest
        && string.Equals(Repository, repository?.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(Number?.ToString(System.Globalization.CultureInfo.InvariantCulture), number?.Trim().TrimStart('#'), StringComparison.Ordinal);

    public bool Equals(GoalAttachment? other) =>
        other is not null
        && Kind == other.Kind
        && TaskId == other.TaskId
        && Number == other.Number
        && string.Equals(Plan, other.Plan, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Repository, other.Repository, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as GoalAttachment);

    public override int GetHashCode() =>
        HashCode.Combine(
            Kind,
            TaskId,
            Number,
            Plan is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Plan),
            Repository is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Repository));

    public override string ToString() => Kind switch
    {
        GoalAttachmentKind.Plan => $"+{Plan}",
        GoalAttachmentKind.Task => $"task {TaskId:D}",
        _ => $"{Repository}#{Number}",
    };
}
