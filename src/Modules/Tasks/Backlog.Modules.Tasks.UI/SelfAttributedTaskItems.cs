using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Changes;
using Backlog.SharedKernel.Results;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The use cases <see cref="TasksDesktopState"/> writes through, marking every
/// write it makes as its own.
/// <para>
/// The list hears every task write on the machine through
/// <see cref="ITaskChangeSignal"/>, its own included, and has to tell the two
/// apart: a write made somewhere else is a reason to reload, and its own is not —
/// it already holds the row it just saved. The signal carries nothing that could
/// say who wrote, deliberately, and <see cref="ITaskChangeSignal.Suppress"/> is
/// not the answer either, because it would also silence the sync worker, which
/// has to hear the list's writes to push them within seconds.
/// </para>
/// <para>
/// So the mark is asynchronous-local, the same device the signal's suppression
/// uses: each write sets <see cref="_writingHere"/> inside its own asynchronous
/// method, the repository raises the signal inside that same flow, and the
/// list's handler finds the mark set. A value set in an async method does not
/// flow back out to its caller, so the mark ends with the write; and the MCP
/// server writing through the same singleton on a request thread at the same
/// moment runs in a flow that never had it set.
/// </para>
/// <para>
/// Each write is also the person's change set of the change journal (local ADR
/// 0061 §2), on the same asynchronous flow, unless the gesture that made it opened
/// one already — a group of undo steps, a typing session — which it then joins.
/// <see cref="RecordUsageAsync"/> opens none: noting that an entry was used changes
/// nothing a person would take back, and <see cref="ReconcileRepositoryIdsAsync"/>
/// is housekeeping rather than a gesture.
/// </para>
/// </summary>
internal sealed class SelfAttributedTaskItems(ITaskItems inner, AsyncLocal<bool> writingHere) : ITaskItems
{
    private readonly AsyncLocal<bool> _writingHere = writingHere;

    public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default) =>
        inner.ListAsync(cancellationToken);

    // Reads, so nothing to attribute - forwarded rather than left to the port's
    // defaults, which would read the whole list for one entry or a count.
    public Task<TaskItemDto?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        inner.GetAsync(id, cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        inner.CountAsync(cancellationToken);

    // Forwarded too: the port's default answers null, which would refuse every
    // versioned write below as stale.
    public Task<DateTimeOffset?> GetUpdatedAtAsync(Guid id, CancellationToken cancellationToken = default) =>
        inner.GetUpdatedAtAsync(id, cancellationToken);

    // The versioned writes are forwarded rather than left to the port's defaults,
    // which compare first and write after: the inner port checks and writes in one
    // conditional save.
    public async Task<Result<SavedTaskDto>> SaveFromTextAsync(
        Guid id,
        string rawText,
        int order,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        using var change = ChangeContext.BeginIfNone(ChangeActor.Person, "saving an entry");
        return await inner.SaveFromTextAsync(id, rawText, order, expectedUpdatedAt, cancellationToken);
    }

    public async Task<Result<TaskItemDto>> LinkToIssueAsync(
        Guid id,
        string repoId,
        string externalId,
        string targetType,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        using var change = ChangeContext.BeginIfNone(ChangeActor.Person, "linking an entry");
        return await inner.LinkToIssueAsync(id, repoId, externalId, targetType, expectedUpdatedAt, cancellationToken);
    }

    public async Task<Result<TaskItemDto>> SetDevbookReferencesAsync(
        Guid id,
        IReadOnlyList<string> references,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        using var change = ChangeContext.BeginIfNone(ChangeActor.Person, "setting Devbook references");
        return await inner.SetDevbookReferencesAsync(id, references, expectedUpdatedAt, cancellationToken);
    }

    public async Task<Result<SavedTaskDto>> SaveFromTextAsync(
        Guid? id,
        string rawText,
        int order,
        string? sourceInboxId = null,
        CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        using var change = ChangeContext.BeginIfNone(ChangeActor.Person, "saving an entry");
        return await inner.SaveFromTextAsync(id, rawText, order, sourceInboxId, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        using var change = ChangeContext.BeginIfNone(ChangeActor.Person, "deleting an entry");
        await inner.DeleteAsync(id, cancellationToken);
    }

    public async Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        using var change = ChangeContext.BeginIfNone(ChangeActor.Person, "reordering");
        await inner.ReorderAsync(idsInOrder, cancellationToken);
    }

    public async Task<Result<TaskItemDto>> LinkToIssueAsync(
        Guid id,
        string repoId,
        string externalId,
        string targetType,
        CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        using var change = ChangeContext.BeginIfNone(ChangeActor.Person, "linking an entry");
        return await inner.LinkToIssueAsync(id, repoId, externalId, targetType, cancellationToken);
    }

    public async Task<Result<TaskItemDto>> UnlinkFromIssueAsync(
        Guid id,
        string repoId,
        string externalId,
        string targetType,
        CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        using var change = ChangeContext.BeginIfNone(ChangeActor.Person, "unlinking an entry");
        return await inner.UnlinkFromIssueAsync(id, repoId, externalId, targetType, cancellationToken);
    }

    public async Task<Result<TaskItemDto>> SetDevbookReferencesAsync(
        Guid id,
        IReadOnlyList<string> references,
        CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        using var change = ChangeContext.BeginIfNone(ChangeActor.Person, "setting Devbook references");
        return await inner.SetDevbookReferencesAsync(id, references, cancellationToken);
    }

    public async Task<Result<TaskItemDto>> SetPlannedHoursAsync(
        Guid id,
        IReadOnlyList<PlannedHoursDto> blocks,
        CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        using var change = ChangeContext.BeginIfNone(ChangeActor.Person, "planning hours");
        return await inner.SetPlannedHoursAsync(id, blocks, cancellationToken);
    }

    public async Task<Result<TaskItemDto>> SetDueAsync(
        Guid id,
        DateOnly? dueOn,
        CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        using var change = ChangeContext.BeginIfNone(ChangeActor.Person, "setting a due date");
        return await inner.SetDueAsync(id, dueOn, cancellationToken);
    }

    public async Task<Result<bool>> CreateFromTextIfAbsentAsync(
        Guid id,
        string rawText,
        int order,
        CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        using var change = ChangeContext.BeginIfNone(ChangeActor.Person, "adding an entry");
        return await inner.CreateFromTextIfAbsentAsync(id, rawText, order, cancellationToken);
    }

    public async Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        await inner.RecordUsageAsync(id, action, cancellationToken);
    }

    public async Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        return await inner.ReconcileRepositoryIdsAsync(cancellationToken);
    }

    public async Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        using var change = ChangeContext.BeginIfNone(ChangeActor.Person, "renaming a repository");
        return await inner.RenameRepositoryAsync(oldId, newId, cancellationToken);
    }

    public async Task<Result<int>> RefileRepositoryTasksAsync(string repositoryId, string? moveToId, bool drop, CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        using var change = ChangeContext.BeginIfNone(ChangeActor.Person, "refiling a repository's entries");
        return await inner.RefileRepositoryTasksAsync(repositoryId, moveToId, drop, cancellationToken);
    }

    public async Task<Result<ImportPlanResultDto>> ImportPlanAsync(
        string rawText,
        string? defaultRepo = null,
        IReadOnlyDictionary<string, string>? repoMatches = null,
        string? sourceInboxId = null,
        IReadOnlyDictionary<string, string>? sourceInboxIds = null,
        bool layOutOnRoadmap = false,
        CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        using var change = ChangeContext.BeginIfNone(ChangeActor.Person, "importing a plan");
        return await inner.ImportPlanAsync(rawText, defaultRepo, repoMatches, sourceInboxId, sourceInboxIds, layOutOnRoadmap, cancellationToken);
    }
}
