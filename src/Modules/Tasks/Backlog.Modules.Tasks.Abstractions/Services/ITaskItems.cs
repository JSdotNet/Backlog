using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Abstractions.Services;

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
    /// </para>
    /// <para>
    /// <paramref name="sourceInboxId"/> is provenance for an entry the Inbox
    /// routed here — the inbox item's id — and null for one typed by hand. It
    /// is read on create only; the aggregate holds it as constructor-only, so an
    /// update leaves whatever the entry was born with.
    /// </para></summary>
    Task<Result<SavedTaskDto>> SaveFromTextAsync(
        Guid? id,
        string rawText,
        int order,
        string? sourceInboxId = null,
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

    /// <summary>
    /// Brings every entry's <c>repo_ids</c> up to the identity the registry
    /// states, and answers how many entries changed.
    /// <para>
    /// A host calls this before it first reads the list, and again after the
    /// workspace root moves. It is on the port rather than left to a screen
    /// because what a stored repository value means is the module's rule, and
    /// because the alias-shaped values it settles were written by this module in
    /// the first place.
    /// </para>
    /// <para>
    /// Safe to call on every start: the pass is idempotent by construction, so a
    /// second run over a reconciled workspace is a pure read and needs no
    /// once-flag. That also makes it immune to ordering — a run against a
    /// registry that has not been reloaded yet settles fewer values and leaves the
    /// rest for the next run, which is a performance question rather than a
    /// correctness one.
    /// </para></summary>
    Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-points every entry that names <paramref name="oldId"/> — in its
    /// repository assignments or in an issue link — at <paramref name="newId"/>,
    /// and answers how many entries moved.
    /// <para>
    /// The other half of a repository rename. Settings carries the registry row
    /// across when a line keeps its alias and changes its <c>owner/name</c>; the
    /// entries that filed themselves against the old coordinate live here, and
    /// without this call <see cref="ReconcileRepositoryIdsAsync"/> would find the
    /// old id on the next start and register it back as a directory-less
    /// repository. Idempotent for the same reason that pass is.
    /// </para>
    /// </summary>
    Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default);

    /// <summary>Brings in a plan — a block of entry text naming more than one
    /// prompt — and turns it into backlog entries in one step. See
    /// <c>ImportPlanCommand</c> and ADR 0007 for what "brings in" means: the
    /// same grammar and repository port every other entry goes through, with
    /// two-pass dependency resolution and upsert-by-plan on top.
    /// <para>
    /// <paramref name="defaultRepo"/> is the Import dialog's optional "Target
    /// repository" field. It is applied only to an entry whose own text names
    /// no <c>repo:</c> — an entry that already specifies one keeps it.
    /// </para>
    /// <para>
    /// <paramref name="repoMatches"/> is what the reader said in the dialog about
    /// the repository names the plan mentions: the name as written, mapped to the
    /// alias they meant. Names they did not match are resolved against the
    /// registry, and registered there when it has never seen them.
    /// </para>
    /// <para>
    /// <paramref name="sourceInboxId"/> is stamped on the entries the import
    /// creates, and on nothing it updates or skips, for the reason
    /// <see cref="SaveFromTextAsync"/> gives: the field is birth provenance.
    /// </para></summary>
    Task<Result<ImportPlanResultDto>> ImportPlanAsync(
        string rawText,
        string? defaultRepo = null,
        IReadOnlyDictionary<string, string>? repoMatches = null,
        string? sourceInboxId = null,
        bool layOutOnRoadmap = false,
        CancellationToken cancellationToken = default);
}
