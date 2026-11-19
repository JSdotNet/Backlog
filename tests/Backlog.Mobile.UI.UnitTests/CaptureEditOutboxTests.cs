using System.Net;
using System.Net.Http.Json;
using System.Text;

using Backlog.Mobile.UI.Outbox;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The phone's edit and withdrawal of a capture on their way to the service:
/// what each sends, and how each answer is counted — above all that a capture
/// the desktop already decided is delivered, with a word for the person, rather
/// than stuck, and that a service too old to know the route is waited for
/// rather than counted as delivered.
/// </summary>
public sealed class CaptureEditOutboxTests
{
    private static readonly DateTimeOffset Made = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // --- The client ------------------------------------------------------------

    [Fact]
    public async Task The_client_puts_the_new_title_and_body_on_the_captures_route()
    {
        HttpRequestMessage? sent = null;
        CaptureEditRequest? body = null;
        var capture = Guid.CreateVersion7();
        var sync = TestCloudSync.Client(request =>
        {
            sent = request;
            body = request.Content!.ReadFromJsonAsync<CaptureEditRequest>().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var answer = await sync.PutCaptureAsync(capture, new CaptureEditRequest("Reworded", "More."), Cancellation);

        Assert.Equal((HttpStatusCode.OK, (string?)null, (string?)null), answer);
        Assert.Equal(HttpMethod.Put, sent!.Method);
        Assert.Equal(SyncRoutes.EditInboxItemFor(capture), sent.RequestUri!.AbsolutePath);
        Assert.Equal(new CaptureEditRequest("Reworded", "More."), body);
    }

    [Fact]
    public async Task The_client_acknowledges_on_the_ack_route_and_reports_what_was_answered()
    {
        HttpRequestMessage? sent = null;
        var capture = Guid.CreateVersion7();
        var sync = TestCloudSync.Client(request =>
        {
            sent = request;
            return Problem(HttpStatusCode.NotFound, SyncErrorCodes.InboxItemNotFound, "No capture with that id is waiting.");
        });

        var answer = await sync.AcknowledgeAsync(capture, Cancellation);

        Assert.Equal((HttpStatusCode.NotFound, "No capture with that id is waiting.", SyncErrorCodes.InboxItemNotFound), answer);
        Assert.Equal(HttpMethod.Post, sent!.Method);
        Assert.Equal(SyncRoutes.AcknowledgeInboxItemFor(capture), sent.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task The_client_reports_a_bare_refusal_with_no_code()
    {
        var sync = TestCloudSync.Client(_ => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));

        var answer = await sync.PutCaptureAsync(Guid.CreateVersion7(), new CaptureEditRequest("Reworded"), Cancellation);

        Assert.Equal((HttpStatusCode.MethodNotAllowed, (string?)null, (string?)null), answer);
    }

    // --- The kinds ---------------------------------------------------------------

    public static TheoryData<string> Kinds => [CaptureEditOutboxKind.Token, CaptureWithdrawOutboxKind.Token];

    /// <summary>The stored tokens are permanent: an entry written by this build
    /// is read by every later one under the same word.</summary>
    [Fact]
    public void The_kind_tokens_are_the_stored_words()
    {
        Assert.Equal("capture-edit", CaptureEditOutboxKind.Token);
        Assert.Equal("capture-withdraw", CaptureWithdrawOutboxKind.Token);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_taken_change_is_delivered_with_no_word_for_the_person(string kind)
    {
        var notices = new CaptureDealtWithNotices();
        var capture = Guid.CreateVersion7();

        var delivery = await Send(kind, capture, notices, _ => new HttpResponseMessage(kind == CaptureEditOutboxKind.Token ? HttpStatusCode.OK : HttpStatusCode.NoContent));

        Assert.Equal(OutboxDeliveryKind.Delivered, delivery.Kind);
        Assert.Empty(notices.Captures);
    }

    /// <summary>The desktop decided first. Nothing is left to change, so the entry
    /// is done — never parked as "tap to retry" — and the person is told once.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_capture_the_desktop_already_decided_is_delivered_and_the_person_is_told(string kind)
    {
        var notices = new CaptureDealtWithNotices();
        var told = 0;
        notices.Changed += () => told++;
        var capture = Guid.CreateVersion7();

        var delivery = await Send(kind, capture, notices, _ => Problem(HttpStatusCode.NotFound, SyncErrorCodes.InboxItemNotFound, "No capture with that id is waiting."));

        Assert.Equal(OutboxDeliveryKind.Delivered, delivery.Kind);
        Assert.Equal([capture], notices.Captures);
        Assert.Equal(1, told);
    }

    /// <summary>A service older than the phone has no edit route. That is not the
    /// capture's answer, so the edit is never counted as delivered; it is set aside
    /// for the person to retry once the service is updated, rather than held at the
    /// head of the queue, where it would keep every later capture on the phone.</summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    public async Task An_edit_route_the_service_lacks_is_set_aside_not_delivered(HttpStatusCode status)
    {
        var notices = new CaptureDealtWithNotices();

        var delivery = await Send(CaptureEditOutboxKind.Token, Guid.CreateVersion7(), notices, _ => new HttpResponseMessage(status));

        Assert.Equal(OutboxDeliveryKind.Refused, delivery.Kind);
        Assert.Equal("The cloud service cannot change a capture yet.", delivery.Error);
        Assert.Empty(notices.Captures);
    }

    /// <summary>The acknowledgement route is as old as captures, so a bare 404 or
    /// 405 there is no older service but a request that went astray: held, never
    /// counted as delivered.</summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    public async Task A_bare_refusal_of_a_withdrawal_is_held_not_delivered(HttpStatusCode status)
    {
        var notices = new CaptureDealtWithNotices();

        var delivery = await Send(CaptureWithdrawOutboxKind.Token, Guid.CreateVersion7(), notices, _ => new HttpResponseMessage(status));

        Assert.Equal(OutboxDeliveryKind.Held, delivery.Kind);
        Assert.Empty(notices.Captures);
    }

    [Fact]
    public async Task An_edit_the_service_finds_out_of_bounds_is_refused()
    {
        var delivery = await Send(
            CaptureEditOutboxKind.Token, Guid.CreateVersion7(), new CaptureDealtWithNotices(),
            _ => Problem(HttpStatusCode.BadRequest, SyncErrorCodes.CaptureInvalid, "A capture needs a title."));

        Assert.Equal(OutboxDeliveryKind.Refused, delivery.Kind);
        Assert.Equal("A capture needs a title.", delivery.Error);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task No_answer_at_all_is_tried_again(string kind)
    {
        var delivery = await Send(kind, Guid.CreateVersion7(), new CaptureDealtWithNotices(), _ => throw new HttpRequestException("No network."));

        Assert.Equal(OutboxDeliveryKind.Transient, delivery.Kind);
    }

    /// <summary>The entry has its own id; the capture it changes travels in the
    /// payload, so two edits of one capture are two entries.</summary>
    [Fact]
    public async Task An_edit_entry_sends_the_capture_named_in_its_payload_not_its_own_id()
    {
        string? path = null;
        var capture = Guid.CreateVersion7();
        var sync = TestCloudSync.Client(request => { path = request.RequestUri!.AbsolutePath; return new HttpResponseMessage(HttpStatusCode.OK); });
        var entry = new OutboxEntry(Guid.CreateVersion7(), CaptureEditOutboxKind.Token, CaptureEditOutboxKind.Write(new CaptureEdit(capture, "Reworded", null)), 0, null, Made);

        await new CaptureEditOutboxKind(sync, new CaptureDealtWithNotices()).SendAsync(entry, Cancellation);

        Assert.Equal(SyncRoutes.EditInboxItemFor(capture), path);
        Assert.NotEqual(entry.Id, capture);
    }

    private static Task<OutboxDelivery> Send(
        string kind,
        Guid capture,
        CaptureDealtWithNotices notices,
        Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        var sync = TestCloudSync.Client(answer);

        if (kind == CaptureEditOutboxKind.Token)
        {
            var entry = new OutboxEntry(Guid.CreateVersion7(), kind, CaptureEditOutboxKind.Write(new CaptureEdit(capture, "Reworded", "More.")), 0, null, Made);
            return new CaptureEditOutboxKind(sync, notices).SendAsync(entry, Cancellation);
        }

        var withdrawal = new OutboxEntry(Guid.CreateVersion7(), kind, CaptureWithdrawOutboxKind.Write(new CaptureWithdrawal(capture)), 0, null, Made);
        return new CaptureWithdrawOutboxKind(sync, notices).SendAsync(withdrawal, Cancellation);
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) => new(status)
    {
        Content = new StringContent($$"""{"status":{{(int)status}},"code":"{{code}}","detail":"{{detail}}"}""", Encoding.UTF8, "application/problem+json"),
    };
}
