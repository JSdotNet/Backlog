using Backlog.Modules.Sessions.Abstractions;
using Backlog.Tests;

using Microsoft.Extensions.Time.Testing;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The answer delivery tools beside the request tools (local ADR 0041, Amends): a claim takes
/// the 2-minute lease or reports the delivery taken, a mark moves a held delivery to Delivered
/// or gives it back, a lapsed claim reads as Pending again, and the list answers what a
/// restarted Manager may still hold — each delivery carrying the desktop session it goes to.
/// </summary>
public class DeliveryToolsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Noon);
    private readonly HeldWaitingQuestions _questions = new();
    private readonly SessionRequestTools _tools;

    public DeliveryToolsTests()
    {
        var sessions = new FixedSessionSource(
            FixedSessionSource.Claude("cli-1", "local_1"),
            FixedSessionSource.Claude("cli-gone", "local_9", archived: true));
        _tools = new SessionRequestTools(new InMemorySessionRequestStore(_clock), _clock, _questions, sessions);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WaitingQuestion Delivery(string session = "cli-1") =>
        _questions.AddPendingDelivery(session, "Which database?", "Postgres", _clock.GetUtcNow());

    [Fact]
    public async Task A_claim_takes_the_delivery_under_a_two_minute_lease_and_carries_the_message_parts()
    {
        var delivery = Delivery();

        var answer = await _tools.ClaimDeliveryAsync(delivery.Id, " manager-1 ", Ct);

        Assert.True(answer.Claimed);
        Assert.Null(answer.Refusal);
        Assert.Equal("delivery", answer.Delivery.Kind);
        Assert.Equal("claimed", answer.Delivery.State);
        Assert.Equal("manager-1", answer.Delivery.ClaimedBy);
        Assert.Equal(Noon + TimeSpan.FromMinutes(2), answer.Delivery.LeaseEndsAt);
        Assert.Equal("cli-1", answer.Delivery.SessionId);
        Assert.Equal("local_1", answer.Delivery.DesktopSessionId);
        Assert.Equal("Which database?", answer.Delivery.Question);
        Assert.Equal("Postgres", answer.Delivery.Answer);
    }

    [Fact]
    public async Task A_claim_on_a_delivery_another_dispatcher_holds_reports_it_taken()
    {
        var delivery = Delivery();
        await _tools.ClaimDeliveryAsync(delivery.Id, "manager-1", Ct);

        var answer = await _tools.ClaimDeliveryAsync(delivery.Id, "manager-2", Ct);

        Assert.False(answer.Claimed);
        Assert.Contains("Claimed", answer.Refusal, StringComparison.Ordinal);
        Assert.Equal("manager-1", answer.Delivery.ClaimedBy);
    }

    [Fact]
    public async Task A_repeated_claim_by_the_holder_reports_it_held_without_renewing_the_lease()
    {
        var delivery = Delivery();
        await _tools.ClaimDeliveryAsync(delivery.Id, "manager-1", Ct);
        _clock.Advance(TimeSpan.FromSeconds(30));

        var answer = await _tools.ClaimDeliveryAsync(delivery.Id, "manager-1", Ct);

        Assert.True(answer.Claimed);
        Assert.Equal(Noon + TimeSpan.FromMinutes(2), answer.Delivery.LeaseEndsAt);
    }

    [Fact]
    public async Task Marking_it_delivered_is_final()
    {
        var delivery = Delivery();
        await _tools.ClaimDeliveryAsync(delivery.Id, "manager-1", Ct);

        var marked = await _tools.MarkDeliveryAsync(delivery.Id, "Delivered", "manager-1", Ct);
        var again = await _tools.ClaimDeliveryAsync(delivery.Id, "manager-1", Ct);

        Assert.True(marked.Marked);
        Assert.Equal("delivered", marked.Outcome);
        Assert.Equal("delivered", marked.Delivery.State);
        Assert.False(again.Claimed);
        Assert.Empty((await _tools.ListDeliveriesAsync(cancellationToken: Ct)).Deliveries);
    }

    [Fact]
    public async Task Releasing_it_gives_the_claim_back_as_pending()
    {
        var delivery = Delivery();
        await _tools.ClaimDeliveryAsync(delivery.Id, "manager-1", Ct);

        var marked = await _tools.MarkDeliveryAsync(delivery.Id, "released", "manager-1", Ct);

        Assert.True(marked.Marked);
        Assert.Equal("pending", marked.Delivery.State);
        Assert.Null(marked.Delivery.ClaimedBy);
    }

    [Fact]
    public async Task A_lapsed_claim_reads_as_pending_and_its_holder_can_no_longer_release_it()
    {
        var delivery = Delivery();
        await _tools.ClaimDeliveryAsync(delivery.Id, "manager-1", Ct);
        _clock.Advance(TimeSpan.FromMinutes(2));

        var listed = await _tools.ListDeliveriesAsync("pending", Ct);
        var late = await _tools.MarkDeliveryAsync(delivery.Id, "released", "manager-1", Ct);

        Assert.Equal(delivery.Id, Assert.Single(listed.Deliveries).Id);
        Assert.False(late.Marked);
        Assert.Contains("lease has run out", late.Refusal, StringComparison.Ordinal);
        Assert.Equal("pending", late.Delivery.State);
    }

    [Fact]
    public async Task A_released_delivery_cannot_be_claimed_again_until_its_hold_passes()
    {
        var delivery = Delivery();
        await _tools.ClaimDeliveryAsync(delivery.Id, "manager-1", Ct);
        await _tools.MarkDeliveryAsync(delivery.Id, "released", "manager-1", Ct);

        var early = await _tools.ClaimDeliveryAsync(delivery.Id, "manager-1", Ct);
        _clock.Advance(TimeSpan.FromMinutes(2));
        var later = await _tools.ClaimDeliveryAsync(delivery.Id, "manager-1", Ct);

        Assert.False(early.Claimed);
        Assert.Contains("held back", early.Refusal, StringComparison.Ordinal);
        Assert.True(later.Claimed);
    }

    [Fact]
    public async Task Only_the_holder_marks_a_delivery()
    {
        var delivery = Delivery();
        await _tools.ClaimDeliveryAsync(delivery.Id, "manager-1", Ct);

        var other = await _tools.MarkDeliveryAsync(delivery.Id, "delivered", "manager-2", Ct);

        Assert.False(other.Marked);
        Assert.Equal("claimed", other.Delivery.State);
    }

    [Fact]
    public async Task An_outcome_other_than_delivered_or_released_is_refused()
    {
        var delivery = Delivery();

        var refusal = await Assert.ThrowsAsync<McpException>(() => _tools.MarkDeliveryAsync(delivery.Id, "started", "manager-1", Ct));

        Assert.Contains("delivery.outcome", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_delivery_is_not_found()
    {
        var refusal = await Assert.ThrowsAsync<McpException>(() => _tools.ClaimDeliveryAsync(Guid.NewGuid(), "manager-1", Ct));

        Assert.Contains("delivery.not_found", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_list_narrows_to_one_state_and_names_no_desktop_session_for_an_ended_one()
    {
        var held = Delivery();
        var gone = Delivery("cli-gone");
        await _tools.ClaimDeliveryAsync(held.Id, "manager-1", Ct);

        var claimed = await _tools.ListDeliveriesAsync("claimed", Ct);
        var pending = await _tools.ListDeliveriesAsync("pending", Ct);
        var all = await _tools.ListDeliveriesAsync(cancellationToken: Ct);

        Assert.Equal("claimed", claimed.State);
        Assert.Equal(held.Id, Assert.Single(claimed.Deliveries).Id);
        var stranded = Assert.Single(pending.Deliveries);
        Assert.Equal(gone.Id, stranded.Id);
        Assert.Null(stranded.DesktopSessionId);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task A_state_the_list_does_not_know_is_refused()
    {
        var refusal = await Assert.ThrowsAsync<McpException>(() => _tools.ListDeliveriesAsync("delivered", Ct));

        Assert.Contains("delivery.state", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_relay_the_delivery_tools_refuse()
    {
        var tools = new SessionRequestTools(new InMemorySessionRequestStore(_clock), _clock);

        var refusal = await Assert.ThrowsAsync<McpException>(() => tools.ListDeliveriesAsync(cancellationToken: Ct));

        Assert.Contains("delivery.not_available", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_delivery_tools_sit_in_the_session_request_group()
    {
        Assert.Contains(SessionRequestTools.ClaimDelivery, BacklogMcpTools.SessionRequests.ToolNames);
        Assert.Contains(SessionRequestTools.MarkDelivery, BacklogMcpTools.SessionRequests.ToolNames);
        Assert.Contains(SessionRequestTools.ListDeliveries, BacklogMcpTools.SessionRequests.ToolNames);
    }

    /// <summary>The Manager's capacity count leaves out a dispatched session waiting on a
    /// question: the session list says which desktop session reads Waiting.</summary>
    [Fact]
    public async Task The_session_list_names_each_sessions_desktop_id_beside_its_waiting_state()
    {
        var waiting = FixedSessionSource.Claude("cli-1", "local_1", state: AgentSessionState.Waiting);
        var working = FixedSessionSource.Claude("cli-2", "local_2", state: AgentSessionState.Running);
        var tools = new SessionTools(new FixedSessionSource(waiting, working), new FakeRepositoryDirectory());

        var answer = await tools.ListSessionsAsync(cancellationToken: Ct);

        Assert.Equal(["local_1:Waiting", "local_2:Running"], answer.Sessions.Select(session => $"{session.DesktopSessionId}:{session.State}"));
    }
}
