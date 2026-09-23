using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The backlog, as the one read the port offers.
/// <para>
/// Every write throws. That is not laziness about a method nobody calls — it is
/// the assertion: a read-only tool that reached for <c>SaveFromTextAsync</c> or
/// <c>ImportPlanAsync</c> would fail here rather than quietly pass, and the whole
/// of local ADR 0012's read-only claim on this side is that it does not.
/// </para>
/// </summary>
internal sealed class FakeTaskItems(params TaskItemDto[] entries) : ITaskItems
{
    /// <summary>How many times the list was asked for. A tool that reads the
    /// whole backlog twice to answer one question is a tool worth noticing.</summary>
    public int Reads { get; private set; }

    public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reads++;

        return Task.FromResult<IReadOnlyList<TaskItemDto>>([.. entries]);
    }

    public Task<Result<SavedTaskDto>> SaveFromTextAsync(
        Guid? id,
        string rawText,
        int order,
        string? sourceInboxId = null,
        CancellationToken cancellationToken = default) => throw Written(nameof(SaveFromTextAsync));

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw Written(nameof(DeleteAsync));

    public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) =>
        throw Written(nameof(ReorderAsync));

    public Task<Result<TaskItemDto>> LinkToIssueAsync(
        Guid id,
        string repoId,
        string externalId,
        string targetType,
        CancellationToken cancellationToken = default) => throw Written(nameof(LinkToIssueAsync));

    public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default) =>
        throw Written(nameof(RecordUsageAsync));

    public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) =>
        throw Written(nameof(ReconcileRepositoryIdsAsync));

    public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) =>
        throw Written(nameof(RenameRepositoryAsync));

    public Task<Result<ImportPlanResultDto>> ImportPlanAsync(
        string rawText,
        string? defaultRepo = null,
        IReadOnlyDictionary<string, string>? repoMatches = null,
        string? sourceInboxId = null,
        CancellationToken cancellationToken = default) => throw Written(nameof(ImportPlanAsync));

    private static InvalidOperationException Written(string member) =>
        new($"A read-only MCP tool called ITaskItems.{member}.");
}

/// <summary>
/// Entries with the fields these tests are about and defaults for the rest.
/// <para>
/// A builder rather than twenty-three positional arguments at each call site:
/// the DTO carries an attachment, a recurrence rule and a reminder, and a test
/// about <c>repo_ids</c> should not have to say anything about any of them.
/// </para>
/// </summary>
internal static class Entries
{
    internal static TaskItemDto Entry(
        string title,
        int order = 0,
        IReadOnlyList<string>? repoIds = null,
        string? importPlanId = null,
        string? importItemId = null,
        EntryStatus status = EntryStatus.Ready,
        Priority priority = Priority.Medium,
        Guid? id = null) =>
        new(
            id ?? Guid.NewGuid(),
            title,
            Body: string.Empty,
            EntryType.Task,
            priority,
            status,
            Area: null,
            Tags: [],
            order,
            TotalSubItems: 0,
            CompletedSubItems: 0,
            Projections: [],
            RepoIds: repoIds,
            ImportPlanId: importPlanId,
            ImportItemId: importItemId);
}
