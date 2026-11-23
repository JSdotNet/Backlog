using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// How an impediment is written on the task feed: a whole document under the
/// impediment's own id with the kind token <c>impediment</c> (local ADR 0035 §9), on the
/// terms a goal travels on (<see cref="GoalReplicaDocuments"/>). The wire half only —
/// which copy wins is Progress's, behind <see cref="IImpedimentReplication"/>. The kind
/// token is persisted on every replica, so it is never renamed.
/// </summary>
public static class ImpedimentReplicaDocuments
{
    /// <summary>The kind token an impediment document carries.</summary>
    public const string ImpedimentType = "impediment";

    /// <summary>What the activity log calls an impediment whose title this build could not read.</summary>
    public const string UntitledTitle = "Impediment";

    private static readonly WholeDocumentKind Kind = new(ImpedimentType, UntitledTitle);

    /// <summary>Whether a document is an impediment's. Ordinal, as every token on the feed is compared.</summary>
    public static bool IsImpediment(TaskChange change) => Kind.Is(change);

    /// <summary>The key an impediment's push mark is kept under, prefixed by the kind token.</summary>
    public static string MarkOf(Guid impedimentId) => Kind.MarkOf(impedimentId);

    /// <summary>The impediment as a task-shaped change, titled by its title.</summary>
    public static TaskChange ToChange(ImpedimentReplicaCopyDto copy)
    {
        ArgumentNullException.ThrowIfNull(copy);

        return Kind.ToChange(copy.Id, copy.Title, copy.Content, copy.UpdatedAt);
    }

    /// <summary>The copy a pulled document carries, as Progress takes it.</summary>
    public static ImpedimentReplicaCopyDto ToCopy(TaskChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new ImpedimentReplicaCopyDto(
            change.Id, WholeDocumentKind.TitleOf(change), WholeDocumentKind.ContentOf(change), change.UpdatedAt);
    }
}
