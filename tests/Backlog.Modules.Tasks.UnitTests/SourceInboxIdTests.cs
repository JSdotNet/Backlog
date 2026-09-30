using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.ImportPlan;
using Backlog.Modules.Tasks.Features.SaveTaskFromText;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// Provenance from the Inbox. <c>SourceInboxId</c> is the one field on the
/// aggregate that only birth can set, so the two use cases that create entries
/// take it and stamp it on what they create — and on nothing they update, which
/// is what these pin: an id handed to an update or a re-import is not a rename
/// of where an existing entry came from.
/// </summary>
public sealed class SourceInboxIdTests
{
    private const string InboxItem = "0199a3f2-7c41-7d1a-9d0f-3d2c5e6f7a8b";

    [Fact]
    public async Task A_created_entry_carries_the_inbox_item_it_was_routed_from()
    {
        var store = new InMemoryTaskRepository();

        var result = await new SaveTaskFromTextCommandHandler(store, new FakeRepositoryDirectory())
            .Handle(new SaveTaskFromTextCommand(null, "# Ship it\n`task` `!draft`\n", 0, InboxItem), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(InboxItem, store.Entries.Values.Single().SourceInboxId);
    }

    [Fact]
    public async Task A_hand_typed_entry_has_no_source()
    {
        var store = new InMemoryTaskRepository();

        await new SaveTaskFromTextCommandHandler(store, new FakeRepositoryDirectory())
            .Handle(new SaveTaskFromTextCommand(null, "# Ship it\n`task`\n", 0), TestContext.Current.CancellationToken);

        Assert.Null(store.Entries.Values.Single().SourceInboxId);
    }

    [Fact]
    public async Task An_update_does_not_rewrite_where_an_entry_came_from()
    {
        var store = new InMemoryTaskRepository();
        var handler = new SaveTaskFromTextCommandHandler(store, new FakeRepositoryDirectory());

        var created = await handler.Handle(new SaveTaskFromTextCommand(null, "# Ship it\n`task`\n", 0), TestContext.Current.CancellationToken);
        var updated = await handler.Handle(
            new SaveTaskFromTextCommand(created.Value.Entry.Id, "# Ship it now\n`task`\n", 0, InboxItem),
            TestContext.Current.CancellationToken);

        Assert.True(updated.IsSuccess);
        Assert.Equal("Ship it now", store.Entries.Values.Single().Title);
        Assert.Null(store.Entries.Values.Single().SourceInboxId);
    }

    [Fact]
    public async Task An_imported_plan_stamps_the_entries_it_creates_and_not_the_ones_it_keeps()
    {
        var store = new InMemoryTaskRepository();
        var handler = new ImportPlanCommandHandler(store, new FakeRepositoryDirectory());

        // A prompt already under way from an earlier, hand-pasted import: the
        // re-import updates it in place and must not rewrite its provenance.
        const string first = "# First prompt\n`prompt` `#myplan` `id:first`\n";
        await handler.Handle(new ImportPlanCommand(first), TestContext.Current.CancellationToken);
        store.Entries.Values.Single().SetStatus(EntryStatus.InProgress, new DateOnly(2026, 1, 1));

        const string second = first + "\n# Second prompt\n`prompt` `#myplan` `id:second`\n";
        var result = await handler.Handle(new ImportPlanCommand(second, SourceInboxId: InboxItem), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Created);
        Assert.Equal(1, result.Value.Updated);

        var live = store.Entries.Values.Where(entry => entry.DeletedAt is null).ToList();
        Assert.Null(live.Single(entry => entry.Title == "First prompt").SourceInboxId);
        Assert.Equal(InboxItem, live.Single(entry => entry.Title == "Second prompt").SourceInboxId);
    }

    /// <summary>A plan drafted from an inbox item is third-party text a model
    /// wrote about — a captured article, a forwarded mail — and an entry born
    /// Ready is a prompt an agent may pick up. So every entry an inbox-sourced
    /// import creates is born Draft, whatever the text says, and a person
    /// promotes it; the hand-pasted import keeps its Ready default.</summary>
    [Fact]
    public async Task An_imported_plan_from_the_inbox_creates_draft_entries_even_when_the_text_says_ready()
    {
        var store = new InMemoryTaskRepository();
        var handler = new ImportPlanCommandHandler(store, new FakeRepositoryDirectory());

        const string plan =
            "# Says ready\n`prompt` `!ready` `#inbox-plan-1` `id:one`\n\n"
            + "# Says nothing\n`prompt` `#inbox-plan-1` `id:two`\n\n"
            + "# Says in progress\n`prompt` `!in-progress` `#inbox-plan-1` `id:three`\n";

        var result = await handler.Handle(new ImportPlanCommand(plan, SourceInboxId: InboxItem), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Created);
        Assert.All(store.Entries.Values, entry =>
        {
            Assert.Equal(EntryStatus.Draft, entry.Status);
            Assert.Equal(InboxItem, entry.SourceInboxId);
        });
    }

    /// <summary>A batch of inbox items routed together is one document, so the
    /// single source cannot say where each entry came from. The per-entry map
    /// does, keyed on the entry's <c>id:</c>; an entry the map does not name falls
    /// back to the single source, and every sourced entry is born Draft.</summary>
    [Fact]
    public async Task An_imported_batch_stamps_each_entry_with_its_own_inbox_item()
    {
        var store = new InMemoryTaskRepository();
        var handler = new ImportPlanCommandHandler(store, new FakeRepositoryDirectory());

        const string other = "0199a3f2-7c41-7d1a-9d0f-000000000002";
        const string plan =
            "# From the first item\n`task` `!ready` `+inbox-batch-1a2b3c4d` `id:first`\n\n"
            + "# From the second item\n`task` `+inbox-batch-1a2b3c4d` `id:second/owner/repo`\n\n"
            + "# Named by nobody\n`task` `+inbox-batch-1a2b3c4d` `id:third`\n";

        var result = await handler.Handle(
            new ImportPlanCommand(
                plan,
                SourceInboxId: null,
                SourceInboxIds: new Dictionary<string, string> { ["first"] = InboxItem, ["second/owner/repo"] = other }),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var byTitle = store.Entries.Values.ToDictionary(entry => entry.Title);
        Assert.Equal(InboxItem, byTitle["From the first item"].SourceInboxId);
        Assert.Equal(other, byTitle["From the second item"].SourceInboxId);
        Assert.Null(byTitle["Named by nobody"].SourceInboxId);
        Assert.Equal(EntryStatus.Draft, byTitle["From the first item"].Status);
        Assert.Equal(EntryStatus.Draft, byTitle["From the second item"].Status);
        Assert.Equal(EntryStatus.Ready, byTitle["Named by nobody"].Status);
        Assert.Equal("second/owner/repo", byTitle["From the second item"].ImportItemId);
        Assert.All(store.Entries.Values, entry => Assert.Equal("+inbox-batch-1a2b3c4d", entry.ImportPlanId));
    }

    [Fact]
    public async Task An_entry_the_batch_map_does_not_name_keeps_the_single_source()
    {
        var store = new InMemoryTaskRepository();
        var handler = new ImportPlanCommandHandler(store, new FakeRepositoryDirectory());

        const string other = "0199a3f2-7c41-7d1a-9d0f-000000000002";
        const string plan =
            "# Mapped\n`task` `+p-1a2b3c4d` `id:one`\n\n"
            + "# Unmapped\n`task` `+p-1a2b3c4d` `id:two`\n";

        await handler.Handle(
            new ImportPlanCommand(plan, SourceInboxId: InboxItem, SourceInboxIds: new Dictionary<string, string> { ["one"] = other }),
            TestContext.Current.CancellationToken);

        var byTitle = store.Entries.Values.ToDictionary(entry => entry.Title);
        Assert.Equal(other, byTitle["Mapped"].SourceInboxId);
        Assert.Equal(InboxItem, byTitle["Unmapped"].SourceInboxId);
    }

    [Fact]
    public async Task A_hand_pasted_plan_still_defaults_to_ready_and_keeps_what_it_says()
    {
        var store = new InMemoryTaskRepository();
        var handler = new ImportPlanCommandHandler(store, new FakeRepositoryDirectory());

        const string plan =
            "# Says ready\n`prompt` `!ready` `#myplan` `id:one`\n\n"
            + "# Says nothing\n`prompt` `#myplan` `id:two`\n\n"
            + "# Says draft\n`prompt` `!draft` `#myplan` `id:three`\n";

        await handler.Handle(new ImportPlanCommand(plan), TestContext.Current.CancellationToken);

        Assert.Equal(
            [EntryStatus.Ready, EntryStatus.Ready, EntryStatus.Draft],
            store.Entries.Values.OrderBy(entry => entry.Order).Select(entry => entry.Status));
    }

    private sealed class InMemoryTaskRepository : ITaskRepository
    {
        public Dictionary<Guid, TaskItem> Entries { get; } = [];

        public Task SaveAsync(TaskItem entry, CancellationToken cancellationToken = default)
        {
            Entries[entry.Id] = entry;
            return Task.CompletedTask;
        }

        public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.TryGetValue(id, out var entry) && entry.DeletedAt is null ? entry : null);

        public Task<TaskItem?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.TryGetValue(id, out var entry) ? entry : null);

        public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>([.. Entries.Values.Where(entry => entry.DeletedAt is null)]);

        public Task<IReadOnlyList<TaskItem>> ListChangedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>(
                [.. Entries.Values.Where(entry => entry.UpdatedAt > since).OrderBy(entry => entry.UpdatedAt)]);
    }
}
