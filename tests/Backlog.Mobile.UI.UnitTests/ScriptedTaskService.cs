using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;

using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The sync service's task feed as the phone meets it: an append-only list of
/// records paged by a cursor that is just a position, pushes that append to it with
/// the next server stamp, and the failures the phone has to survive — no network,
/// and a cursor the service will not take.
/// </summary>
internal sealed class ScriptedTaskService
{
    private const string CursorPrefix = "pos:";

    private readonly Lock _lock = new();
    private readonly List<TaskChangeRecord> _feed = [];
    private readonly List<TaskChange> _pushed = [];
    private long _serverTimestamp;

    public ScriptedTaskService(params TaskChange[] changes)
    {
        foreach (var change in changes) Append(change);
    }

    public InboxServiceState State { get; set; } = InboxServiceState.Answering;

    /// <summary>How many records a page carries. Small in the paging tests, so a
    /// pull has to follow <c>HasMore</c> to see everything.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Set to a problem code to refuse any cursor the phone sends —
    /// <c>sync.cursor_expired</c>, <c>sync.cursor_malformed</c>. A pull from the
    /// beginning still answers.</summary>
    public string? RejectCursorWith { get; set; }

    public int Pulls { get; private set; }

    /// <summary>The <c>since</c> of every pull, in order — null for a pull from
    /// the beginning.</summary>
    public List<string?> PulledFrom { get; } = [];

    /// <summary>Every change a push carried, retries included, in arrival order.</summary>
    public IReadOnlyList<TaskChange> Pushed
    {
        get
        {
            lock (_lock) return [.. _pushed];
        }
    }

    /// <summary>A change another device made: on the feed from the next pull.</summary>
    public void Append(TaskChange change)
    {
        lock (_lock) _feed.Add(new TaskChangeRecord(change, Guid.NewGuid(), ++_serverTimestamp));
    }

    public async Task<HttpResponseMessage> AnswerAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        PushTasksRequest? push = null;
        if (request.Method == HttpMethod.Post && request.Content is not null)
        {
            push = await request.Content.ReadFromJsonAsync<PushTasksRequest>(cancellationToken);
        }

        lock (_lock)
        {
            if (push is not null) _pushed.AddRange(push.Tasks);

            if (State == InboxServiceState.Unreachable)
            {
                throw new HttpRequestException("No such host is known. (sync.test:443)");
            }

            if (State == InboxServiceState.WarmingUp)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "sync.replica_unavailable");
            }

            if (push is not null)
            {
                foreach (var change in push.Tasks)
                {
                    _feed.Add(new TaskChangeRecord(change, Guid.NewGuid(), ++_serverTimestamp));
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new PushTasksResponse(push.Tasks.Count)) };
            }

            Pulls++;

            var since = Query(request.RequestUri, "since");
            PulledFrom.Add(since);

            if (since is not null && RejectCursorWith is { } code)
            {
                return Problem(HttpStatusCode.BadRequest, code);
            }

            var start = since is null ? 0 : int.Parse(since[CursorPrefix.Length..], CultureInfo.InvariantCulture);
            var page = _feed.Skip(start).Take(PageSize).ToList();
            var next = start + page.Count;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new PullTasksResponse(
                    page,
                    CursorPrefix + next.ToString(CultureInfo.InvariantCulture),
                    HasMore: next < _feed.Count))
            };
        }
    }

    private static string? Query(Uri? uri, string name)
    {
        var query = uri?.Query.TrimStart('?') ?? string.Empty;

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts[0] == name) return Uri.UnescapeDataString(parts.Length > 1 ? parts[1] : string.Empty);
        }

        return null;
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string code) => new(status)
    {
        Content = new StringContent(
            $$"""{"title":"Refused","status":{{(int)status}},"detail":"Scripted {{code}}.","code":"{{code}}"}""",
            Encoding.UTF8,
            "application/problem+json")
    };
}

/// <summary>Tasks as a test writes them: a title and only the fields it is about.</summary>
internal static class TestTasks
{
    public static TaskChange Task(
        string title,
        DateTimeOffset updatedAt,
        DateOnly? inMyDayOn = null,
        DateOnly? dueOn = null,
        string status = "ready",
        string priority = "medium",
        string type = "task",
        Guid? id = null,
        DateTimeOffset? deletedAt = null,
        string contentMd = "",
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<string>? repoIds = null,
        IReadOnlyList<SubItemPayload>? subItems = null) =>
        new(
            id ?? Guid.CreateVersion7(),
            updatedAt,
            deletedAt,
            new TaskPayload(
                title, contentMd, type, status, priority, 0, null, updatedAt, null, null,
                dueOn, null, null, inMyDayOn, null, null, null, null, null,
                tags ?? [], repoIds ?? [], [], subItems ?? [], [], []));
}
