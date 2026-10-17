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
/// <param name="HoldsBack">The ids of the tasks it holds back, in the order they were linked.</param>
public sealed record ImpedimentDto(
    Guid Id,
    string Title,
    ImpedimentState State,
    string? ReportedTo,
    string? HandledBy,
    DateOnly? CheckAgain,
    string? Note,
    IReadOnlyList<ImpedimentStateChangeDto> History,
    IReadOnlyList<Guid> HoldsBack)
{
    /// <summary>Whether it still holds work back: every state but Resolved.</summary>
    public bool IsOpen => State != ImpedimentState.Resolved;

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

/// <summary>What a person or a session gives to raise an impediment.</summary>
/// <param name="Title">What is in the way. Refused when blank.</param>
/// <param name="HoldsBack">The tasks it holds back from the start, by id; may be empty.</param>
/// <param name="Note">An optional note.</param>
public sealed record RaiseImpedimentRequest(string? Title, IReadOnlyList<Guid>? HoldsBack = null, string? Note = null);

/// <summary>One task marked blocked by hand, as the
/// <c>.devbook/domain/progress/domain.md#blocked-task-policy</c> reads it from Tasks.</summary>
/// <param name="Id">The task's id.</param>
/// <param name="Title">The task's title; a raised impediment takes it.</param>
/// <param name="BlockedSince">The day the task was marked blocked.</param>
public sealed record BlockedTaskDto(Guid Id, string Title, DateOnly BlockedSince);

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
