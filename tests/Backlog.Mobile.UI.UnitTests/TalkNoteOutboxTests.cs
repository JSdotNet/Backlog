using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;

using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.TalkNotes;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A saved talk note is one outbox entry that is several requests: every
/// attachment's upload, then the capture naming them. These tests hold the
/// order, the retry of only what has not landed, and what the page says while
/// it goes.
/// </summary>
public sealed class TalkNoteOutboxTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 9, 26, 14, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Start);
    private readonly AttachmentService _service = new();
    private readonly TalkNoteFiles _files = new(TestFolders.Create());
    private readonly TestAttachmentPicker _picker = new();
    private readonly DeviceOutbox _outbox;
    private readonly TalkNoteComposer _composer;

    public TalkNoteOutboxTests()
    {
        var sync = new CloudSyncClient(new HttpClient(_service) { BaseAddress = new Uri("https://sync.test") });
        _outbox = new DeviceOutbox(new InMemoryDeviceStore(), [new TalkNoteOutboxKind(sync, _files)], _clock);
        _composer = new TalkNoteComposer(_files, _outbox, _clock);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _outbox.Dispose();

    [Fact]
    public async Task Every_attachment_is_uploaded_before_the_capture_that_names_them()
    {
        var id = await SaveAsync("Handout", ("handout.pdf", "application/pdf"), ("notes.md", "text/markdown"));
        await _outbox.WhenIdleAsync();

        var draftless = _service.Posted!;
        Assert.Equal(["PUT handout.pdf", "PUT notes.md", "POST"], _service.Calls);
        Assert.Equal(id, draftless.Id);
        Assert.Equal(["handout.pdf", "notes.md"], draftless.Attachments!.Select(attachment => attachment.Name));

        // What the capture names is exactly what went up, digest and all.
        foreach (var attachment in draftless.Attachments!)
        {
            Assert.Equal(attachment.Sha256, _service.Digests[attachment.Id]);
            Assert.Equal(attachment.SizeBytes, _service.Sizes[attachment.Id]);
        }

        Assert.Empty(_outbox.Entries);
        Assert.Equal("synced", TalkNoteOutboxKind.StatusOf(_outbox, id));
    }

    [Fact]
    public async Task A_failed_upload_is_retried_on_its_own_and_one_already_sent_is_not_sent_again()
    {
        _service.Answer = (call, attempt) => call == "PUT b.pdf" && attempt == 1 ? HttpStatusCode.ServiceUnavailable : null;

        await SaveAsync("Two files", ("a.pdf", "application/pdf"), ("b.pdf", "application/pdf"));
        await _outbox.WhenIdleAsync();

        Assert.Equal(["PUT a.pdf", "PUT b.pdf"], _service.Calls);
        var waiting = Assert.Single(_outbox.Entries);
        Assert.Equal(1, waiting.Attempts);
        Assert.Contains("b.pdf", waiting.LastError, StringComparison.Ordinal);

        _clock.Advance(DeviceOutbox.FirstRetryDelay);
        await _outbox.WhenIdleAsync();

        Assert.Equal(["PUT a.pdf", "PUT b.pdf", "PUT b.pdf", "POST"], _service.Calls);
        Assert.Empty(_outbox.Entries);
    }

    /// <summary>
    /// Four failures on each of two files is eight in all — past the five the
    /// outbox allows an entry — and yet it is not parked: each upload that lands
    /// gives the attempts back, so each file has the whole budget of its own.
    /// </summary>
    [Fact]
    public async Task Each_upload_has_its_own_attempt_budget()
    {
        _service.Answer = (call, attempt) => call.StartsWith("PUT", StringComparison.Ordinal) && attempt <= 4
            ? HttpStatusCode.BadGateway
            : null;

        await SaveAsync("Budget", ("a.pdf", "application/pdf"), ("b.pdf", "application/pdf"));

        for (var round = 0; round < 12 && _outbox.Entries.Count > 0; round++)
        {
            await _outbox.WhenIdleAsync();
            _clock.Advance(TimeSpan.FromSeconds(16));
        }

        await _outbox.WhenIdleAsync();

        Assert.Empty(_outbox.Entries);
        Assert.Equal(5, _service.Calls.Count(call => call == "PUT a.pdf"));
        Assert.Equal(5, _service.Calls.Count(call => call == "PUT b.pdf"));
        Assert.Equal("POST", _service.Calls[^1]);
    }

    [Fact]
    public async Task A_file_the_service_refuses_refuses_the_note_by_name_and_the_capture_is_never_posted()
    {
        _service.Answer = (call, _) => call == "PUT b.pdf" ? HttpStatusCode.UnsupportedMediaType : null;

        var id = await SaveAsync("Refused", ("a.pdf", "application/pdf"), ("b.pdf", "application/pdf"));
        await _outbox.WhenIdleAsync();

        var parked = Assert.Single(_outbox.Entries);
        Assert.True(parked.Refused);
        Assert.StartsWith("b.pdf:", parked.LastError, StringComparison.Ordinal);
        Assert.DoesNotContain("POST", _service.Calls);
        Assert.Equal("waiting — tap to retry", TalkNoteOutboxKind.StatusOf(_outbox, id));
    }

    [Fact]
    public async Task While_the_second_of_three_goes_up_the_note_says_uploading_2_of_3()
    {
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _service.Hold = call => call == "PUT b.pdf" ? (reached, held.Task) : null;

        var id = await SaveAsync("Three", ("a.pdf", "application/pdf"), ("b.pdf", "application/pdf"), ("c.pdf", "application/pdf"));
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        Assert.Equal("uploading 2 of 3", TalkNoteOutboxKind.StatusOf(_outbox, id));

        held.SetResult();
        await _outbox.WhenIdleAsync();

        Assert.Equal("synced", TalkNoteOutboxKind.StatusOf(_outbox, id));
    }

    [Fact]
    public async Task Offline_the_note_is_waiting_and_its_files_stay_until_the_capture_lands()
    {
        _service.Offline = true;

        var id = await SaveAsync("Offline", ("a.pdf", "application/pdf"));
        await _outbox.WhenIdleAsync();

        Assert.Equal("waiting", TalkNoteOutboxKind.StatusOf(_outbox, id));
        var attachment = TalkNoteOutboxKind.Read(_outbox.Entries[0]).Capture.Attachments!.Single();
        Assert.True(File.Exists(_files.OutboxPath(attachment.Id)));

        _service.Offline = false;
        await _outbox.ResumeAsync(Cancellation);

        Assert.Equal("synced", TalkNoteOutboxKind.StatusOf(_outbox, id));
        Assert.False(File.Exists(_files.OutboxPath(attachment.Id)), "A delivered note's files are released.");
    }

    [Fact]
    public async Task A_picture_is_downscaled_unless_the_note_keeps_originals()
    {
        var photo = await File.ReadAllBytesAsync(TestFolders.Fixture("slide-photo.jpg"), Cancellation);

        var draft = new TalkNoteDraft { Title = "Slides" };
        draft.Attachments.Add(await _files.StageAsync(_picker.File("IMG_0042.JPG", "image/jpeg", photo), 0, Cancellation));
        await _composer.SaveAsync(draft, Cancellation);

        var original = new TalkNoteDraft { Title = "Slides, as taken", SendOriginals = true };
        original.Attachments.Add(await _files.StageAsync(_picker.File("IMG_0043.png", "image/png", photo), 0, Cancellation));
        await _composer.SaveAsync(original, Cancellation);

        await _outbox.WhenIdleAsync();

        var downscaled = _service.Captures[0].Attachments!.Single();
        Assert.Equal("IMG_0042.jpg", downscaled.Name);
        Assert.Equal("image/jpeg", downscaled.ContentType);
        Assert.True(downscaled.SizeBytes < photo.Length, "The downscaled photo is the smaller one.");

        var kept = _service.Captures[1].Attachments!.Single();
        Assert.Equal("IMG_0043.png", kept.Name);
        Assert.Equal(photo.Length, kept.SizeBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(photo)), kept.Sha256);
    }

    [Fact]
    public async Task The_capture_carries_the_body_tags_and_speaker()
    {
        var draft = new TalkNoteDraft
        {
            Body = "Outbox before network.\n\nThe hall has no signal.",
            Tags = "#conference, offline  #conference",
            Speaker = "@ada",
        };

        await _composer.SaveAsync(draft, Cancellation);
        await _outbox.WhenIdleAsync();

        var capture = Assert.Single(_service.Captures);
        Assert.Equal("Outbox before network.", capture.Title);
        Assert.Equal("Outbox before network.\n\nThe hall has no signal.", capture.BodyMd);
        Assert.Equal(["conference", "offline"], capture.Tags);
        Assert.Equal("ada", capture.Person);
        Assert.Null(capture.Attachments);
        Assert.Equal(CaptureOutboxKind.Source, capture.Source);
    }

    private async Task<Guid> SaveAsync(string title, params (string Name, string Type)[] attachments)
    {
        var draft = new TalkNoteDraft { Title = title };
        foreach (var (name, type) in attachments)
        {
            draft.Attachments.Add(await _files.StageAsync(_picker.Text(name, type, $"contents of {name}"), draft.Attachments.Count, Cancellation));
        }

        return (await _composer.SaveAsync(draft, Cancellation))!.Value;
    }

    /// <summary>
    /// The two sync routes a talk note uses, recording every call as
    /// "PUT name" or "POST" in arrival order. <see cref="Answer"/> overrides the
    /// status of a call's nth attempt; <see cref="Hold"/> parks one until the
    /// test lets it go.
    /// </summary>
    private sealed class AttachmentService : HttpMessageHandler
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<Guid, string> _names = [];
        private readonly Dictionary<string, int> _attempts = [];

        public List<string> Calls { get; } = [];

        public Dictionary<Guid, string> Digests { get; } = [];

        public Dictionary<Guid, long> Sizes { get; } = [];

        public List<CaptureRequest> Captures { get; } = [];

        public CaptureRequest? Posted => Captures.LastOrDefault();

        public bool Offline { get; set; }

        public Func<string, int, HttpStatusCode?>? Answer { get; set; }

        public Func<string, (TaskCompletionSource Reached, Task Release)?>? Hold { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Offline) throw new HttpRequestException("No such host is known. (sync.test:443)");

            string call;
            if (request.Method == HttpMethod.Put)
            {
                var id = Guid.Parse(request.RequestUri!.Segments[^1]);
                var bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);

                // The name is not on the wire; it is matched back through the
                // capture's metadata once the POST arrives, so record by id here.
                call = $"PUT {NameOf(id, bytes)}";

                lock (_lock)
                {
                    Digests[id] = request.Headers.GetValues(SyncRoutes.AttachmentSha256Header).Single();
                    Sizes[id] = bytes.LongLength;
                }
            }
            else
            {
                call = "POST";
            }

            int attempt;
            lock (_lock)
            {
                Calls.Add(call);
                attempt = _attempts[call] = _attempts.GetValueOrDefault(call) + 1;
            }

            if (Hold?.Invoke(call) is { } hold)
            {
                hold.Reached.TrySetResult();
                await hold.Release;
            }

            if (Answer?.Invoke(call, attempt) is { } status)
            {
                return new HttpResponseMessage(status) { Content = JsonContent.Create(new { detail = $"answered {(int)status}" }) };
            }

            if (call == "POST")
            {
                var capture = (await request.Content!.ReadFromJsonAsync<CaptureRequest>(cancellationToken))!;
                lock (_lock) Captures.Add(capture);
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.Created);
        }

        /// <summary>The test files' bytes are "contents of {name}", which is
        /// how a PUT is told apart without a name on the wire.</summary>
        private string NameOf(Guid id, byte[] bytes)
        {
            lock (_lock)
            {
                if (_names.TryGetValue(id, out var known)) return known;

                var text = System.Text.Encoding.UTF8.GetString(bytes);
                var name = text.StartsWith("contents of ", StringComparison.Ordinal) ? text["contents of ".Length..] : id.ToString("N");
                _names[id] = name;
                return name;
            }
        }
    }
}
