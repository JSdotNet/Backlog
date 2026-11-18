using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;

using Backlog.Infrastructure.Sync.KnowledgeEvents;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>How the scripted service answers the knowledge event feed.</summary>
internal enum EventFeedState
{
    /// <summary>Pages of what was published.</summary>
    Answering,

    /// <summary>A service deployed before the route: a bare 404, no problem body.</summary>
    NotServedYet,

    /// <summary>The cursor the phone holds is refused once as expired.</summary>
    CursorExpiredOnce,

    /// <summary>A 404 that names a problem: something else went wrong.</summary>
    CodedNotFound,
}

/// <summary>
/// The sync service's knowledge event feed, scripted: what desktops pushed, in the order the
/// service stored it, handed out a page at a time from an integer cursor.
/// </summary>
internal sealed class ScriptedEventService
{
    private readonly Lock _lock = new();
    private readonly List<KnowledgeEventChangeRecord> _feed = [];

    public EventFeedState State { get; set; } = EventFeedState.Answering;

    public int PageSize { get; set; } = 100;

    public int Pulls { get; private set; }

    public List<string?> CursorsAsked { get; } = [];

    /// <summary>Stores an event as a desktop pushed it.</summary>
    public void Publish(KnowledgeEventReplicaDto knowledgeEvent) => Store(KnowledgeEventDocuments.ToChange(knowledgeEvent));

    /// <summary>Stores a series as a desktop pushed it.</summary>
    public void Publish(EventSeriesReplicaDto series) => Store(KnowledgeEventDocuments.ToChange(series));

    /// <summary>Stores any change, readable or not.</summary>
    public void Store(KnowledgeEventChange change)
    {
        lock (_lock) _feed.Add(new KnowledgeEventChangeRecord(change, Guid.NewGuid(), _feed.Count + 1));
    }

    public CloudSyncClient Client() => TestCloudSync.Client(Answer);

    /// <summary>Answers a request the whole-app host routed here.</summary>
    public Task<HttpResponseMessage> AnswerAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(Answer(request));

    private HttpResponseMessage Answer(HttpRequestMessage request)
    {
        if (request.RequestUri!.AbsolutePath != SyncRoutes.Absolute(SyncRoutes.KnowledgeEvents) || request.Method != HttpMethod.Get)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        lock (_lock)
        {
            Pulls++;
            var since = Query(request.RequestUri, "since");
            CursorsAsked.Add(since);

            switch (State)
            {
                case EventFeedState.NotServedYet:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);

                case EventFeedState.CodedNotFound:
                    return Problem(HttpStatusCode.NotFound, "sync.device_not_found");

                case EventFeedState.CursorExpiredOnce when since is not null:
                    State = EventFeedState.Answering;
                    return Problem(HttpStatusCode.BadRequest, SyncErrorCodes.SyncCursorExpired);
            }

            var from = since is null ? 0 : int.Parse(since.Split(':')[1], CultureInfo.InvariantCulture);
            var page = _feed.Skip(from).Take(PageSize).ToList();
            var next = from + page.Count;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new PullKnowledgeEventsResponse(page, $"c:{next}", next < _feed.Count)),
            };
        }
    }

    private static string? Query(Uri uri, string name) =>
        uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair[0] == name)
            .Select(pair => Uri.UnescapeDataString(pair[1]))
            .FirstOrDefault();

    private static HttpResponseMessage Problem(HttpStatusCode status, string code) => new(status)
    {
        Content = new StringContent($$"""{"title":"Refused","status":{{(int)status}},"code":"{{code}}"}""", Encoding.UTF8, "application/problem+json"),
    };
}

/// <summary>Knowledge events and series as a desktop pushes them.</summary>
internal static class TestEvents
{
    public static KnowledgeEventReplicaDto Event(
        string title,
        DateTimeOffset updatedAt,
        DateOnly? start = null,
        DateOnly? end = null,
        KnowledgeEventState state = KnowledgeEventState.Going,
        Guid? id = null,
        Guid? seriesId = null,
        IReadOnlyList<EventNoteLinkReplicaDto>? notes = null) =>
        new(
            id ?? Guid.NewGuid(),
            title,
            state,
            start is { } from ? new EventDateReplicaDto(from, null, null) : null,
            end is { } to ? new EventDateReplicaDto(to, null, null) : null,
            start is null ? null : DateSource.Typed,
            "Kinepolis Utrecht",
            Online: false,
            "https://techorama.nl/2026",
            [new EventKeyDateReplicaDto(new DateOnly(2026, 6, 30), "Early bird ends")],
            "Aspire in production",
            seriesId,
            notes ?? [],
            state == KnowledgeEventState.Skipped ? "Clashes with the release" : null,
            TicketState.Bought,
            null,
            [],
            null,
            updatedAt);

    public static EventSeriesReplicaDto Series(string name, DateTimeOffset updatedAt, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), name, "Techorama BV", "https://techorama.nl", updatedAt);
}
