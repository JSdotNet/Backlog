using System.Net;
using System.Text.Json;

using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Infrastructure.SpecManager.Questions;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// The other spec-manager notifications over the stub installation: the connector's scenario
/// signed in a day before the clock's noon on 5 October 2026 — so the first sign-in moment is
/// noon on the 4th — the person's unread notifications and the products answered from
/// fixtures, and the ledger of notifications.
/// </summary>
internal sealed class NotificationScenario : IDisposable
{
    public const string NotificationsPath = "/api/notifications";
    public const string ProductsPath = "/api/products";

    public static readonly Guid Mention = new("50000000-0000-0000-0000-000000000001");
    public static readonly Guid Reply = new("50000000-0000-0000-0000-000000000002");
    public static readonly Guid Question = new("50000000-0000-0000-0000-000000000003");
    public static readonly Guid BeforeFirstSignIn = new("50000000-0000-0000-0000-000000000004");

    /// <summary>A notification the fixture does not hold: read in spec-manager.</summary>
    public static readonly Guid ReadElsewhere = new("50000000-0000-0000-0000-000000000009");

    public static readonly Guid Billing = new("10000000-0000-0000-0000-000000000001");
    public static readonly Guid Demo = new("10000000-0000-0000-0000-000000000002");

    public NotificationScenario()
    {
        Connector.SignedIn();
        Unread("notifications-unread.json");
        Connector.Server.Json(HttpMethod.Get, ProductsPath, "products.json");

        Sync = new SpecManagerNotificationSync(
            Connector.Client,
            Connector.SignIn,
            Ledger,
            Ledger,
            defaultInstallation: false);
    }

    public ConnectorScenario Connector { get; } = new();

    public NotificationLedger Ledger { get; } = new();

    public SpecManagerNotificationSync Sync { get; }

    public string Root => Connector.Options.Root;

    public Guid IdOf(Guid notificationId) => SpecManagerQuestionIds.ForNotification(Root, notificationId);

    /// <summary>Answers the unread notifications from a fixture.</summary>
    public void Unread(string fixture) =>
        Connector.Server.Json(HttpMethod.Get, NotificationsPath, fixture);

    /// <summary>Answers the unread notifications with the fixture's list changed by
    /// <paramref name="change"/>, and <paramref name="unread"/> as the count of all unread —
    /// the list's own length when null.</summary>
    public void Unread(Func<IReadOnlyList<SpecManagerNotificationDto>, IEnumerable<SpecManagerNotificationDto>> change, int? unread = null)
    {
        var fixture = JsonSerializer.Deserialize<NotificationsResponse>(Fixtures.Read("notifications-unread.json"), SpecManagerJson.Options)!;
        var list = change(fixture.Notifications!).ToList();
        var json = JsonSerializer.Serialize(new NotificationsResponse(unread ?? list.Count, list), SpecManagerJson.Options);

        Connector.Server.Route(HttpMethod.Get, NotificationsPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.OK, json));
    }

    /// <summary>One notification of the fixture, by id.</summary>
    public static SpecManagerNotificationDto Fixture(Guid id) =>
        JsonSerializer.Deserialize<NotificationsResponse>(Fixtures.Read("notifications-unread.json"), SpecManagerJson.Options)!
            .Notifications!.Single(notification => notification.Id == id);

    public static string ReadPath(Guid notificationId) => $"{NotificationsPath}/{notificationId:D}/read";

    /// <summary>Answers the read of <paramref name="notificationId"/> with
    /// <paramref name="status"/>.</summary>
    public void ReadAnswers(Guid notificationId, HttpStatusCode status) =>
        Connector.Server.Route(HttpMethod.Post, ReadPath(notificationId), (_, _) => new HttpResponseMessage(status));

    /// <summary>The reads sent for <paramref name="notificationId"/>.</summary>
    public IReadOnlyList<RecordedRequest> ReadsOf(Guid notificationId) =>
        [.. Connector.Server.To(ReadPath(notificationId)).Where(request => request.Method == HttpMethod.Post)];

    public void Dispose()
    {
        Sync.Dispose();
        Connector.Dispose();
    }
}
