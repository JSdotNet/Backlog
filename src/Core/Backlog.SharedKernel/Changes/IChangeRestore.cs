namespace Backlog.SharedKernel.Changes;

/// <summary>
/// Takes a change set back (local ADR 0061 §4): a later save the person makes, never
/// a rewind. <see cref="PlanAsync"/> says what a restore would do, so the person can
/// confirm it; <see cref="RestoreAsync"/> does it, as a change set of its own.
/// </summary>
public interface IChangeRestore
{
    /// <summary>What restoring <paramref name="changeSetId"/> would do to each document
    /// it changed, newest revision first, and which of them changed since. Null for a
    /// change set the journal does not hold.</summary>
    Task<RestorePlan?> PlanAsync(Guid changeSetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores <paramref name="changeSetId"/> under a new change set, actor
    /// <see cref="ChangeActor.Person"/>, labelled "Restore of &lt;label&gt;", and points
    /// the restored change set's <c>undone_by</c> at it.
    /// <para>
    /// Each document is checked again as it is written: one whose stamp no longer
    /// matches what the change set left is a conflict, kept as it is unless
    /// <paramref name="restoreAnyway"/> names it. A document the change set created is
    /// tombstoned; any other gets its prior content back, stamped now.
    /// </para>
    /// </summary>
    Task<RestoreOutcome> RestoreAsync(
        Guid changeSetId,
        IReadOnlySet<DocumentKey> restoreAnyway,
        CancellationToken cancellationToken = default);
}

/// <summary>One journalled document: what the journal calls its kind, and its id.</summary>
public sealed record DocumentKey(string Kind, string Id);

/// <summary>What a restore does to one document.</summary>
public enum RestoreAction
{
    /// <summary>Saved again with the content it had before the change set, stamped now.</summary>
    Revert,

    /// <summary>The change set created it, so it is tombstoned.</summary>
    Tombstone,

    /// <summary>Nothing: the journal keeps this document, but a restore cannot write
    /// it back — a kind with no reader, or one with no tombstone of its own.</summary>
    Unsupported,
}

/// <summary>One document of a <see cref="RestorePlan"/>.</summary>
/// <param name="Key">The document.</param>
/// <param name="Title">Its title now, or as it was.</param>
/// <param name="Action">What a restore does to it.</param>
/// <param name="Conflict">True when it changed after the change set left it, so the
/// person chooses between keeping it and restoring it anyway.</param>
public sealed record RestoreStep(DocumentKey Key, string Title, RestoreAction Action, bool Conflict);

/// <summary>What restoring one change set would do.</summary>
/// <param name="ChangeSetId">The change set.</param>
/// <param name="Label">Its label, or null.</param>
/// <param name="UndoneBy">The change set that already restored it, or null.</param>
/// <param name="Steps">One per document, newest revision first.</param>
public sealed record RestorePlan(Guid ChangeSetId, string? Label, Guid? UndoneBy, IReadOnlyList<RestoreStep> Steps)
{
    /// <summary>The documents a restore can write.</summary>
    public IEnumerable<RestoreStep> Restorable => Steps.Where(step => step.Action is not RestoreAction.Unsupported);

    /// <summary>How many of those changed since.</summary>
    public int ConflictCount => Restorable.Count(step => step.Conflict);

    /// <summary>The label the restore's own change set carries.</summary>
    public string RestoreLabel => RestoreLabelFor(Label);

    /// <summary>"Restore of &lt;label&gt;", for a change set with or without one.</summary>
    public static string RestoreLabelFor(string? label) =>
        $"Restore of {(string.IsNullOrWhiteSpace(label) ? "an unlabelled change set" : label)}";
}

/// <summary>What one restore did.</summary>
/// <param name="RestoreChangeSetId">The restore's own change set, or null when it wrote
/// nothing and so recorded none.</param>
/// <param name="Label">That change set's label.</param>
/// <param name="Restored">Documents written back or tombstoned.</param>
/// <param name="Kept">Documents left as they are: conflicts kept, and kinds a restore
/// cannot write.</param>
public sealed record RestoreOutcome(Guid? RestoreChangeSetId, string Label, int Restored, int Kept);
