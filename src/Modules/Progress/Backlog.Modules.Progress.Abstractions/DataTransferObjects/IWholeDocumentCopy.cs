namespace Backlog.Modules.Progress.Abstractions.DataTransferObjects;

/// <summary>What every copy of a replicated Progress document carries that last-write-wins
/// is decided on: the id it is kept under, and the stamp it was stored at.
/// <see cref="GoalReplicaCopyDto"/> and <see cref="ImpedimentReplicaCopyDto"/> are its
/// two. An interface only — it adds nothing to the copy's shape.</summary>
public interface IWholeDocumentCopy
{
    /// <summary>The document's id — its id on the replica.</summary>
    Guid Id { get; }

    /// <summary>When it was last written.</summary>
    DateTimeOffset UpdatedAt { get; }
}
