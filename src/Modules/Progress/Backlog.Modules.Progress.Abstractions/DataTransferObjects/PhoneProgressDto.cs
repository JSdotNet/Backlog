using Backlog.SharedKernel;

namespace Backlog.Modules.Progress.Abstractions.DataTransferObjects;

/// <summary>
/// A goal as a <c>goal</c> document on the task feed says it, read by a head that keeps no
/// goals of its own — the phone (local ADR 0035 §8, §9).
/// </summary>
/// <param name="Goal">The goal; for a tombstone its id and title and no attachments.</param>
/// <param name="CreatedAt">When it was set, which orders the goals alike on every device;
/// null in a document that does not say.</param>
/// <param name="Deleted">Whether the document is the tombstone a deletion leaves.</param>
public sealed record GoalDocumentDto(GoalDto Goal, DateTimeOffset? CreatedAt, bool Deleted);

/// <summary>An impediment as an <c>impediment</c> document says it, read by the phone.</summary>
/// <param name="CreatedAt">When it was raised, which orders the impediments alike on every device.</param>
public sealed record ImpedimentDocumentDto(ImpedimentDto Impediment, DateTimeOffset? CreatedAt);

/// <summary>
/// What a desktop knows that the phone needs to read the stand-up and cannot find out
/// itself, written whole as the one <c>stand-up-context</c> document on the task feed:
/// what every pull request the goals count said about itself — its state, merge day,
/// checks and approvals — so the phone needs no GitHub access, the person's pace, their
/// working week, and the iteration they read the stand-up by. The newest reading wins, so
/// a refresh on one desktop never displaces an edit to a goal.
/// </summary>
/// <param name="ReadAt">When the desktop read it.</param>
/// <param name="PullRequests">Every pull request the goals count, once each.</param>
/// <param name="PointsPerWeek">The person's pace, or null when none is known.</param>
/// <param name="Week">The person's working week, or null for Monday to Friday.</param>
/// <param name="Length">The iteration length the person reads by on that desktop, or null.</param>
/// <param name="TwoWeekStart">The day two-week windows count from, or null.</param>
public sealed record StandUpContextDto(
    DateTimeOffset ReadAt,
    IReadOnlyList<GoalPullRequestDto> PullRequests,
    decimal? PointsPerWeek,
    WorkingHours? Week,
    IterationLength? Length,
    DateOnly? TwoWeekStart);

/// <summary>
/// One change the phone asks for on an impediment, as an <c>impediment-edit</c> document on
/// the task feed: a command, not the impediment — the phone owns no canonical data, and the
/// desktop applies it through its own impediment features, then retires the document.
/// </summary>
/// <param name="ImpedimentId">The impediment it changes.</param>
/// <param name="Kind">Which field it changes.</param>
/// <param name="State">The state to move to, for <see cref="ImpedimentEditKind.State"/>.</param>
/// <param name="With">Who it is with at that step, for <see cref="ImpedimentEditKind.State"/>; may be null.</param>
/// <param name="CheckAgain">The day to look again, for <see cref="ImpedimentEditKind.CheckAgain"/>; null clears it.</param>
/// <param name="Note">The note, for <see cref="ImpedimentEditKind.Note"/>; null or blank clears it.</param>
/// <param name="AskedAt">When the person asked for it on the phone.</param>
/// <param name="WasState">The state the phone saw it in, for <see cref="ImpedimentEditKind.State"/>.</param>
/// <param name="WasCheckAgain">The check-again day the phone saw, for <see cref="ImpedimentEditKind.CheckAgain"/>.</param>
/// <param name="WasNote">The note the phone saw, for <see cref="ImpedimentEditKind.Note"/>.</param>
/// <remarks>
/// The <c>Was</c> values make an edit a compare-and-set on its one field: a desktop applies it
/// only while the field still says what the phone saw, so a change made on a desktop since —
/// the newer change — stands, and an edit met a second time finds its own value already there
/// and changes nothing. The phone sends, for a field it has already asked to change, the value
/// it asked for, so its edits in a row apply in the order they were asked.
/// </remarks>
public sealed record ImpedimentEditDto(
    Guid ImpedimentId,
    ImpedimentEditKind Kind,
    ImpedimentState? State,
    string? With,
    DateOnly? CheckAgain,
    string? Note,
    DateTimeOffset AskedAt,
    ImpedimentState? WasState = null,
    DateOnly? WasCheckAgain = null,
    string? WasNote = null);
