using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Services;
using Backlog.Mobile.UI.TalkNotes;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The Inbox tab's edit and Delete of a capture, as the outbox carries them: a
/// capture still waiting is changed where it waits and never reaches the service
/// in its old form; one that has left — or is leaving — gets an entry of its own
/// behind it. And the list shows the change at once, over whatever the service
/// last said.
/// </summary>
public sealed class CaptureEditsTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "capture-edits-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly TalkNoteFiles _files;

    public CaptureEditsTests() => _files = new TalkNoteFiles(_root);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // --- Edit ----------------------------------------------------------------

    [Fact]
    public async Task Editing_a_capture_still_waiting_rewrites_it_where_it_waits()
    {
        using var outbox = Offline();
        var capture = await WaitingCaptureAsync(outbox, "Call the dentist");

        await new CaptureEdits(outbox, _files).EditAsync(capture, "Call the dentist about the crown", "Thursday.", Cancellation);

        var entry = Assert.Single(outbox.Entries);
        Assert.Equal(capture, entry.Id);
        var request = CaptureOutboxKind.Read(entry);
        Assert.Equal("Call the dentist about the crown", request.Title);
        Assert.Equal("Thursday.", request.BodyMd);
        Assert.Equal(CaptureOutboxKind.Source, request.Source);
    }

    /// <summary>A talk note's uploads are its own progress; rewording it keeps
    /// them, so no file is sent twice.</summary>
    [Fact]
    public async Task Editing_a_talk_note_still_waiting_rewrites_its_capture_and_keeps_its_uploads()
    {
        using var outbox = Offline();
        var file = new AttachmentMetadata(Guid.CreateVersion7(), "note.m4a", "audio/mp4", 10, new string('a', 64));
        var id = Guid.CreateVersion7();
        var payload = new TalkNotePayload(new CaptureRequest("Standup", CaptureOutboxKind.Source, id, Attachments: [file]), [file.Id]);
        await outbox.EnqueueAsync(TalkNoteOutboxKind.Token, id, TalkNoteOutboxKind.Write(payload), Cancellation);
        await outbox.WhenIdleAsync();

        await new CaptureEdits(outbox, _files).EditAsync(id, "Standup notes", "Three points.", Cancellation);

        var rewritten = TalkNoteOutboxKind.Read(Assert.Single(outbox.Entries));
        Assert.Equal("Standup notes", rewritten.Capture.Title);
        Assert.Equal("Three points.", rewritten.Capture.BodyMd);
        Assert.Equal([file.Id], rewritten.Uploaded);
        Assert.Equal(file.Id, Assert.Single(rewritten.Capture.Attachments!).Id);
    }

    [Fact]
    public async Task Editing_a_capture_that_has_left_queues_an_edit_naming_it()
    {
        using var outbox = Offline();
        var capture = Guid.CreateVersion7();

        await new CaptureEdits(outbox, _files).EditAsync(capture, "Reworded", "More.", Cancellation);

        var entry = Assert.Single(outbox.Entries);
        Assert.Equal(CaptureEditOutboxKind.Token, entry.Kind);
        Assert.NotEqual(capture, entry.Id);
        Assert.Equal(new CaptureEdit(capture, "Reworded", "More."), CaptureEditOutboxKind.Read(entry));
    }

    /// <summary>One pause, one entry: a second edit before the first has left
    /// rewrites it rather than queuing another.</summary>
    [Fact]
    public async Task A_second_edit_before_the_first_left_rewrites_it()
    {
        using var outbox = Offline();
        var capture = Guid.CreateVersion7();
        var edits = new CaptureEdits(outbox, _files);

        await edits.EditAsync(capture, "Reworded", null, Cancellation);
        await edits.EditAsync(capture, "Reworded again", "Now with a body.", Cancellation);

        Assert.Equal(new CaptureEdit(capture, "Reworded again", "Now with a body."), CaptureEditOutboxKind.Read(Assert.Single(outbox.Entries)));
    }

    /// <summary>The capture is on its way out: its words are already said, so the
    /// edit goes behind it, to arrive after it.</summary>
    [Fact]
    public async Task Editing_a_capture_being_sent_queues_the_edit_behind_it()
    {
        var gate = new GatedCaptureKind();
        using var outbox = new DeviceOutbox(new InMemoryDeviceStore(), [gate, new OfflineKind(CaptureEditOutboxKind.Token)], _clock);
        var capture = CaptureOutboxKind.Create("Call the dentist");
        await outbox.EnqueueAsync(CaptureOutboxKind.Token, capture.Id!.Value, CaptureOutboxKind.Write(capture), Cancellation);
        Assert.Equal(capture.Id, outbox.Sending);

        await new CaptureEdits(outbox, _files).EditAsync(capture.Id.Value, "Reworded", null, Cancellation);

        Assert.Equal([CaptureOutboxKind.Token, CaptureEditOutboxKind.Token], outbox.Entries.Select(entry => entry.Kind));
        Assert.Equal("Call the dentist", CaptureOutboxKind.Read(outbox.Entries[0]).Title);
        gate.Release();
    }

    /// <summary>A capture the service refused for what it said — a title too long —
    /// is set aside; rewording it is the person's answer to that, so the rewritten
    /// capture goes again without a further tap.</summary>
    [Fact]
    public async Task Rewording_a_capture_that_was_set_aside_sends_it_again()
    {
        var kind = new RefusingOnceKind(CaptureOutboxKind.Token);
        using var outbox = new DeviceOutbox(new InMemoryDeviceStore(), [kind], _clock);
        var capture = CaptureOutboxKind.Create("A title the service refuses");
        await outbox.EnqueueAsync(CaptureOutboxKind.Token, capture.Id!.Value, CaptureOutboxKind.Write(capture), Cancellation);
        await outbox.WhenIdleAsync();
        Assert.True(Assert.Single(outbox.Entries).Refused);

        await new CaptureEdits(outbox, _files).EditAsync(capture.Id.Value, "A shorter title", null, Cancellation);
        await outbox.WhenIdleAsync();

        Assert.Empty(outbox.Entries);
        Assert.Equal("A shorter title", CaptureOutboxKind.Read(kind.Delivered.Single()).Title);
    }

    /// <summary>The same for an edit set aside because the service could not take
    /// it: rewording it again sends it again.</summary>
    [Fact]
    public async Task Rewording_again_after_an_edit_was_set_aside_sends_it_again()
    {
        var kind = new RefusingOnceKind(CaptureEditOutboxKind.Token);
        using var outbox = new DeviceOutbox(new InMemoryDeviceStore(), [kind], _clock);
        var capture = Guid.CreateVersion7();
        var edits = new CaptureEdits(outbox, _files);
        await edits.EditAsync(capture, "Reworded", null, Cancellation);
        await outbox.WhenIdleAsync();
        Assert.True(Assert.Single(outbox.Entries).Refused);

        await edits.EditAsync(capture, "Reworded again", null, Cancellation);
        await outbox.WhenIdleAsync();

        Assert.Empty(outbox.Entries);
        Assert.Equal(new CaptureEdit(capture, "Reworded again", null), CaptureEditOutboxKind.Read(kind.Delivered.Single()));
    }

    /// <summary>
    /// The person rewords a capture while it is being sent, so the edit waits behind
    /// it — and then the service refuses the capture. Sent on its own, the edit would
    /// find nothing (404) and read as "already dealt with" while the capture stayed
    /// set aside with its old words. Instead the edit's words go into the capture,
    /// the edit is dropped unsent, and retrying the capture sends the new words.
    /// </summary>
    [Fact]
    public async Task An_edit_waiting_behind_a_capture_the_service_refuses_is_folded_into_it()
    {
        var captures = new GatedRefusingOnceKind();
        var editKind = new RecordingKind(CaptureEditOutboxKind.Token);
        using var outbox = new DeviceOutbox(new InMemoryDeviceStore(), [captures, editKind], _clock, folds: [new CaptureEditFold()]);
        var capture = CaptureOutboxKind.Create("A title the service refuses");
        var id = capture.Id!.Value;
        await outbox.EnqueueAsync(CaptureOutboxKind.Token, id, CaptureOutboxKind.Write(capture), Cancellation);
        Assert.Equal(id, outbox.Sending);

        await new CaptureEdits(outbox, _files).EditAsync(id, "A shorter title", "Thursday.", Cancellation);
        Assert.Equal([CaptureOutboxKind.Token, CaptureEditOutboxKind.Token], outbox.Entries.Select(entry => entry.Kind));

        captures.Release();
        await outbox.FlushAsync(Cancellation);
        await outbox.WhenIdleAsync();

        var setAside = Assert.Single(outbox.Entries);
        Assert.Equal(id, setAside.Id);
        Assert.True(setAside.Refused);
        Assert.Equal("A shorter title", CaptureOutboxKind.Read(setAside).Title);
        Assert.Equal("Thursday.", CaptureOutboxKind.Read(setAside).BodyMd);
        Assert.Empty(editKind.Sent);

        await outbox.RetryAsync(id, Cancellation);

        Assert.Empty(outbox.Entries);
        var sent = CaptureOutboxKind.Read(captures.Delivered.Single());
        Assert.Equal("A shorter title", sent.Title);
        Assert.Equal("Thursday.", sent.BodyMd);
        Assert.Empty(editKind.Sent);
    }

    /// <summary>The same when the edit was already in line as the pass reached the
    /// capture — a phone restarted with both waiting — and for a talk note, whose
    /// uploads are kept.</summary>
    [Fact]
    public async Task An_edit_already_behind_a_talk_note_the_service_refuses_is_folded_into_it()
    {
        var file = new AttachmentMetadata(Guid.CreateVersion7(), "note.m4a", "audio/mp4", 10, new string('a', 64));
        var id = Guid.CreateVersion7();
        var payload = new TalkNotePayload(new CaptureRequest("Standup", CaptureOutboxKind.Source, id, Attachments: [file]), [file.Id]);
        var store = new InMemoryDeviceStore();
        await store.AddAsync(new OutboxEntry(id, TalkNoteOutboxKind.Token, TalkNoteOutboxKind.Write(payload), 0, null, Start), Cancellation);
        await store.AddAsync(Entry(CaptureEditOutboxKind.Token, CaptureEditOutboxKind.Write(new CaptureEdit(id, "First words", null))) with { CreatedAt = Start.AddSeconds(1) }, Cancellation);
        await store.AddAsync(Entry(CaptureEditOutboxKind.Token, CaptureEditOutboxKind.Write(new CaptureEdit(id, "Standup notes", "Three points."))) with { CreatedAt = Start.AddSeconds(2) }, Cancellation);
        var talkNotes = new RefusingOnceKind(TalkNoteOutboxKind.Token);
        var editKind = new RecordingKind(CaptureEditOutboxKind.Token);
        using var outbox = new DeviceOutbox(store, [talkNotes, editKind], _clock, folds: [new CaptureEditFold()]);

        await outbox.FlushAsync(Cancellation);

        var setAside = Assert.Single(outbox.Entries);
        Assert.True(setAside.Refused);
        var folded = TalkNoteOutboxKind.Read(setAside);
        Assert.Equal("Standup notes", folded.Capture.Title);
        Assert.Equal("Three points.", folded.Capture.BodyMd);
        Assert.Equal([file.Id], folded.Uploaded);
        Assert.Single(store.ReadOutbox());
        Assert.Empty(editKind.Sent);
    }

    /// <summary>An edit of another capture, behind a refused one, is left alone.</summary>
    [Fact]
    public async Task An_edit_of_another_capture_is_not_folded()
    {
        var store = new InMemoryDeviceStore();
        var id = Guid.CreateVersion7();
        await store.AddAsync(new OutboxEntry(id, CaptureOutboxKind.Token, CaptureOutboxKind.Write(CaptureOutboxKind.Create("Refused") with { Id = id }), 0, null, Start), Cancellation);
        await store.AddAsync(Entry(CaptureEditOutboxKind.Token, CaptureEditOutboxKind.Write(new CaptureEdit(Guid.CreateVersion7(), "Other", null))) with { CreatedAt = Start.AddSeconds(1) }, Cancellation);
        var editKind = new RecordingKind(CaptureEditOutboxKind.Token);
        using var outbox = new DeviceOutbox(store, [new RefusingOnceKind(CaptureOutboxKind.Token), editKind], _clock, folds: [new CaptureEditFold()]);

        await outbox.FlushAsync(Cancellation);

        Assert.Equal("Refused", CaptureOutboxKind.Read(Assert.Single(outbox.Entries)).Title);
        Assert.Equal("Other", CaptureEditOutboxKind.Read(Assert.Single(editKind.Sent)).Title);
    }

    [Fact]
    public async Task A_blank_title_is_not_an_edit()
    {
        using var outbox = Offline();

        await Assert.ThrowsAsync<ArgumentException>(() => new CaptureEdits(outbox, _files).EditAsync(Guid.CreateVersion7(), " ", null, Cancellation));
        Assert.Empty(outbox.Entries);
    }

    // --- Delete --------------------------------------------------------------

    [Fact]
    public async Task Deleting_a_capture_still_waiting_takes_it_out_and_sends_nothing()
    {
        using var outbox = Offline();
        var capture = await WaitingCaptureAsync(outbox, "Call the dentist");

        await new CaptureEdits(outbox, _files).WithdrawAsync(capture, Cancellation);

        Assert.Empty(outbox.Entries);
    }

    /// <summary>A talk note taken out before it left has nobody left to upload
    /// its files for, so they go with it.</summary>
    [Fact]
    public async Task Deleting_a_talk_note_still_waiting_releases_its_files()
    {
        using var outbox = Offline();
        var file = new AttachmentMetadata(Guid.CreateVersion7(), "note.m4a", "audio/mp4", 10, new string('a', 64));
        Directory.CreateDirectory(_files.OutboxFolder);
        await File.WriteAllTextAsync(_files.OutboxPath(file.Id), "audio", Cancellation);
        var id = Guid.CreateVersion7();
        var payload = new TalkNotePayload(new CaptureRequest("Standup", CaptureOutboxKind.Source, id, Attachments: [file]), []);
        await outbox.EnqueueAsync(TalkNoteOutboxKind.Token, id, TalkNoteOutboxKind.Write(payload), Cancellation);

        await new CaptureEdits(outbox, _files).WithdrawAsync(id, Cancellation);

        Assert.Empty(outbox.Entries);
        Assert.False(File.Exists(_files.OutboxPath(file.Id)));
    }

    /// <summary>A capture the service holds is withdrawn through the service, and
    /// an edit of it still waiting has nothing left to change.</summary>
    [Fact]
    public async Task Deleting_a_capture_that_has_left_queues_one_withdrawal_and_drops_its_waiting_edit()
    {
        using var outbox = Offline();
        var capture = Guid.CreateVersion7();
        var edits = new CaptureEdits(outbox, _files);
        await edits.EditAsync(capture, "Reworded", null, Cancellation);

        await edits.WithdrawAsync(capture, Cancellation);
        await edits.WithdrawAsync(capture, Cancellation);

        var entry = Assert.Single(outbox.Entries);
        Assert.Equal(CaptureWithdrawOutboxKind.Token, entry.Kind);
        Assert.Equal(new CaptureWithdrawal(capture), CaptureWithdrawOutboxKind.Read(entry));
    }

    [Fact]
    public async Task Deleting_a_capture_being_sent_queues_its_withdrawal_behind_it()
    {
        var gate = new GatedCaptureKind();
        using var outbox = new DeviceOutbox(new InMemoryDeviceStore(), [gate, new OfflineKind(CaptureWithdrawOutboxKind.Token)], _clock);
        var capture = CaptureOutboxKind.Create("Call the dentist");
        await outbox.EnqueueAsync(CaptureOutboxKind.Token, capture.Id!.Value, CaptureOutboxKind.Write(capture), Cancellation);

        await new CaptureEdits(outbox, _files).WithdrawAsync(capture.Id.Value, Cancellation);

        Assert.Equal([CaptureOutboxKind.Token, CaptureWithdrawOutboxKind.Token], outbox.Entries.Select(entry => entry.Kind));
        gate.Release();
    }

    // --- The list ------------------------------------------------------------

    /// <summary>What the service last said, with the phone's own change laid over
    /// it until the service has it: the latest edit's words, or nothing at all for
    /// a capture being deleted.</summary>
    [Fact]
    public void A_waiting_edit_is_shown_over_the_capture_and_a_waiting_withdrawal_hides_it()
    {
        var edited = new InboxItem(Guid.CreateVersion7(), "Call the dentist", "mobile", Start, "old", ["health"], "sam");
        var deleted = new InboxItem(Guid.CreateVersion7(), "Buy milk", "mobile", Start);
        var untouched = new InboxItem(Guid.CreateVersion7(), "Water the plants", "mobile", Start);
        OutboxEntry[] entries =
        [
            Entry(CaptureEditOutboxKind.Token, CaptureEditOutboxKind.Write(new CaptureEdit(edited.Id, "First words", null))),
            Entry(CaptureEditOutboxKind.Token, CaptureEditOutboxKind.Write(new CaptureEdit(edited.Id, "Call the dentist about the crown", "Thursday."))),
            Entry(CaptureWithdrawOutboxKind.Token, CaptureWithdrawOutboxKind.Write(new CaptureWithdrawal(deleted.Id))),
            Entry("capture", "{}"),
        ];

        var shown = CaptureEdits.Overlay(edited, entries)!;
        Assert.Equal("Call the dentist about the crown", shown.Title);
        Assert.Equal("Thursday.", shown.BodyMd);
        Assert.Equal(["health"], shown.Tags!);
        Assert.Equal("sam", shown.Person);
        Assert.True(CaptureEdits.IsChangeWaiting(edited.Id, entries));

        Assert.Null(CaptureEdits.Overlay(deleted, entries));
        Assert.True(CaptureEdits.IsChangeWaiting(deleted.Id, entries));

        Assert.Same(untouched, CaptureEdits.Overlay(untouched, entries));
        Assert.False(CaptureEdits.IsChangeWaiting(untouched.Id, entries));

        // The row's stuck reason and its retry come from the newest change waiting.
        Assert.Equal(entries[1].Id, CaptureEdits.WaitingChange(edited.Id, entries)!.Id);
        Assert.Equal(entries[2].Id, CaptureEdits.WaitingChange(deleted.Id, entries)!.Id);
        Assert.Null(CaptureEdits.WaitingChange(untouched.Id, entries));
    }

    // --- Composition ---------------------------------------------------------

    /// <summary>The MAUI head and the web harness both compose the outbox through
    /// <c>AddDeviceOutbox</c>, so both send the two new kinds.</summary>
    [Fact]
    public void The_device_outbox_composition_sends_both_kinds_and_offers_the_edits()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TestCloudSync.Client(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        services.AddDeviceOutbox(Path.Combine(_root, "device.db"));
        using var provider = services.BuildServiceProvider();

        var kinds = provider.GetServices<IOutboxKind>().Select(kind => kind.Kind).ToList();
        Assert.Contains(CaptureEditOutboxKind.Token, kinds);
        Assert.Contains(CaptureWithdrawOutboxKind.Token, kinds);
        Assert.NotNull(provider.GetRequiredService<CaptureEdits>());
        Assert.Contains(provider.GetServices<IOutboxFold>(), fold => fold is CaptureEditFold);
        Assert.Same(provider.GetRequiredService<CaptureDealtWithNotices>(), provider.GetRequiredService<CaptureDealtWithNotices>());
    }

    private OutboxEntry Entry(string kind, string payload) => new(Guid.CreateVersion7(), kind, payload, 0, null, Start);

    /// <summary>An outbox whose every kind finds no network, so what is queued
    /// stays queued and nothing is being sent once a pass has run.</summary>
    private DeviceOutbox Offline() => new(
        new InMemoryDeviceStore(),
        [
            new OfflineKind(CaptureOutboxKind.Token),
            new OfflineKind(TalkNoteOutboxKind.Token),
            new OfflineKind(CaptureEditOutboxKind.Token),
            new OfflineKind(CaptureWithdrawOutboxKind.Token),
        ],
        _clock);

    private static async Task<Guid> WaitingCaptureAsync(DeviceOutbox outbox, string title)
    {
        var capture = CaptureOutboxKind.Create(title);
        await outbox.EnqueueAsync(CaptureOutboxKind.Token, capture.Id!.Value, CaptureOutboxKind.Write(capture), Cancellation);
        await outbox.WhenIdleAsync();
        return capture.Id.Value;
    }

    private sealed class OfflineKind(string token) : IOutboxKind
    {
        public string Kind => token;

        public Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken) =>
            Task.FromResult(OutboxDelivery.Transient("No network."));
    }

    /// <summary>Refuses the first entry it is sent, then delivers.</summary>
    private sealed class RefusingOnceKind(string token) : IOutboxKind
    {
        private bool _refused;

        public string Kind => token;

        public List<OutboxEntry> Delivered { get; } = [];

        public Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken)
        {
            if (!_refused)
            {
                _refused = true;
                return Task.FromResult(OutboxDelivery.Refused("Too long."));
            }

            Delivered.Add(entry);
            return Task.FromResult(OutboxDelivery.Delivered);
        }
    }

    /// <summary>Delivers everything it is sent, and keeps it.</summary>
    private sealed class RecordingKind(string token) : IOutboxKind
    {
        public string Kind => token;

        public List<OutboxEntry> Sent { get; } = [];

        public Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken)
        {
            Sent.Add(entry);
            return Task.FromResult(OutboxDelivery.Delivered);
        }
    }

    /// <summary>The capture kind caught on its way out until released, refusing
    /// that first attempt and delivering every later one.</summary>
    private sealed class GatedRefusingOnceKind : IOutboxKind
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _refused;

        public string Kind => CaptureOutboxKind.Token;

        public List<OutboxEntry> Delivered { get; } = [];

        public void Release() => _gate.TrySetResult();

        public async Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken)
        {
            await _gate.Task.WaitAsync(cancellationToken);

            if (!_refused)
            {
                _refused = true;
                return OutboxDelivery.Refused("Too long.");
            }

            Delivered.Add(entry);
            return OutboxDelivery.Delivered;
        }
    }

    /// <summary>The capture kind caught on its way out until released.</summary>
    private sealed class GatedCaptureKind : IOutboxKind
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Kind => CaptureOutboxKind.Token;

        public void Release() => _gate.TrySetResult();

        public async Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken)
        {
            await _gate.Task.WaitAsync(cancellationToken);
            return OutboxDelivery.Delivered;
        }
    }
}
