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

    public ScriptedInboxService(params InboxItem[] items) => _items.AddRange(items);

    public InboxServiceState State { get; set; } = InboxServiceState.Answering;

    public int Requests { get; private set; }

    /// <summary>Every capture post that reached the service, repeats included,
    /// in the order they arrived.</summary>
    public IReadOnlyList<CaptureRequest> Received
    {
        get
        {
            lock (_lock) return [.. _received];
        }
    }

    /// <summary>Only the posts answered 201 — the ones that wrote something.</summary>
    public int Created { get; private set; }

    public async Task<HttpResponseMessage> AnswerAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;

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

            var stored = new InboxItem(id, capture.Title, capture.Source, DateTimeOffset.UtcNow, capture.BodyMd, capture.Tags ?? [], capture.Person);
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
