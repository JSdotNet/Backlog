using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
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
/// </summary>
internal sealed class SelfAttributedTaskItems(ITaskItems inner, AsyncLocal<bool> writingHere) : ITaskItems
{
    private readonly AsyncLocal<bool> _writingHere = writingHere;

    public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default) =>
        inner.ListAsync(cancellationToken);

    public async Task<Result<SavedTaskDto>> SaveFromTextAsync(
        Guid? id,
        string rawText,
        int order,
        string? sourceInboxId = null,
        CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        return await inner.SaveFromTextAsync(id, rawText, order, sourceInboxId, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        await inner.DeleteAsync(id, cancellationToken);
    }

    public async Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
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
        return await inner.LinkToIssueAsync(id, repoId, externalId, targetType, cancellationToken);
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
        return await inner.RenameRepositoryAsync(oldId, newId, cancellationToken);
    }

    public async Task<Result<ImportPlanResultDto>> ImportPlanAsync(
        string rawText,
        string? defaultRepo = null,
        IReadOnlyDictionary<string, string>? repoMatches = null,
        string? sourceInboxId = null,
        bool layOutOnRoadmap = false,
        CancellationToken cancellationToken = default)
    {
        _writingHere.Value = true;
        return await inner.ImportPlanAsync(rawText, defaultRepo, repoMatches, sourceInboxId, layOutOnRoadmap, cancellationToken);
    }
}
