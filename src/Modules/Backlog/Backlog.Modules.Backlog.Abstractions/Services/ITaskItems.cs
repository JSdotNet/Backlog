using Backlog.Modules.Backlog.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Backlog.Abstractions.Services;

/// <summary>
/// Everything a host may do to the backlog, in one port.
/// <para>
/// The use cases themselves are feature slices with their own handlers (ADR
/// 0009); this is the service contract ADR 0005 asks a module to publish, and it
/// is a plain delegation to those handlers. An API host would map an endpoint
/// straight onto a handler and skip it — a desktop editor that calls six use
/// cases from one screen would otherwise take six constructor arguments to say
/// "the backlog".
/// </para>
/// <para>
/// Note what is not here: no aggregate, no repository, no way to set a field.
/// Changing an entry means saving its text, because in this product the text is
/// the entry.
/// </para>
/// </summary>
public interface ITaskItems
{
    /// <summary>Everything in the backlog, in rank order.</summary>
    Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes one block of entry markdown down — creating the entry when
    /// <paramref name="id"/> is null. Fails with a validation error while the
    /// text still has no title, which is an ordinary state for something
    /// half-typed.
    /// <para>
    /// The result is a <see cref="SavedTaskDto"/> rather than the entry alone
    /// because one save can produce two entries: completing a repeating entry
    /// leaves it completed and creates the next occurrence, and the caller has no
    /// other way to hear about the second one.
    /// </para></summary>
    Task<Result<SavedTaskDto>> SaveFromTextAsync(
        Guid? id,
        string rawText,
        int order,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Position in the list becomes the entry's rank.</summary>
    Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default);

    /// <summary>Records that an entry became an external artifact. The host
    /// creates the issue; the module only remembers it.</summary>
    Task<Result<TaskItemDto>> LinkToIssueAsync(
        Guid id,
        string repoId,
        string externalId,
        string targetType,
        CancellationToken cancellationToken = default);

    /// <summary>Notes that an entry was actually used for something.</summary>
    Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default);

    /// <summary>Brings in a plan — a block of entry text naming more than one
    /// prompt — and turns it into backlog entries in one step. See
    /// <c>ImportPlanCommand</c> and ADR 0004 for what "brings in" means: the
    /// same grammar and repository port every other entry goes through, with
    /// two-pass dependency resolution and upsert-by-plan on top.
    /// <para>
    /// <paramref name="defaultRepo"/> is the Import dialog's optional "Target
    /// repository" field. It is applied only to an entry whose own text names
    /// no <c>repo:</c> — an entry that already specifies one keeps it.
    /// </para></summary>
    Task<Result<ImportPlanResultDto>> ImportPlanAsync(
        string rawText,
        string? defaultRepo = null,
        CancellationToken cancellationToken = default);
}
