using Backlog.Infrastructure.FileSystem.Inbox;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The backlog's tag vocabulary as the Inbox's picker may offer it. What these
/// pin is the one translation the adapter makes: a backlog entry's stored tags
/// carry a sigil for a person or a roadmap item and none for a general tag, and
/// only the general ones are words the Inbox can write back on to an entry.
/// </summary>
public sealed class InboxBacklogTagSourceTests
{
    [Fact]
    public async Task Offers_the_general_tags_every_entry_uses_once_each_in_first_seen_order()
    {
        var tasks = new ListingTaskItems(
            Entry("First", ["infra", "Deploy"]),
            Entry("Second", ["deploy", "sync"]),
            Entry("Third", []));

        var tags = await new InboxBacklogTagSource(tasks).TagsInUseAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["infra", "Deploy", "sync"], tags);
    }

    [Fact]
    public async Task Leaves_out_people_and_roadmap_items()
    {
        var tasks = new ListingTaskItems(Entry("One", ["@bob", "+release-q4", "infra"]));

        var tags = await new InboxBacklogTagSource(tasks).TagsInUseAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["infra"], tags);
    }

    [Fact]
    public async Task An_empty_backlog_offers_nothing()
    {
        var tags = await new InboxBacklogTagSource(new ListingTaskItems()).TagsInUseAsync(TestContext.Current.CancellationToken);

        Assert.Empty(tags);
    }

    private static TaskItemDto Entry(string title, IReadOnlyList<string> tags) =>
        new(Guid.NewGuid(), title, string.Empty, EntryType.Task, Priority.Medium, EntryStatus.Draft, null, tags, 0, 0, 0, []);

    /// <summary>The Tasks facade with a fixed list and nothing else: the
    /// adapter only ever reads.</summary>
    private sealed class ListingTaskItems(params TaskItemDto[] entries) : ITaskItems
    {
        public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItemDto>>(entries);

        public Task<Result<SavedTaskDto>> SaveFromTextAsync(Guid? id, string rawText, int order, string? sourceInboxId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<ImportPlanResultDto>> ImportPlanAsync(string rawText, string? defaultRepo = null, IReadOnlyDictionary<string, string>? repoMatches = null, string? sourceInboxId = null, bool layOutOnRoadmap = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<TaskItemDto>> LinkToIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
