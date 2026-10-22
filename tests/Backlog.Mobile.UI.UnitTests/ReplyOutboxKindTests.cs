using System.Net;
using System.Net.Http.Json;
using System.Text;

using Backlog.Mobile.UI.Outbox;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The phone's reply to a question as an outbox entry (<c>.devbook/domain/notifications/domain.md#reply</c>):
/// one post of the reply the phone minted to the replies route, read the way every kind reads
/// an answer — a network that is not there is worth retrying, a refusal is not.
/// </summary>
public sealed class ReplyOutboxKindTests
{
    private static readonly NotificationReplyPost Reply = new(
        Guid.Parse("01920000-0000-7000-8000-000000000001"),
        Guid.Parse("01920000-0000-7000-8000-000000000002"),
        TestNotifications.Owner,
        ["yes"],
        null,
        new DateTimeOffset(2026, 10, 21, 9, 43, 0, TimeSpan.Zero));

    private static OutboxEntry Entry() =>
        new(Reply.Id, ReplyOutboxKind.Token, ReplyOutboxKind.Write(Reply), 0, null, Reply.RepliedAt);

    [Fact]
    public void The_token_is_the_stored_kind() => Assert.Equal("notification-reply", new ReplyOutboxKind(Client(_ => new HttpResponseMessage())).Kind);

    [Fact]
    public void An_entry_carries_the_reply_whole()
    {
        var read = ReplyOutboxKind.Read(Entry());

        Assert.Equal((Reply.Id, Reply.NotificationId, Reply.OwnerInstanceId, Reply.Text, Reply.RepliedAt), (read.Id, read.NotificationId, read.OwnerInstanceId, read.Text, read.RepliedAt));
        Assert.Equal(Reply.ChoiceIds, read.ChoiceIds);
    }

    [Fact]
    public async Task One_attempt_posts_the_reply_to_the_replies_route()
    {
        HttpRequestMessage? sent = null;
        NotificationReplyPost? body = null;
        var kind = new ReplyOutboxKind(Client(request =>
        {
            sent = request;
            body = request.Content!.ReadFromJsonAsync<NotificationReplyPost>().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new PostNotificationReplyResponse(Reply.Id, true)) };
        }));

        var delivery = await kind.SendAsync(Entry(), TestContext.Current.CancellationToken);

        Assert.Equal(OutboxDeliveryKind.Delivered, delivery.Kind);
        Assert.Equal(HttpMethod.Post, sent!.Method);
        Assert.Equal("/api/sync/notifications/replies", sent.RequestUri!.AbsolutePath);
        Assert.Equal(Reply.Id, body!.Id);
        Assert.Equal(["yes"], body.ChoiceIds);
        Assert.Equal(TestNotifications.Owner, body.OwnerInstanceId);
    }

    [Fact]
    public async Task No_network_is_worth_trying_again()
    {
        var kind = new ReplyOutboxKind(Client(_ => throw new HttpRequestException("No such host is known.")));

        Assert.Equal(OutboxDeliveryKind.Transient, (await kind.SendAsync(Entry(), TestContext.Current.CancellationToken)).Kind);
    }

    [Fact]
    public async Task A_refused_reply_is_set_aside_with_the_services_reason()
    {
        var kind = new ReplyOutboxKind(Client(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"title":"Refused","status":400,"detail":"A reply picks at most 6 choices.","code":"notification.reply_invalid"}""", Encoding.UTF8, "application/problem+json")
        }));

        var delivery = await kind.SendAsync(Entry(), TestContext.Current.CancellationToken);

        Assert.Equal(OutboxDeliveryKind.Refused, delivery.Kind);
        Assert.Equal("A reply picks at most 6 choices.", delivery.Error);
    }

    private static CloudSyncClient Client(Func<HttpRequestMessage, HttpResponseMessage> answer) =>
        new(new HttpClient(new Handler(answer)) { BaseAddress = new Uri("https://sync.test") });

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }
}
