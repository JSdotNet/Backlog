using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The pane's own use cases mark each write as the pane's, so the signal it raises is not
/// announced back as written elsewhere. A write the wrapper does not forward falls to the
/// port's default refusal, so every write the port gains is forwarded here too.
/// </summary>
public sealed class SelfAttributedTaskItemsTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);

    [Fact]
    public async Task Setting_the_due_date_is_forwarded_and_marked_as_the_panes_write()
    {
        var mark = new AsyncLocal<bool>();
        var inner = new DueRecordingTaskItems(mark);
        ITaskItems items = new SelfAttributedTaskItems(inner, mark);
        var id = Guid.NewGuid();

        var result = await items.SetDueAsync(id, Monday, TestContext.Current.CancellationToken);

        Assert.IsNotType<Error>(result.Value);
        Assert.Equal((id, (DateOnly?)Monday, true), inner.Asked);
    }

    /// <summary>The version and a write pinned to it reach the inner port, marked as the
    /// pane's - not the port's defaults, which would read the version as null and refuse
    /// the write as stale.</summary>
    [Fact]
    public async Task A_pinned_save_and_its_version_are_forwarded_and_marked_as_the_panes_write()
    {
        var mark = new AsyncLocal<bool>();
        var inner = new DueRecordingTaskItems(mark);
        ITaskItems items = new SelfAttributedTaskItems(inner, mark);
        var id = Guid.NewGuid();
        var token = TestContext.Current.CancellationToken;

        var version = await items.GetUpdatedAtAsync(id, token);
        var saved = await items.SaveFromTextAsync(id, "# Refactor sync", 0, version!.Value, token);

        Assert.IsNotType<Error>(saved.Value);
        Assert.Equal((id, DueRecordingTaskItems.Version, true), inner.Pinned);
    }

    /// <summary>Records what the due-date write and a pinned save were asked for and whether
    /// the pane's mark was set while each ran. Every other member is out of these tests' reach.</summary>
    // Differs from RecordingTaskItems: records the due-date write and a pinned save and whether the pane's mark was set during each; every other member throws.
    private sealed class DueRecordingTaskItems(AsyncLocal<bool> mark) : ITaskItems
    {
        public (Guid Id, DateOnly? DueOn, bool Marked)? Asked { get; private set; }

        public static readonly DateTimeOffset Version = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

        public (Guid Id, DateTimeOffset Expected, bool Marked)? Pinned { get; private set; }

        public Task<DateTimeOffset?> GetUpdatedAtAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<DateTimeOffset?>(Version);

        public Task<Result<SavedTaskDto>> SaveFromTextAsync(Guid id, string rawText, int order, DateTimeOffset expectedUpdatedAt, CancellationToken cancellationToken = default)
        {
            Pinned = (id, expectedUpdatedAt, mark.Value);
            return Task.FromResult<Result<SavedTaskDto>>(new SavedTaskDto(new TaskItemDto(
                id, "Refactor sync", string.Empty, EntryType.Task, Priority.Medium, EntryStatus.Ready, null, [], 0, 0, 0, [])));
        }

        public Task<Result<TaskItemDto>> SetDueAsync(Guid id, DateOnly? dueOn, CancellationToken cancellationToken = default)
        {
            Asked = (id, dueOn, mark.Value);
            return Task.FromResult<Result<TaskItemDto>>(new TaskItemDto(
                id, "Refactor sync", string.Empty, EntryType.Task, Priority.Medium, EntryStatus.Ready, null, [], 0, 0, 0, [], DueOn: dueOn));
        }

        public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<SavedTaskDto>> SaveFromTextAsync(Guid? id, string rawText, int order, string? sourceInboxId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<TaskItemDto>> LinkToIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<TaskItemDto>> UnlinkFromIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<TaskItemDto>> SetDevbookReferencesAsync(Guid id, IReadOnlyList<string> references, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> RefileRepositoryTasksAsync(string repositoryId, string? moveToId, bool drop, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<ImportPlanResultDto>> ImportPlanAsync(
            string rawText,
            string? defaultRepo = null,
            IReadOnlyDictionary<string, string>? repoMatches = null,
            string? sourceInboxId = null,
            IReadOnlyDictionary<string, string>? sourceInboxIds = null,
            bool layOutOnRoadmap = false,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
