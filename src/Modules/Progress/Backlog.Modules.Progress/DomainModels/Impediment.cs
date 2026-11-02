using Backlog.Modules.Progress.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.DomainModels;

/// <summary>
/// Something outside the person's own work that holds it back, tracked until it is
/// resolved (<c>.devbook/domain/progress/domain.md#impediment</c>, local ADR 0035 §5).
/// <para>
/// Its rules are <c>.devbook/domain/progress/domain.invariants.md#impediment</c>, each with
/// a unit test in <c>Backlog.Modules.Progress.UnitTests</c>: a title, a start in New, moves
/// forward only with Escalated the one state a move may skip, any open state to Resolved,
/// Resolved for good, every move recorded with days that never go backwards, and each item
/// held back once, linking and unlinking leaving the state and history alone.
/// </para>
/// <para>
/// It names the tasks, pull requests and plans it holds back by their identity and nothing
/// else (<see cref="HoldsBack"/>); the goal it holds back is read through them, never
/// stored here.
/// </para>
/// </summary>
public sealed class Impediment
{
    private readonly List<ImpedimentStateChange> _history;
    private readonly List<HeldBackItem> _holdsBack;

    private Impediment(
        Guid id,
        string title,
        string? reportedTo,
        string? handledBy,
        DateOnly? checkAgain,
        string? note,
        List<ImpedimentStateChange> history,
        List<HeldBackItem> holdsBack,
        DateTimeOffset? createdAt)
    {
        Id = id;
        Title = title;
        ReportedTo = reportedTo;
        HandledBy = handledBy;
        CheckAgain = checkAgain;
        Note = note;
        _history = history;
        _holdsBack = holdsBack;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public string Title { get; private set; }

    /// <summary>Where it stands: the state of its last step.</summary>
    public ImpedimentState State => _history[^1].State;

    /// <summary>Who it was last reported or escalated to, in words, or null.</summary>
    public string? ReportedTo { get; private set; }

    /// <summary>Who handles it, in words, or null.</summary>
    public string? HandledBy { get; private set; }

    /// <summary>The day the person means to look at it next, or null.</summary>
    public DateOnly? CheckAgain { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Every move in the order it happened; the first is always New.</summary>
    public IReadOnlyList<ImpedimentStateChange> History => _history;

    /// <summary>The tasks, pull requests and plans it holds back, in the order they were linked.</summary>
    public IReadOnlyList<HeldBackItem> HoldsBack => _holdsBack;

    /// <summary>When it was raised, on the device that raised it; it orders the impediments
    /// alike on every device. Null for one restored without it.</summary>
    public DateTimeOffset? CreatedAt { get; }

    /// <summary>Open until it is Resolved.</summary>
    public bool IsOpen => State != ImpedimentState.Resolved;

    /// <summary>The day it was raised: the day of its first step.</summary>
    public DateOnly RaisedOn => _history[0].On;

    /// <summary>The day it was resolved, or null while it is open.</summary>
    public DateOnly? ResolvedOn => IsOpen ? null : _history[^1].On;

    /// <summary>A new impediment in state New on <paramref name="raisedOn"/>, holding back
    /// nothing yet. Refused without a title (<c>progress.impediment_without_title</c>).</summary>
    public static Result<Impediment> Raise(
        Guid id,
        string? title,
        DateOnly raisedOn,
        DateTimeOffset? createdAt = null)
    {
        var clean = CleanTitle(title);
        if (clean.IsFailure) return clean.Error;

        return new Impediment(
            id,
            clean.Value,
            null,
            null,
            null,
            null,
            [new ImpedimentStateChange(ImpedimentState.New, raisedOn, null)],
            [],
            createdAt);
    }

    /// <summary>Rehydration from storage. Trusted, as every stored aggregate is: the rules
    /// held when it was written. A history without a step reads as New on
    /// <paramref name="fallbackRaisedOn"/>, and a repeated item is kept once, where it was
    /// first linked.</summary>
    public static Impediment Restore(
        Guid id,
        string title,
        string? reportedTo,
        string? handledBy,
        DateOnly? checkAgain,
        string? note,
        IEnumerable<ImpedimentStateChange> history,
        IEnumerable<HeldBackItem> holdsBack,
        DateTimeOffset? createdAt,
        DateOnly fallbackRaisedOn)
    {
        List<ImpedimentStateChange> steps = [.. history];
        if (steps.Count == 0) steps.Add(new ImpedimentStateChange(ImpedimentState.New, fallbackRaisedOn, null));

        return new Impediment(
            id,
            title,
            Clean(reportedTo),
            Clean(handledBy),
            checkAgain,
            Clean(note),
            steps,
            [.. holdsBack.OfType<HeldBackItem>().Distinct()],
            createdAt);
    }

    public Result Rename(string? title)
    {
        var clean = CleanTitle(title);
        if (clean.IsFailure) return Result.Failure(clean.Error);

        Title = clean.Value;
        return Result.Success();
    }

    /// <summary>
    /// Moves to <paramref name="state"/> on <paramref name="on"/>, with
    /// <paramref name="with"/> as who it is with at that step. Reported and Escalated
    /// record <paramref name="with"/> as who it was reported to, Being handled as who
    /// handles it. Refused for a resolved impediment (<c>progress.impediment_resolved</c>),
    /// for a move the state order does not allow (<c>progress.impediment_move_not_allowed</c>),
    /// and for a day before the last step (<c>progress.impediment_move_before_last_step</c>).
    /// </summary>
    public Result MoveTo(ImpedimentState state, DateOnly on, string? with = null)
    {
        if (!IsOpen) return Result.Failure(ProgressErrors.ImpedimentResolved());
        if (!ImpedimentMoves.Allows(State, state)) return Result.Failure(ProgressErrors.ImpedimentMoveNotAllowed(State, state));

        var last = _history[^1].On;
        if (on < last) return Result.Failure(ProgressErrors.ImpedimentMoveBeforeLastStep(last));

        var who = Clean(with);
        _history.Add(new ImpedimentStateChange(state, on, who));

        switch (state)
        {
            case ImpedimentState.Reported or ImpedimentState.Escalated when who is not null:
                ReportedTo = who;
                break;
            case ImpedimentState.BeingHandled when who is not null:
                HandledBy = who;
                break;
        }

        return Result.Success();
    }

    /// <summary>Sets the day to look at it again, or clears it with null.</summary>
    public void SetCheckAgain(DateOnly? checkAgain) => CheckAgain = checkAgain;

    /// <summary>Replaces the note, or clears it with null or blank text.</summary>
    public void EditNote(string? note) => Note = Clean(note);

    /// <summary>Links an item it holds back; linking one it holds back already, by kind and
    /// identity, changes nothing. Adds no step to the history.</summary>
    public void HoldBack(HeldBackItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!_holdsBack.Contains(item)) _holdsBack.Add(item);
    }

    /// <summary>Unlinks an item, answering whether it held it back; one it does not hold
    /// back changes nothing. Adds no step to the history.</summary>
    public bool Release(HeldBackItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return _holdsBack.Remove(item);
    }

    /// <summary>Whether it holds back <paramref name="item"/>.</summary>
    public bool Holds(HeldBackItem item) => _holdsBack.Contains(item);

    private static Result<string> CleanTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) ? ProgressErrors.ImpedimentWithoutTitle() : title.Trim();

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>One step in an impediment's history
/// (<c>.devbook/domain/progress/domain.md#state-change</c>): the state it moved to, the day
/// it moved, and who it was with at that step, or null.</summary>
public sealed record ImpedimentStateChange(ImpedimentState State, DateOnly On, string? With);
