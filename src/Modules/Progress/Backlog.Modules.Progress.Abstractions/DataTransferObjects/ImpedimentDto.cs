namespace Backlog.Modules.Progress.Abstractions.DataTransferObjects;

/// <summary>
/// One impediment as a caller receives it (<c>.devbook/domain/progress/domain.md#impediment</c>):
/// what holds the person's work back, where it stands, and its history.
/// </summary>
/// <param name="Id">The impediment's id.</param>
/// <param name="Title">What is in the way, such as "Bank sample files missing".</param>
/// <param name="State">Where it stands now.</param>
/// <param name="ReportedTo">Who it was last reported or escalated to, in words, or null
/// while nobody has been told.</param>
/// <param name="HandledBy">Who handles it, in words, or null while nobody does.</param>
/// <param name="CheckAgain">The day the person means to look at it next, or null.</param>
/// <param name="Note">The person's note on it, or null.</param>
/// <param name="History">Every move in the order it happened; the first is always New.</param>
/// <param name="HoldsBack">The tasks, pull requests and plans it holds back, in the order
/// they were linked.</param>
public sealed record ImpedimentDto(
    Guid Id,
    string Title,
    ImpedimentState State,
    string? ReportedTo,
    string? HandledBy,
    DateOnly? CheckAgain,
    string? Note,
    IReadOnlyList<ImpedimentStateChangeDto> History,
    IReadOnlyList<HeldBackItemDto> HoldsBack)
{
    /// <summary>Whether it still holds work back: every state but Resolved.</summary>
    public bool IsOpen => State != ImpedimentState.Resolved;

    /// <summary>The ids of the tasks it holds back, in the order they were linked.</summary>
    public IReadOnlyList<Guid> HeldBackTasks =>
        [.. HoldsBack.Where(item => item.Kind == HeldBackKind.Task && item.TaskId is not null).Select(item => item.TaskId!.Value)];

    /// <summary>The day it was raised: the day of its first step.</summary>
    public DateOnly RaisedOn => History[0].On;

    /// <summary>The day it was resolved, or null while it is open.</summary>
    public DateOnly? ResolvedOn => IsOpen ? null : History[^1].On;
}

/// <summary>One step in an impediment's history
/// (<c>.devbook/domain/progress/domain.md#state-change</c>), such as "Reported · bank support".</summary>
/// <param name="State">The state it moved to.</param>
/// <param name="On">The day it moved.</param>
/// <param name="With">Who it was with at that step, in words, or null.</param>
public sealed record ImpedimentStateChangeDto(ImpedimentState State, DateOnly On, string? With);

/// <summary>
/// One piece of work an impediment holds back
/// (<c>.devbook/domain/progress/domain.md#holds-back</c>): a task by its id, a pull request
/// by its repository and number, or a plan by its tag. Only the fields its kind reads are set.
/// </summary>
/// <param name="Kind">Which of the three kinds it is.</param>
/// <param name="TaskId">A task's id.</param>
/// <param name="Repository">A pull request's repository, as <c>owner/name</c>.</param>
/// <param name="Number">A pull request's number.</param>
/// <param name="PlanTag">A plan's tag, without its <c>+</c> sigil as the module answers it.</param>
public sealed record HeldBackItemDto(
    HeldBackKind Kind,
    Guid? TaskId = null,
    string? Repository = null,
    int? Number = null,
    string? PlanTag = null)
{
    public static HeldBackItemDto ForTask(Guid taskId) => new(HeldBackKind.Task, TaskId: taskId);

    public static HeldBackItemDto ForPullRequest(string repository, int number) =>
        new(HeldBackKind.PullRequest, Repository: repository, Number: number);

    public static HeldBackItemDto ForPlan(string planTag) => new(HeldBackKind.Plan, PlanTag: planTag);
}

/// <summary>What a person or a session gives to raise an impediment.</summary>
/// <param name="Title">What is in the way. Refused when blank.</param>
/// <param name="HoldsBack">The tasks, pull requests and plans it holds back from the start,
/// in that order; may be empty. One that does not name its whole identity refuses the raise.</param>
/// <param name="Note">An optional note.</param>
public sealed record RaiseImpedimentRequest(string? Title, IReadOnlyList<HeldBackItemDto>? HoldsBack = null, string? Note = null);

/// <summary>
/// One copy of a replicated impediment, on the terms <see cref="GoalReplicaCopyDto"/> gives
/// a goal: its id, the title it shows under in the sync log, the stored text whole, and
/// the stamp it was stored at.
/// </summary>
/// <param name="Id">The impediment's id — the document's id on the replica.</param>
/// <param name="Title">What the impediment is called, for the sync log.</param>
/// <param name="Content">The impediment exactly as it is stored.</param>
/// <param name="UpdatedAt">When it was last written — the stamp last-write-wins is decided on.</param>
public sealed record ImpedimentReplicaCopyDto(Guid Id, string Title, string Content, DateTimeOffset UpdatedAt);
