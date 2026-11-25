using Backlog.Infrastructure.Sqlite.Changes;
using Backlog.Infrastructure.Sqlite.Events;
using Backlog.Infrastructure.Sqlite.Inbox;
using Backlog.Modules.Events.DomainModels;
using Backlog.Modules.Inbox;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.RouteToBacklog;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.SharedKernel.Changes;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The inbox items and the knowledge events in the change journal (local ADR 0061 §2):
/// their triggers write the revisions, a triage decision is one change set over the item
/// and the task it makes, and a restore takes each back through its own repository.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteChangeRestoreInboxAndEventsTests : IDisposable
{
    private static readonly IReadOnlySet<DocumentKey> NoOverrides = new HashSet<DocumentKey>();
    private static readonly DateTimeOffset Noon = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _temp = new("backlog-change-restore-inbox");
    private readonly SqliteTaskRepository _tasks;
    private readonly SqliteInboxRepository _inbox;
    private readonly SqliteKnowledgeEventRepository _events;
    private readonly SqliteChangeRestore _restore;
    private readonly SqliteChangeJournal _journal;

    public SqliteChangeRestoreInboxAndEventsTests()
    {
        _tasks = new SqliteTaskRepository(_temp.Path);
        _inbox = new SqliteInboxRepository(_temp.Path);
        _events = new SqliteKnowledgeEventRepository(_temp.Path);
        _restore = new SqliteChangeRestore(_temp.Path, _tasks);
        _journal = new SqliteChangeJournal(_tasks.DatabasePath);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_triage_is_one_change_set_and_its_restore_reopens_the_item_and_tombstones_the_task()
    {
        var item = await CaptureAsync("Call the plumber");
        var router = new RouteToBacklogCommandHandler(_inbox, new TaskTarget(_tasks), TimeProvider.System);

        // No change set current: the person's gesture in the Inbox pane.
        var routed = await router.Handle(new RouteToBacklogCommand(item.Id), Token);
        var taskId = Assert.Single(Assert.IsType<Ok<InboxRoutedDto>>(routed.Value).Value.TaskIds);

        var triage = Assert.Single(await _journal.ListAsync(cancellationToken: Token), entry => entry.ChangeSet.Label == "Triage of Call the plumber");
        Assert.Equal(ChangeActor.Person, triage.ChangeSet.Actor);
        var documents = await _journal.ReadAsync(triage.ChangeSet.Id, Token);
        Assert.Equal(["inbox-item", "task"], documents.Select(document => document.Kind).Order());

        var plan = Assert.IsType<RestorePlan>(await _restore.PlanAsync(triage.ChangeSet.Id, Token));
        Assert.Equal(RestoreAction.Revert, Assert.Single(plan.Steps, step => step.Key.Kind == "inbox-item").Action);
        Assert.Equal(RestoreAction.Tombstone, Assert.Single(plan.Steps, step => step.Key.Kind == "task").Action);
        Assert.Equal(0, plan.ConflictCount);

        var outcome = await _restore.RestoreAsync(triage.ChangeSet.Id, NoOverrides, Token);

        Assert.Equal(2, outcome.Restored);
        var reopened = Assert.IsType<InboxItem>(await _inbox.GetAsync(item.Id, Token));
        Assert.Equal(InboxStatus.Unprocessed, reopened.Status);
        Assert.False(reopened.IsRouted);
        Assert.NotNull(reopened.RestoredAt);
        Assert.Null(await _tasks.GetAsync(taskId, Token));
        Assert.NotNull((await StoredTaskAsync(taskId)).DeletedAt);
    }

    [Fact]
    public async Task A_triage_inside_an_agents_change_set_joins_it()
    {
        var item = await CaptureAsync("Renew the passport");
        var router = new RouteToBacklogCommandHandler(_inbox, new TaskTarget(_tasks), TimeProvider.System);

        var agent = await AgentAsync("route_inbox_item on Renew the passport", async () =>
            await router.Handle(new RouteToBacklogCommand(item.Id), Token));

        var documents = await _journal.ReadAsync(agent.Id, Token);
        Assert.Equal(["inbox-item", "task"], documents.Select(document => document.Kind).Order());
        Assert.DoesNotContain(await _journal.ListAsync(cancellationToken: Token), entry => entry.ChangeSet.Label == "Triage of Renew the passport");
    }

    [Fact]
    public async Task An_archive_restored_puts_the_item_back_in_the_inbox()
    {
        var item = await CaptureAsync("Old newsletter");
        var agent = await AgentAsync("archive_inbox_item", async () =>
        {
            var held = (await _inbox.GetAsync(item.Id, Token))!;
            held.Archive(Noon.AddHours(1));
            await _inbox.SaveAsync(held, Token);
        });

        var outcome = await _restore.RestoreAsync(agent.Id, NoOverrides, Token);

        Assert.Equal(1, outcome.Restored);
        Assert.Equal(InboxStatus.Unprocessed, (await _inbox.GetAsync(item.Id, Token))!.Status);
    }

    [Fact]
    public async Task An_inbox_item_edited_since_is_a_conflict_kept_as_it_is()
    {
        var item = await CaptureAsync("Book the dentist");
        var agent = await AgentAsync("defer_inbox_item", async () =>
        {
            var held = (await _inbox.GetAsync(item.Id, Token))!;
            held.Defer(new DateOnly(2026, 12, 1), Noon.AddHours(1));
            await _inbox.SaveAsync(held, Token);
        });

        using (ChangeContext.Begin(ChangeActor.Person, "moving"))
        {
            var held = (await _inbox.GetAsync(item.Id, Token))!;
            held.Defer(new DateOnly(2026, 12, 9), DateTimeOffset.UtcNow);
            await _inbox.SaveAsync(held, Token);
        }

        Assert.True(Assert.Single((await _restore.PlanAsync(agent.Id, Token))!.Steps).Conflict);

        var outcome = await _restore.RestoreAsync(agent.Id, NoOverrides, Token);

        Assert.Equal(0, outcome.Restored);
        Assert.Equal(new DateOnly(2026, 12, 9), (await _inbox.GetAsync(item.Id, Token))!.DeferredUntil);
    }

    [Fact]
    public async Task An_inbox_save_no_entry_point_named_is_recorded_under_an_unknown_writer()
    {
        var item = InboxItem.Capture("From another process", new InboxSource("web", null), null, ContentKind.Text, Noon, Noon, replicaBacked: false);
        await _inbox.SaveAsync(item, Token);

        var unknown = Assert.Single(await _journal.ListAsync(cancellationToken: Token));
        Assert.Equal(ChangeActor.Unknown, unknown.ChangeSet.Actor);
        var document = Assert.Single(await _journal.ReadAsync(unknown.ChangeSet.Id, Token));
        Assert.Equal("inbox-item", document.Kind);
        Assert.Null(document.Before);
    }

    [Fact]
    public async Task Restoring_an_add_knowledge_event_change_set_tombstones_the_event()
    {
        var added = Assert.IsType<Ok<KnowledgeEvent>>(KnowledgeEvent.Create(Guid.CreateVersion7(), "DotNext 2026").Value).Value;
        var agent = await AgentAsync("add_knowledge_event", async () => await _events.SaveAsync(added, Token));

        var step = Assert.Single((await _restore.PlanAsync(agent.Id, Token))!.Steps);
        Assert.Equal(new DocumentKey("knowledge-event", added.Id.ToString()), step.Key);
        Assert.Equal(RestoreAction.Tombstone, step.Action);
        Assert.Equal("DotNext 2026", step.Title);

        var outcome = await _restore.RestoreAsync(agent.Id, NoOverrides, Token);

        Assert.Equal(1, outcome.Restored);
        Assert.Null(await _events.GetAsync(added.Id, Token));
        Assert.True(await _events.IsRemovedAsync(added.Id, Token), "The removal is a tombstone, so it reaches the other devices.");
    }

    [Fact]
    public async Task An_edit_in_the_events_pane_restored_brings_the_prior_event_back_stamped_later()
    {
        var added = Assert.IsType<Ok<KnowledgeEvent>>(KnowledgeEvent.Create(Guid.CreateVersion7(), "Build 2026").Value).Value;
        using (ChangeContext.Begin(ChangeActor.Person, "adding"))
        {
            await _events.SaveAsync(added, Token);
        }

        var agent = await AgentAsync("update_knowledge_event", async () =>
        {
            var held = (await _events.GetAsync(added.Id, Token))!;
            held.Rename("Build 2026 (renamed)");
            await _events.SaveAsync(held, Token);
        });
        var renamedStamp = Assert.Single(await _events.ListChangedSinceAsync(DateTimeOffset.MinValue, Token)).UpdatedAt;

        var outcome = await _restore.RestoreAsync(agent.Id, NoOverrides, Token);

        Assert.Equal(1, outcome.Restored);
        Assert.Equal("Build 2026", (await _events.GetAsync(added.Id, Token))!.Title);
        Assert.True(Assert.Single(await _events.ListChangedSinceAsync(DateTimeOffset.MinValue, Token)).UpdatedAt > renamedStamp);
    }

    private async Task<InboxItem> CaptureAsync(string title)
    {
        var item = InboxItem.Capture(title, new InboxSource("web", null), null, ContentKind.Text, Noon, Noon, replicaBacked: false);
        using (ChangeContext.Begin(ChangeActor.Person, "capturing"))
        {
            await _inbox.SaveAsync(item, Token);
        }

        return item;
    }

    private static async Task<ChangeSet> AgentAsync(string label, Func<Task> writes)
    {
        var agent = ChangeSet.Start(ChangeActor.Agent, label) with { Caller = "claude-code", SessionId = "session-1" };
        using (ChangeContext.Enter(agent))
        {
            await writes();
        }

        return agent;
    }

    private async Task<TaskItem> StoredTaskAsync(Guid id)
    {
        await using var connection = await SqliteTaskRepository.OpenForJournalAsync(_tasks.DatabasePath, Token);
        return await SqliteTaskRepository.ReadCurrentAsync(connection, id.ToString(), Token)
            ?? throw new InvalidOperationException($"Task {id} has no row.");
    }

    public void Dispose() => _temp.Dispose();

    /// <summary>Tasks' side of a route, as far as these tests need it: one task per
    /// route, saved in the same file.</summary>
    private sealed class TaskTarget(SqliteTaskRepository tasks) : IInboxBacklogTarget
    {
        public async Task<Result<IReadOnlyList<Guid>>> CreateTasksAsync(InboxRouteRequestDto request, CancellationToken cancellationToken = default)
        {
            var task = new TaskItem(request.Title, request.BodyMd, EntryType.Task);
            await tasks.SaveAsync(task, cancellationToken);
            return Result.Success<IReadOnlyList<Guid>>([task.Id]);
        }

        public Task<InboxBatchTargetResultDto> CreateBatchTasksAsync(InboxBatchRouteRequestDto request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<IReadOnlyList<Guid>>> ImportPlanAsync(string planMarkdown, Guid sourceInboxId, IReadOnlyList<string> allowedRepoIds, string? attachmentPath = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Result<IReadOnlyList<InboxBatchEdge>> ReadDraftedOrder(string planMarkdown, IReadOnlyCollection<Guid> itemIds, IReadOnlyList<string> allowedRepoIds) =>
            throw new NotSupportedException();

        public Task<Result> CommentOnTaskAsync(InboxMergeRequestDto request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result> DeleteRoutedTasksAsync(IReadOnlyList<Guid> taskIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
