using Backlog.Infrastructure.FileSystem.Inbox;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The backlog's open tasks as a batch's dependency proposal may see them. What
/// these pin is the adapter's two translations — a projection into the link
/// GitHub gives it, the <c>Source:</c> line into a link — and the one filter:
/// done and archived work is never something to wait on.
/// </summary>
public sealed class InboxTaskReferencesTests
{
    [Fact]
    public async Task An_open_task_is_known_by_its_issue_its_pull_requests_and_its_source_link()
    {
        var id = Guid.NewGuid();
        var entry = Entry("Ship the parser", EntryStatus.InProgress, id) with
        {
            Body = "Notes.\n\nSource: https://example.com/spec\n",
            ImportItemId = "0199a3f2-7c41-7d1a-9d0f-000000000002",
            RepoIds = ["JSdotNet/Backlog"],
            Projections =
            [
                new EntryProjectionDto("JSdotNet/Backlog", "12", EntryProjectionDto.IssueTargetType),
                new EntryProjectionDto("JSdotNet/Backlog", "40", EntryProjectionDto.PullRequestTargetType),
                new EntryProjectionDto("JSdotNet/Backlog", "session-1", EntryProjectionDto.SessionTargetType),
            ],
        };

        var reference = Assert.Single(await Read(entry));

        Assert.Equal(id, reference.Id);
        Assert.Equal("0199a3f2-7c41-7d1a-9d0f-000000000002", reference.ImportItemId);
        Assert.Equal("Ship the parser", reference.Title);
        Assert.Equal(["JSdotNet/Backlog"], reference.RepoIds);
        Assert.Equal([new InboxIssueReferenceDto("JSdotNet/Backlog", 12)], reference.Issues);
        Assert.Equal(
            ["https://github.com/JSdotNet/Backlog/issues/12", "https://github.com/JSdotNet/Backlog/pull/40", "https://example.com/spec"],
            reference.Links);
    }

    [Fact]
    public async Task Done_and_archived_tasks_are_left_out()
    {
        var references = await Read(
            Entry("Draft", EntryStatus.Draft),
            Entry("Ready", EntryStatus.Ready),
            Entry("Done", EntryStatus.Done),
            Entry("Archived", EntryStatus.Archived));

        Assert.Equal(["Draft", "Ready"], references.Select(reference => reference.Title));
    }

    [Fact]
    public async Task A_projection_that_is_not_a_github_number_makes_no_link()
    {
        var entry = Entry("Odd", EntryStatus.Ready) with
        {
            Projections =
            [
                new EntryProjectionDto("not-a-repository", "12", EntryProjectionDto.IssueTargetType),
                new EntryProjectionDto("a/one", "twelve", EntryProjectionDto.IssueTargetType),
            ],
        };

        var reference = Assert.Single(await Read(entry));

        Assert.Empty(reference.Issues);
        Assert.Empty(reference.Links);
        Assert.Null(reference.ImportItemId);
    }

    [Fact]
    public async Task Every_task_but_an_archived_one_is_read_for_relations_saying_whether_it_is_open()
    {
        var references = await new InboxTaskReferences(new ListingTaskItems(
            [
                Entry("Ready", EntryStatus.Ready),
                Entry("Done", EntryStatus.Done),
                Entry("Archived", EntryStatus.Archived),
            ])).AllTasksAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Ready", "Done"], references.Select(reference => reference.Title));
        Assert.Equal([true, false], references.Select(reference => reference.IsOpen));
    }

    [Fact]
    public async Task A_task_carries_the_inbox_item_it_was_routed_from_and_its_source_link()
    {
        var item = Guid.NewGuid();
        var entry = Entry("Ship the parser", EntryStatus.Ready) with
        {
            Body = "Notes.\n\nSource: https://example.com/spec\n",
            SourceInboxId = item.ToString("D"),
        };

        var reference = Assert.Single(await new InboxTaskReferences(new ListingTaskItems([entry]))
            .AllTasksAsync(TestContext.Current.CancellationToken));

        Assert.Equal(item, reference.SourceInboxId);
        Assert.Equal("https://example.com/spec", reference.SourceUrl);
        Assert.True(reference.IsOpen);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("desktop")]
    public async Task A_source_that_is_not_an_inbox_items_id_names_no_item(string? source)
    {
        var entry = Entry("Ack", EntryStatus.Ready) with { SourceInboxId = source };

        var reference = Assert.Single(await Read(entry));

        Assert.Null(reference.SourceInboxId);
        Assert.Null(reference.SourceUrl);
    }

    private static Task<IReadOnlyList<InboxTaskReferenceDto>> Read(params TaskItemDto[] entries) =>
        new InboxTaskReferences(new ListingTaskItems(entries)).OpenTasksAsync(TestContext.Current.CancellationToken);

    private static TaskItemDto Entry(string title, EntryStatus status, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), title, string.Empty, EntryType.Task, Priority.Medium, status, null, [], 0, 0, 0, []);

    /// <summary>The Tasks facade with a fixed list and nothing else: the
    /// adapter only ever reads.</summary>
    private sealed class ListingTaskItems(TaskItemDto[] entries) : ITaskItems
    {
        public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItemDto>>(entries);

        public Task<Result<SavedTaskDto>> SaveFromTextAsync(Guid? id, string rawText, int order, string? sourceInboxId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<ImportPlanResultDto>> ImportPlanAsync(string rawText, string? defaultRepo = null, IReadOnlyDictionary<string, string>? repoMatches = null, string? sourceInboxId = null, IReadOnlyDictionary<string, string>? sourceInboxIds = null, bool layOutOnRoadmap = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<TaskItemDto>> LinkToIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<TaskItemDto>> SetDevbookReferencesAsync(Guid id, IReadOnlyList<string> references, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
