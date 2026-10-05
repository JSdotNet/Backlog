using System.Net;
using System.Text;
using System.Text.Json;

using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The task replica at the HTTP edge, holding what the deployed one holds: one
/// document per id for one owner, a push taken only when it is a later version —
/// the rule <c>TaskChangePrecedence</c> keeps — and a feed in the order documents
/// were written, the cursor being the position in it.
/// <para>
/// Shared by the tests that pair two desktops through it — the roadmap's documents
/// (local ADR 0018) and the GitHub settings' (local ADR 0020) — so both are held to
/// the same replica.
/// </para>
/// </summary>
internal sealed class FakeTaskReplica
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, (TaskChange Change, Guid DeviceId, long Sequence)> _documents = [];
    private readonly Dictionary<Guid, int> _pushes = [];
    private long _sequence;

    public HttpMessageHandler For(Guid deviceId) => new Handler(this, deviceId);

    public int PushesFrom(Guid deviceId)
    {
        lock (_gate) return _pushes.GetValueOrDefault(deviceId);
    }

    /// <summary>The document the replica holds under <paramref name="id"/>, or null.</summary>
    public TaskChange? Held(Guid id)
    {
        lock (_gate) return _documents.TryGetValue(id, out var held) ? held.Change : null;
    }

    private string Push(Guid deviceId, string body)
    {
        var request = JsonSerializer.Deserialize<PushTasksRequest>(body, Web)!;
        var accepted = 0;

        lock (_gate)
        {
            _pushes[deviceId] = _pushes.GetValueOrDefault(deviceId) + 1;

            foreach (var change in request.Tasks)
            {
                if (_documents.TryGetValue(change.Id, out var held) && !Supersedes(change, held.Change)) continue;

                _documents[change.Id] = (change, deviceId, ++_sequence);
                accepted++;
            }
        }

        return JsonSerializer.Serialize(new PushTasksResponse(accepted), Web);
    }

    private string Pull(string? since)
    {
        var after = long.TryParse(since, out var position) ? position : 0;

        lock (_gate)
        {
            var page = _documents.Values
                .Where(document => document.Sequence > after)
                .OrderBy(document => document.Sequence)
                .Select(document => new TaskChangeRecord(document.Change, document.DeviceId, document.Sequence))
                .ToList();

            return JsonSerializer.Serialize(
                new PullTasksResponse(page, _sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), HasMore: false),
                Web);
        }
    }

    private static bool Supersedes(TaskChange inbound, TaskChange stored) =>
        inbound.UpdatedAt != stored.UpdatedAt
            ? inbound.UpdatedAt > stored.UpdatedAt
            : inbound.DeletedAt is not null && stored.DeletedAt is null;

    private sealed class Handler(FakeTaskReplica replica, Guid deviceId) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Method == HttpMethod.Post
                ? replica.Push(deviceId, await request.Content!.ReadAsStringAsync(cancellationToken))
                : replica.Pull(System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["since"]);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
