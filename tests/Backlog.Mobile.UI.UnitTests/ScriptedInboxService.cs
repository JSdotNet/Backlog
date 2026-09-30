using System.Net;
using System.Net.Http.Json;
using System.Text;

using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The sync service's inbox as the phone meets it, in the three states that
/// matter: answering, not there at all (a conference hall), and up but with its
/// replica still warming (503 <c>sync.replica_unavailable</c>). It keeps the
/// captures that reached it, in arrival order, and answers a repeated id the
/// way the real service does — 200 with what it already has.
/// </summary>
internal sealed class ScriptedInboxService
{
    private readonly Lock _lock = new();
    private readonly List<InboxItem> _items = [];
    private readonly List<CaptureRequest> _received = [];
    private readonly List<(Guid Id, long Bytes)> _uploads = [];

    private TaskCompletionSource? _pullsHeld;
    private int _heldPulls;
    private int _pulls;

    public ScriptedInboxService(params InboxItem[] items) => _items.AddRange(items);

    public InboxServiceState State { get; set; } = InboxServiceState.Answering;

    public int Requests { get; private set; }

    /// <summary>Pulls that arrived while <see cref="HoldPulls"/> was in force.</summary>
    public int HeldPulls => Volatile.Read(ref _heldPulls);

    /// <summary>Every pull that reached the service, however it was answered.</summary>
    public int Pulls => Volatile.Read(ref _pulls);

    /// <summary>
    /// From now on a pull reads the list as it stands when it arrives, then waits
    /// for <see cref="ReleasePulls"/> before answering with it — an answer the
    /// service gave before a capture landed, still on its way back to the phone.
    /// </summary>
    public void HoldPulls() => _pullsHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Lets the held pulls answer; the ones after them answer at once.</summary>
    public void ReleasePulls()
    {
        var held = _pullsHeld;
        _pullsHeld = null;
        held?.TrySetResult();
    }

    /// <summary>Every capture post that reached the service, repeats included,
    /// in the order they arrived.</summary>
    public IReadOnlyList<CaptureRequest> Received
    {
        get
        {
            lock (_lock) return [.. _received];
        }
    }

    /// <summary>Every attachment upload that reached the service, in order: its
    /// id and how many bytes came.</summary>
    public IReadOnlyList<(Guid Id, long Bytes)> Uploads
    {
        get
        {
            lock (_lock) return [.. _uploads];
        }
    }

    /// <summary>Only the posts answered 201 — the ones that wrote something.</summary>
    public int Created { get; private set; }

    public async Task<HttpResponseMessage> AnswerAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        if (request.Method == HttpMethod.Get) Interlocked.Increment(ref _pulls);

        if (request.Method == HttpMethod.Get && State == InboxServiceState.Answering && _pullsHeld is { } held)
        {
            List<InboxItem> answered;
            lock (_lock) answered = [.. _items];

            Interlocked.Increment(ref _heldPulls);
            await held.Task.WaitAsync(cancellationToken);

            return JsonAnswer(HttpStatusCode.OK, answered);
        }

        byte[]? upload = null;
        if (request.Method == HttpMethod.Put && request.Content is not null)
        {
            upload = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        CaptureRequest? capture = null;
        if (request.Method == HttpMethod.Post && request.Content is not null)
        {
            capture = await request.Content.ReadFromJsonAsync<CaptureRequest>(cancellationToken);
        }

        lock (_lock)
        {
            if (capture is not null) _received.Add(capture);

            switch (State)
            {
                case InboxServiceState.Unreachable:
                    throw new HttpRequestException("No such host is known. (sync.test:443)");

                case InboxServiceState.Unauthorized:
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);

                case InboxServiceState.WarmingUp:
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent(
                            """{"title":"Service Unavailable","status":503,"detail":"The replica is not ready yet.","code":"sync.replica_unavailable"}""",
                            Encoding.UTF8,
                            "application/problem+json")
                    };
            }

            if (upload is not null)
            {
                var uploaded = Guid.Parse(request.RequestUri!.Segments[^1]);
                _uploads.Add((uploaded, upload.LongLength));
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            if (request.Method == HttpMethod.Get)
            {
                return JsonAnswer(HttpStatusCode.OK, _items.ToList());
            }

            if (capture is null) return new HttpResponseMessage(HttpStatusCode.NoContent);

            var id = capture.Id ?? Guid.CreateVersion7();
            if (_items.FirstOrDefault(item => item.Id == id) is { } existing)
            {
                return JsonAnswer(HttpStatusCode.OK, existing);
            }

            var stored = new InboxItem(id, capture.Title, capture.Source, DateTimeOffset.UtcNow, capture.BodyMd, capture.Tags ?? [], capture.Person, capture.Attachments);
            _items.Add(stored);
            Created++;

            return JsonAnswer(HttpStatusCode.Created, stored);
        }
    }

    private static HttpResponseMessage JsonAnswer<T>(HttpStatusCode status, T value) =>
        new(status) { Content = JsonContent.Create(value) };
}

internal enum InboxServiceState
{
    Answering,
    Unreachable,
    WarmingUp,

    /// <summary>Up, but rejecting the device's token — a service just restarted
    /// with a new signing key.</summary>
    Unauthorized
}
