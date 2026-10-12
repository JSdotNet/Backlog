using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.Sqlite.Inbox;
using Backlog.Infrastructure.Sync;
using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Services;
using Backlog.Mobile.UI.TalkNotes;
using Backlog.Modules.Inbox;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Extensions;
using Backlog.Modules.Inbox.Features.ArchiveItem;
using Backlog.Modules.Inbox.Features.RestoreItem;
using Backlog.Modules.Inbox.Features.ReturnToInbox;
using Backlog.SharedKernel.Results;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using TaskChange = Backlog.Modules.Sync.Abstractions.DataTransferObjects.TaskChange;
using TaskPayload = Backlog.Modules.Sync.Abstractions.DataTransferObjects.TaskPayload;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// An undo in the Inbox outlasting the echo of the decision it took back. The
/// desktop archives or routes a capture from the replica, its acknowledgement
/// leaves as a tombstone, and the reader undoes the decision. The replica keeps
/// that tombstone, so a later pull — the same cycle, the next, or a full replay
/// after the cursor is forgotten — delivers it back. The item must stay open.
/// <para>
/// The desktop is the real Inbox module over its own <c>backlog.db</c>, with the
/// real merge and session, as in <see cref="NoteSyncBetweenPhoneAndDesktopTests"/>;
/// a capture is seeded by another device pushing a <c>capture</c> document, and a
/// note by the real phone.
/// </para>
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class UndoOutlastsSyncEchoTests : IDisposable
{
    private static readonly Guid Owner = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset Start = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "undo-echo-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTaskReplica _replica = new();
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly Desktop _desktop;

    public UndoOutlastsSyncEchoTests()
    {
        _desktop = new Desktop(Path.Combine(_root, "desktop"), _replica, _clock);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _desktop.Dispose();
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task A_restored_capture_stays_open_when_its_own_tombstone_comes_back()
    {
        var id = await SeedCaptureAsync("Call the dentist");
        await _desktop.SyncAsync();

        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True((await _desktop.ArchiveAsync(id)).IsSuccess);
        await _desktop.PushAsync();
        Assert.NotNull(_replica.Held(id)!.DeletedAt);

        // The reader undoes before the pull that would have found the item
        // still archived and taken the echo for what it is.
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await _desktop.RestoreAsync(id)).IsSuccess);
        var echo = await _desktop.PullAsync();

        Assert.Equal(0, echo.Applied);
        var item = await _desktop.ItemAsync(id);
        Assert.Equal(InboxStatus.Unprocessed, item!.Status);
        Assert.False(item.ReplicaAckPending);
    }

    /// <summary>A cursor the service retired is forgotten and the feed read from
    /// the start, which delivers every tombstone the replica holds — however
    /// long ago the reader took the decision back.</summary>
    [Fact]
    public async Task A_restored_capture_stays_open_when_the_feed_is_replayed_from_the_start()
    {
        var id = await SeedCaptureAsync("Call the dentist");
        await _desktop.SyncAsync();

        _clock.Advance(TimeSpan.FromMinutes(5));
        await _desktop.ArchiveAsync(id);
        await _desktop.SyncAsync();
        Assert.Equal(InboxStatus.Archived, (await _desktop.ItemAsync(id))!.Status);

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await _desktop.RestoreAsync(id)).IsSuccess);
        await _desktop.SyncAsync();

        _clock.Advance(TimeSpan.FromDays(3));
        _desktop.ForgetCursor();
        await _desktop.PullAsync();

        Assert.Equal(InboxStatus.Unprocessed, (await _desktop.ItemAsync(id))!.Status);
    }

    [Fact]
    public async Task A_capture_returned_from_the_backlog_stays_open_when_its_routed_tombstone_comes_back()
    {
        var id = await SeedCaptureAsync("Call the dentist");
        await _desktop.SyncAsync();

        _clock.Advance(TimeSpan.FromMinutes(5));
        await _desktop.RouteAsync(id);
        await _desktop.PushAsync();
        Assert.NotNull(_replica.Held(id)!.DeletedAt);

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await _desktop.ReturnAsync(id)).IsSuccess);
        await _desktop.SyncAsync();

        var item = await _desktop.ItemAsync(id);
        Assert.Equal(InboxStatus.Unprocessed, item!.Status);
        Assert.False(item.IsRouted);
    }

    /// <summary>A dismissal made after the undo is not the echo, and still
    /// closes the item here.</summary>
    [Fact]
    public async Task A_dismissal_pushed_after_the_restore_still_archives_the_capture()
    {
        var id = await SeedCaptureAsync("Call the dentist");
        await _desktop.SyncAsync();

        _clock.Advance(TimeSpan.FromMinutes(5));
        await _desktop.ArchiveAsync(id);
        await _desktop.PushAsync();
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _desktop.RestoreAsync(id);

        _clock.Advance(TimeSpan.FromMinutes(10));
        await PushFromAnotherDeviceAsync(Capture(id, "Call the dentist", deletedAt: _clock.GetUtcNow()));
        await _desktop.SyncAsync();

        Assert.Equal(InboxStatus.Archived, (await _desktop.ItemAsync(id))!.Status);
    }

    /// <summary>A note already outlasts its echo by its own stamp: the restore
    /// stamps it after the tombstone, the intake ignores a tombstone no later
    /// than that, and the live copy it pushes next brings it back to the phone.</summary>
    [Fact]
    public async Task A_restored_note_stays_open_through_its_echo_and_the_phone_shows_it_again()
    {
        using var phone = new Phone(Path.Combine(_root, "phone"), _replica, _clock);
        var made = await phone.Notes.CreateAsync("Standup", "text", cancellationToken: Cancellation);
        await phone.Outbox.FlushAsync(Cancellation);
        await _desktop.SyncAsync();

        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True((await _desktop.ArchiveAsync(made.Id)).IsSuccess);
        await _desktop.PushAsync();
        await phone.Notes.PullAsync(Cancellation);
        Assert.Empty(phone.Notes.Notes());

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await _desktop.RestoreAsync(made.Id)).IsSuccess);
        await _desktop.PullAsync();
        Assert.Equal(InboxStatus.Unprocessed, (await _desktop.ItemAsync(made.Id))!.Status);

        await _desktop.SyncAsync();
        await phone.Notes.PullAsync(Cancellation);

        Assert.Equal(InboxStatus.Unprocessed, (await _desktop.ItemAsync(made.Id))!.Status);
        Assert.Equal(made.Id, Assert.Single(phone.Notes.Notes()).Id);
    }

    private async Task<Guid> SeedCaptureAsync(string title)
    {
        var id = Guid.CreateVersion7();
        await PushFromAnotherDeviceAsync(Capture(id, title, deletedAt: null));
        return id;
    }

    private async Task PushFromAnotherDeviceAsync(TaskChange change)
    {
        using var http = new HttpClient(_replica.For(Guid.NewGuid())) { BaseAddress = new Uri("https://sync.test") };
        var pushed = await new TaskSyncClient(http).PushAsync([change], Cancellation);
        Assert.True(pushed.IsSuccess);
    }

    /// <summary>A capture document the way the service writes one for the phone.</summary>
    private TaskChange Capture(Guid id, string title, DateTimeOffset? deletedAt)
    {
        var at = deletedAt ?? _clock.GetUtcNow();

        return new TaskChange(id, at, deletedAt, new TaskPayload(
            title,
            ContentMd: string.Empty,
            Type: "capture",
            Status: "draft",
            Priority: "medium",
            Order: 0,
            Area: null,
            CreatedAt: Start,
            SourceInboxId: "mobile",
            RecurrenceSourceId: null,
            DueOn: null,
            RemindAt: null,
            Recurrence: null,
            InMyDayOn: null,
            View: null,
            Effort: null,
            ImportPlanId: null,
            ImportItemId: null,
            AttachmentPath: null,
            Tags: [],
            RepoIds: [],
            DependsOn: [],
            SubItems: [],
            UsageEvents: [],
            ProjectionRefs: []));
    }

    /// <summary>The phone: its note view, outbox and sync client over its own file.</summary>
    private sealed class Phone : IDisposable
    {
        private readonly HttpClient _http;

        public Phone(string root, FakeTaskReplica replica, TimeProvider clock)
        {
            Directory.CreateDirectory(root);
            var database = Path.Combine(root, "backlog-mobile.db");

            _http = new HttpClient(replica.For(Guid.NewGuid())) { BaseAddress = new Uri("https://sync.test") };
            var sync = new CloudSyncClient(_http);

            Outbox = new DeviceOutbox(
                new SqliteDeviceStore(database),
                [new NoteOutboxKind(sync, new TalkNoteFiles(Path.Combine(root, "talk-notes")))],
                clock);
            Notes = new NoteViewProjection(new SqliteNoteViewStore(database), Outbox, sync, clock);
        }

        public DeviceOutbox Outbox { get; }

        public NoteViewProjection Notes { get; }

        public void Dispose()
        {
            Notes.Dispose();
            Outbox.Dispose();
            _http.Dispose();
        }
    }

    /// <summary>The desktop, composed as in <see cref="NoteSyncBetweenPhoneAndDesktopTests"/>,
    /// with the push and the pull reachable apart so a test can put an undo between
    /// them, and the cursor forgettable the way the session forgets a retired one.</summary>
    private sealed class Desktop : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly HttpClient _http;
        private readonly TaskSyncSession _session;
        private readonly SqliteInboxRepository _inbox;
        private readonly FileTaskSyncStateStore _state;
        private readonly TimeProvider _clock;

        public Desktop(string root, FakeTaskReplica replica, TimeProvider clock)
        {
            Directory.CreateDirectory(root);
            _clock = clock;
            _inbox = new SqliteInboxRepository(root);

            var services = new ServiceCollection();
            services.AddSingleton(clock);
            services.AddSingleton<IInboxItemRepository>(_inbox);
            services.AddSingleton<IInboxOrganizerRepository>(_inbox);
            services.AddInboxModule();
            _provider = services.BuildServiceProvider();

            var tasks = new RootedSqliteTaskRepository(() => root);
            var notes = _provider.GetRequiredService<IInboxNoteReplication>();
            var deviceId = Guid.NewGuid();

            _state = new FileTaskSyncStateStore(Path.Combine(root, "sync", "task-sync-state.json"));
            _http = new HttpClient(replica.For(deviceId)) { BaseAddress = new Uri("https://sync.test") };
            _session = new TaskSyncSession(
                new TaskSyncClient(_http),
                new TaskReplicaMerge(tasks, _provider.GetRequiredService<IInboxIntake>(), notes: notes),
                tasks,
                _state,
                new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, deviceId, $"PC {deviceId:N}", "credential")),
                clock,
                _provider.GetRequiredService<IInboxCaptureOutbox>(),
                notes: notes);
        }

        public Task<InboxItem?> ItemAsync(Guid id) => _inbox.GetAsync(id, Cancellation);

        public Task<Result> ArchiveAsync(Guid id) =>
            new ArchiveItemCommandHandler(_inbox, _clock).Handle(new ArchiveItemCommand(id), Cancellation);

        public Task<Result> RestoreAsync(Guid id) =>
            new RestoreItemCommandHandler(_inbox, _clock).Handle(new RestoreItemCommand(id), Cancellation);

        /// <summary>Routes the item as the route handler leaves it: the task made,
        /// the item marked routed with the acknowledgement owed.</summary>
        public async Task RouteAsync(Guid id)
        {
            var item = await _inbox.GetAsync(id, Cancellation);
            item!.RouteToBacklog([Guid.CreateVersion7()], [], _clock.GetUtcNow());
            await _inbox.SaveAsync(item, Cancellation);
        }

        public Task<Result> ReturnAsync(Guid id) =>
            new ReturnToInboxCommandHandler(_inbox, new DeletingBacklog(), _clock)
                .Handle(new ReturnToInboxCommand(id, DeleteTasks: true), Cancellation);

        public async Task PushAsync()
        {
            var pushed = await _session.PushAsync(Cancellation);
            Assert.True(pushed.IsSuccess, pushed.IsFailure ? pushed.Error.Message : null);
        }

        public async Task<TaskSyncSummary> PullAsync()
        {
            var pulled = await _session.PullAsync(Cancellation);
            Assert.True(pulled.IsSuccess, pulled.IsFailure ? pulled.Error.Message : null);
            return pulled.Value;
        }

        public async Task SyncAsync()
        {
            var synced = await _session.SyncAsync(Cancellation);
            Assert.True(synced.IsSuccess, synced.IsFailure ? synced.Error.Message : null);
        }

        /// <summary>What the session itself does with a cursor the service retired.</summary>
        public void ForgetCursor() => _state.Save(_state.Current with { PullCursor = null });

        public void Dispose()
        {
            _http.Dispose();
            _provider.Dispose();
        }
    }

    /// <summary>A backlog whose routed tasks delete without objection; the undo
    /// asks it nothing else.</summary>
    private sealed class DeletingBacklog : IInboxBacklogTarget
    {
        public Task<Result> DeleteRoutedTasksAsync(IReadOnlyList<Guid> taskIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public Task<Result<IReadOnlyList<Guid>>> CreateTasksAsync(InboxRouteRequestDto request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InboxBatchTargetResultDto> CreateBatchTasksAsync(InboxBatchRouteRequestDto request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<IReadOnlyList<Guid>>> ImportPlanAsync(
            string planMarkdown,
            Guid sourceInboxId,
            IReadOnlyList<string> allowedRepoIds,
            string? attachmentPath = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Result<IReadOnlyList<InboxBatchEdge>> ReadDraftedOrder(
            string planMarkdown,
            IReadOnlyCollection<Guid> itemIds,
            IReadOnlyList<string> allowedRepoIds) =>
            throw new NotSupportedException();

        public Task<Result> CommentOnTaskAsync(InboxMergeRequestDto request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
