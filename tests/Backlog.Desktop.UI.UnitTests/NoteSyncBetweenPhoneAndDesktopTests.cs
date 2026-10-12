using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.Sqlite.Inbox;
using Backlog.Infrastructure.Sync;
using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Services;
using Backlog.Mobile.UI.TalkNotes;
using Backlog.Modules.Inbox;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Extensions;
using Backlog.Modules.Inbox.Features.ArchiveItem;
using Backlog.Modules.Inbox.Features.DeleteItem;
using Backlog.Modules.Inbox.Features.EditNote;
using Backlog.Modules.Tasks;
using Backlog.SharedKernel.Results;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A phone and a desktop sharing notes through the task feed
/// (.devbook/arc42/06-runtime-view.md#mobile-note-sync). The phone is its real note
/// view, outbox and sync client over its own SQLite file; the desktop is the real
/// Inbox module over its own <c>backlog.db</c>, with the real merge and session. The
/// two exchange through one replica double that keeps what the deployed service
/// keeps: one document per id, a later version wins, a feed in write order.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class NoteSyncBetweenPhoneAndDesktopTests : IDisposable
{
    private static readonly Guid Owner = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset Start = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "note-sync-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTaskReplica _replica = new();
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly Phone _phone;
    private readonly Desktop _desktop;

    public NoteSyncBetweenPhoneAndDesktopTests()
    {
        _phone = new Phone(Path.Combine(_root, "phone"), _replica, _clock);
        _desktop = new Desktop(Path.Combine(_root, "desktop"), _replica, _clock);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _phone.Dispose();
        _desktop.Dispose();
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task A_note_made_on_the_phone_reaches_the_desktop_inbox_and_stays_on_the_phone()
    {
        var made = await _phone.Notes.CreateAsync("Standup", "- shipped note sync", cancellationToken: Cancellation);
        await _phone.Outbox.FlushAsync(Cancellation);
        Assert.Empty(_phone.Outbox.Entries);

        await _desktop.SyncAsync();

        var item = await _desktop.ItemAsync(made.Id);
        Assert.NotNull(item);
        Assert.Equal(ContentKind.Note, item.Kind);
        Assert.Equal("Standup", item.Title);
        Assert.Equal("- shipped note sync", item.BodyMd);
        Assert.Equal("mobile", item.Source.Channel);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);

        // Taken in, not acknowledged away: the replica still holds the live note,
        // and the phone, pulling, still lists it.
        await _desktop.SyncAsync();
        Assert.Null(_replica.Held(made.Id)!.DeletedAt);
        await _phone.Notes.PullAsync(Cancellation);
        Assert.Equal(made.Id, Assert.Single(_phone.Notes.Notes()).Id);
    }

    [Fact]
    public async Task An_edit_on_the_desktop_reaches_the_phone()
    {
        var made = await _phone.Notes.CreateAsync("Standup", "first", cancellationToken: Cancellation);
        await _phone.Outbox.FlushAsync(Cancellation);
        await _desktop.SyncAsync();

        _clock.Advance(TimeSpan.FromMinutes(30));
        Assert.True((await _desktop.EditAsync(made.Id, "Standup (desktop)", "first\nadded at the desk")).IsSuccess);
        await _desktop.SyncAsync();

        await _phone.Notes.PullAsync(Cancellation);

        var note = Assert.Single(_phone.Notes.Notes());
        Assert.Equal("Standup (desktop)", note.Title);
        Assert.Equal("first\nadded at the desk", note.Body);
        Assert.Equal(Start.AddMinutes(30), note.UpdatedAt);
    }

    [Fact]
    public async Task An_edit_on_the_phone_reaches_the_desktop_after_one_made_there()
    {
        var made = await _phone.Notes.CreateAsync("Standup", "first", cancellationToken: Cancellation);
        await _phone.Outbox.FlushAsync(Cancellation);
        await _desktop.SyncAsync();
        _clock.Advance(TimeSpan.FromMinutes(10));
        await _desktop.EditAsync(made.Id, "Standup", "desk");
        await _desktop.SyncAsync();
        await _phone.Notes.PullAsync(Cancellation);

        _clock.Advance(TimeSpan.FromMinutes(10));
        await _phone.Notes.EditAsync(made.Id, "Standup", "desk\nphone", cancellationToken: Cancellation);
        await _phone.Outbox.FlushAsync(Cancellation);
        await _desktop.SyncAsync();

        Assert.Equal("desk\nphone", (await _desktop.ItemAsync(made.Id))!.BodyMd);
    }

    [Fact]
    public async Task Archiving_on_the_desktop_removes_the_note_from_the_phone()
    {
        var made = await _phone.Notes.CreateAsync("Standup", "text", cancellationToken: Cancellation);
        await _phone.Outbox.FlushAsync(Cancellation);
        await _desktop.SyncAsync();
        await _phone.Notes.PullAsync(Cancellation);
        Assert.Single(_phone.Notes.Notes());

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.True((await _desktop.ArchiveAsync(made.Id)).IsSuccess);
        await _desktop.SyncAsync();

        await _phone.Notes.PullAsync(Cancellation);

        Assert.Empty(_phone.Notes.Notes());
        Assert.NotNull(_replica.Held(made.Id)!.DeletedAt);
        Assert.Equal("note", _replica.Held(made.Id)!.Task.Type);
    }

    [Fact]
    public async Task Deleting_on_the_desktop_removes_the_note_from_the_phone()
    {
        var made = await _phone.Notes.CreateAsync("Standup", "text", cancellationToken: Cancellation);
        await _phone.Outbox.FlushAsync(Cancellation);
        await _desktop.SyncAsync();

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.True((await _desktop.DeleteAsync(made.Id)).IsSuccess);
        await _desktop.SyncAsync();

        await _phone.Notes.PullAsync(Cancellation);

        Assert.Empty(_phone.Notes.Notes());
        Assert.Null(await _desktop.ItemAsync(made.Id));
    }

    [Fact]
    public async Task A_phone_edit_made_before_it_heard_of_the_archive_does_not_bring_the_note_back()
    {
        var made = await _phone.Notes.CreateAsync("Standup", "text", cancellationToken: Cancellation);
        await _phone.Outbox.FlushAsync(Cancellation);
        await _desktop.SyncAsync();
        await _phone.Notes.PullAsync(Cancellation);

        // The desktop archives; the phone, not yet told, edits a little later.
        _clock.Advance(TimeSpan.FromMinutes(5));
        await _desktop.ArchiveAsync(made.Id);
        await _desktop.SyncAsync();
        _clock.Advance(TimeSpan.FromMinutes(5));
        await _phone.Notes.EditAsync(made.Id, "Standup", "edited offline", cancellationToken: Cancellation);
        await _phone.Outbox.FlushAsync(Cancellation);

        // The desktop sees the edit, keeps its archive, and restates it.
        await _desktop.SyncAsync();
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _desktop.SyncAsync();

        await _phone.Notes.PullAsync(Cancellation);

        Assert.Empty(_phone.Notes.Notes());
        Assert.Equal(InboxStatus.Archived, (await _desktop.ItemAsync(made.Id))!.Status);
    }

    /// <summary>The phone: its note view, outbox and sync client over its own file,
    /// talking to the shared replica as its own device.</summary>
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

    /// <summary>The desktop: the real Inbox module over its own database, and the
    /// task sync session composed the way the sync client registration composes it,
    /// with the intake, the acknowledgement outbox and the note port.</summary>
    private sealed class Desktop : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly HttpClient _http;
        private readonly TaskSyncSession _session;
        private readonly SqliteInboxRepository _inbox;
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

            _http = new HttpClient(replica.For(deviceId)) { BaseAddress = new Uri("https://sync.test") };
            _session = new TaskSyncSession(
                new TaskSyncClient(_http),
                new TaskReplicaMerge(tasks, _provider.GetRequiredService<IInboxIntake>(), notes: notes),
                tasks,
                new FileTaskSyncStateStore(Path.Combine(root, "sync", "task-sync-state.json")),
                new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, deviceId, $"PC {deviceId:N}", "credential")),
                clock,
                _provider.GetRequiredService<IInboxCaptureOutbox>(),
                notes: notes);
        }

        public Task<InboxItem?> ItemAsync(Guid id) => _inbox.GetAsync(id, Cancellation);

        public Task<Result> EditAsync(Guid id, string title, string? body) =>
            new EditNoteCommandHandler(_inbox, _clock).Handle(new EditNoteCommand(id, title, body), Cancellation);

        public Task<Result> ArchiveAsync(Guid id) =>
            new ArchiveItemCommandHandler(_inbox, _clock).Handle(new ArchiveItemCommand(id), Cancellation);

        public Task<Result> DeleteAsync(Guid id) =>
            new DeleteItemCommandHandler(_inbox, _clock).Handle(new DeleteItemCommand(id), Cancellation);

        public async Task SyncAsync()
        {
            var synced = await _session.SyncAsync(Cancellation);
            Assert.True(synced.IsSuccess, synced.IsFailure ? synced.Error.Message : null);
        }

        public void Dispose()
        {
            _http.Dispose();
            _provider.Dispose();
        }
    }
}
