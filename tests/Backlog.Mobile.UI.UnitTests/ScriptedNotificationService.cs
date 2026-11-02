using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Backlog.Infrastructure.Sync.Notifications;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The sync service's notification replica as the phone meets it: the owner desktops' feed,
/// paged by a cursor that is just a position, and the replies the phone posts, kept in arrival
/// order. A test plays the owner by publishing a notification again with its verdict.
/// </summary>
internal sealed class ScriptedNotificationService
{
    private const string CursorPrefix = "pos:";

    private readonly Lock _lock = new();
    private readonly List<NotificationChangeRecord> _feed = [];
    private readonly List<NotificationReplyPost> _replies = [];
    private long _serverTimestamp;

    public InboxServiceState State { get; set; } = InboxServiceState.Answering;

    public int Pulls { get; private set; }

    /// <summary>Every reply post that reached the service, repeats included.</summary>
    public IReadOnlyList<NotificationReplyPost> Replies
    {
        get
        {
            lock (_lock) return [.. _replies];
        }
    }

    /// <summary>An owner desktop pushes <paramref name="notification"/>, stamped
    /// <paramref name="at"/> on its clock: on the feed from the next pull.</summary>
    public void Publish(
        NotificationSnapshot notification,
        DateTimeOffset at,
        string? ownerName = "WORKSTATION-01",
        IReadOnlyList<NotificationReplyRejection>? rejected = null)
    {
        var change = NotificationPayloads.ToChange(notification, rejected ?? [], at, ownerName);
        lock (_lock) _feed.Add(new NotificationChangeRecord(change, Guid.NewGuid(), ++_serverTimestamp));
    }

    public async Task<HttpResponseMessage> AnswerAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        NotificationReplyPost? reply = null;
        if (request.Method == HttpMethod.Post && request.Content is not null)
        {
            reply = await request.Content.ReadFromJsonAsync<NotificationReplyPost>(cancellationToken);
        }

        lock (_lock)
        {
            if (State == InboxServiceState.Unreachable)
            {
                throw new HttpRequestException("No such host is known. (sync.test:443)");
            }

            if (reply is not null)
            {
                var stored = _replies.All(held => held.Id != reply.Id);
                _replies.Add(reply);

                return new HttpResponseMessage(stored ? HttpStatusCode.Created : HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new PostNotificationReplyResponse(reply.Id, stored))
                };
            }

            Pulls++;

            var since = Query(request.RequestUri, "since");
            var start = since is null ? 0 : int.Parse(since[CursorPrefix.Length..], CultureInfo.InvariantCulture);
            var page = _feed.Skip(start).ToList();
            var next = start + page.Count;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new PullNotificationsResponse(page, CursorPrefix + next.ToString(CultureInfo.InvariantCulture), HasMore: false))
            };
        }
    }

    private static string? Query(Uri? uri, string name)
    {
        foreach (var pair in (uri?.Query.TrimStart('?') ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts[0] == name) return Uri.UnescapeDataString(parts.Length > 1 ? parts[1] : string.Empty);
        }

        return null;
    }
}

/// <summary>Notifications as a test writes them: a question or an alert and only the fields it
/// is about.</summary>
internal static class TestNotifications
{
    public static readonly Guid Owner = Guid.Parse("d0000000-0000-4000-8000-0000000000aa");

    public static NotificationSnapshot Question(
        string title,
        DateTimeOffset createdAt,
        AnswerSpec? spec = null,
        Guid? id = null,
        NotificationSeverity severity = NotificationSeverity.Warning,
        string body = "",
        string summary = "") =>
        new(
            id ?? Guid.CreateVersion7(),
            Owner,
            new NotificationSource("mcp", "ask-1"),
            new NotificationSubject("sessions", "session", "sess-91"),
            NotificationKind.Question,
            severity,
            title,
            summary,
            body,
            spec ?? YesNo,
            DeepLink: null,
            IsPrivate: false,
            createdAt,
            NotificationState.Open,
            SettledAt: null,
            CloseReason: null,
            Answer: null);

    public static NotificationSnapshot Alert(string title, DateTimeOffset createdAt, Guid? id = null) =>
        Question(title, createdAt, id: id, severity: NotificationSeverity.Info) with { Kind = NotificationKind.Alert, AnswerSpec = null, Subject = null };

    public static readonly AnswerSpec YesNo = new(
        [new AnswerChoice("yes", "Yes, run it", AnswerChoiceStyle.Primary), new AnswerChoice("no", "Not now")]);

    public static readonly AnswerSpec Journeys = new(
        [new AnswerChoice("pair", "Pair phone"), new AnswerChoice("capture", "Capture note"), new AnswerChoice("sync", "Sync")],
        AllowMultiple: true,
        MinChoices: 1,
        MaxChoices: 2);

    public static readonly AnswerSpec ReleaseNote = new([], Open: new OpenAnswer("Write an answer…", 200));

    /// <summary>A single-choice question whose Revise takes the person's words, as the Personal
    /// Validation question asks.</summary>
    public static AnswerSpec Validation(OpenAnswer? open = null) => new(
        [
            new AnswerChoice("approve", "Approve", AnswerChoiceStyle.Primary),
            new AnswerChoice("revise", "Revise", TakesText: true, Placeholder: "What should change?"),
            new AnswerChoice("reject", "Reject", AnswerChoiceStyle.Destructive)
        ],
        Open: open);

    /// <summary>The owner accepted <paramref name="replyId"/> from <paramref name="fromDevice"/>.</summary>
    public static NotificationSnapshot Answered(NotificationSnapshot question, Guid replyId, Guid fromDevice, DateTimeOffset at, params string[] choiceIds) =>
        question with
        {
            State = NotificationState.Answered,
            SettledAt = at,
            Answer = new NotificationAnswer(question.Id, question.Source, replyId, fromDevice, choiceIds, null, at)
        };

    public static NotificationReplyRejection Rejected(Guid replyId, Guid fromDevice, string message, DateTimeOffset at) =>
        new(replyId, fromDevice, NotificationReplyReasons.Invalid, "reply.invalid", message, at);

    public static JsonElement Raw(NotificationSnapshot snapshot) =>
        NotificationPayloads.ToChange(snapshot, [], snapshot.CreatedAt).Notification;
}
