using Backlog.Modules.Sessions.Abstractions;
using Backlog.Tests;

using Microsoft.Extensions.Time.Testing;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The dispatcher tools of local ADR 0037 §4 over the queue: a claim takes the 2-minute
/// lease or reports the request taken, a mark moves a claimed request to Started or
/// back to Requested, and the list answers the queue for a dispatcher that restarts —
/// each answer carrying the brief the server built.
/// </summary>
public class SessionRequestToolsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Noon);
    private readonly InMemorySessionRequestStore _queue;
    private readonly SessionRequestTools _tools;

    public SessionRequestToolsTests()
    {
        _queue = new InMemorySessionRequestStore(_clock);
        _tools = new SessionRequestTools(_queue, _clock);
    }

    private SessionRequest Entry(Guid? entryId = null) =>
        _queue.Add(SessionRequests.ForEntry(entryId ?? Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", _clock.GetUtcNow()));

    private SessionRequest Plan(string tag) =>
        _queue.Add(SessionRequests.ForPlan(tag, "JSdotNet/Backlog", @"D:\Repos\Backlog", _clock.GetUtcNow()));

    [Fact]
    public async Task A_claim_takes_the_request_under_a_two_minute_lease_and_carries_the_brief()
    {
        var entryId = Guid.Parse("cec5cf9f-a7c2-4d55-b487-eb1a2f9ded45");
        var request = Entry(entryId);

        var answer = await _tools.ClaimSessionRequestAsync(request.Id, " manager-1 ", TestContext.Current.CancellationToken);

        Assert.True(answer.Claimed);
        Assert.Null(answer.Refusal);
        Assert.Equal("claimed", answer.Request.State);
        Assert.Equal("manager-1", answer.Request.ClaimedBy);
        Assert.Equal(Noon + TimeSpan.FromMinutes(2), answer.Request.LeaseEndsAt);
        Assert.Equal("entry", answer.Request.Kind);
        Assert.Equal(@"D:\Repos\Backlog", answer.Request.CloneFolder);
        Assert.Equal("/backlog-tools:run-plan-item entry `cec5cf9f-a7c2-4d55-b487-eb1a2f9ded45`:", answer.Request.Brief);
    }

    [Fact]
    public async Task A_claim_on_a_request_another_dispatcher_holds_reports_it_taken()
    {
        var request = Entry();
        await _tools.ClaimSessionRequestAsync(request.Id, "manager-1", TestContext.Current.CancellationToken);

        var answer = await _tools.ClaimSessionRequestAsync(request.Id, "manager-2", TestContext.Current.CancellationToken);

        Assert.False(answer.Claimed);
        Assert.Contains("Claimed", answer.Refusal, StringComparison.Ordinal);
        Assert.Equal("manager-1", answer.Request.ClaimedBy);
    }

    /// <summary>A dispatcher whose first answer was lost and that claims again already
    /// holds the request, and is told so rather than told it was taken.</summary>
    [Fact]
    public async Task A_repeated_claim_by_the_holder_reports_it_held_without_renewing_the_lease()
    {
        var request = Entry();
        await _tools.ClaimSessionRequestAsync(request.Id, "manager-1", TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(30));

        var answer = await _tools.ClaimSessionRequestAsync(request.Id, "manager-1", TestContext.Current.CancellationToken);

        Assert.True(answer.Claimed);
        Assert.Null(answer.Refusal);
        Assert.Equal(Noon + TimeSpan.FromMinutes(2), answer.Request.LeaseEndsAt);
    }

    [Fact]
    public async Task A_claim_whose_lease_ran_out_can_be_taken_by_another_dispatcher()
    {
        var request = Entry();
        await _tools.ClaimSessionRequestAsync(request.Id, "manager-1", TestContext.Current.CancellationToken);

        _clock.Advance(TimeSpan.FromMinutes(2));

        var listed = await _tools.ListSessionRequestsAsync("requested", TestContext.Current.CancellationToken);
        Assert.Equal(request.Id, Assert.Single(listed.Requests).Id);

        var answer = await _tools.ClaimSessionRequestAsync(request.Id, "manager-2", TestContext.Current.CancellationToken);

        Assert.True(answer.Claimed);
        Assert.Equal("manager-2", answer.Request.ClaimedBy);
    }

    [Fact]
    public async Task A_claim_on_an_unknown_request_is_refused_naming_it()
    {
        var id = Guid.NewGuid();

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            _tools.ClaimSessionRequestAsync(id, "manager-1", TestContext.Current.CancellationToken));

        Assert.Contains(id.ToString("D"), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_claim_without_a_dispatcher_is_refused()
    {
        var request = Entry();

        await Assert.ThrowsAsync<McpException>(() =>
            _tools.ClaimSessionRequestAsync(request.Id, "  ", TestContext.Current.CancellationToken));

        Assert.Equal(SessionRequestState.Requested, (await _queue.GetAsync(request.Id, TestContext.Current.CancellationToken))!.State);
    }

    [Fact]
    public async Task Marking_started_records_the_session_and_ends_the_lease()
    {
        var request = Entry();
        await _tools.ClaimSessionRequestAsync(request.Id, "manager-1", TestContext.Current.CancellationToken);

        var answer = await _tools.MarkSessionRequestAsync(request.Id, "Started", "manager-1", "session-a", TestContext.Current.CancellationToken);

        Assert.Equal("started", answer.Outcome);
        Assert.True(answer.Marked);
        Assert.Equal("started", answer.Request.State);
        Assert.Equal("session-a", answer.Request.SessionId);
        Assert.Null(answer.Request.LeaseEndsAt);
    }

    [Fact]
    public async Task Marking_started_without_a_session_id_is_refused_and_moves_nothing()
    {
        var request = Entry();
        await _tools.ClaimSessionRequestAsync(request.Id, "manager-1", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<McpException>(() =>
            _tools.MarkSessionRequestAsync(request.Id, "started", "manager-1", null, TestContext.Current.CancellationToken));

        Assert.Equal(SessionRequestState.Claimed, (await _queue.GetAsync(request.Id, TestContext.Current.CancellationToken))!.State);
    }

    [Fact]
    public async Task Marking_released_puts_the_request_back_to_requested()
    {
        var request = Entry();
        await _tools.ClaimSessionRequestAsync(request.Id, "manager-1", TestContext.Current.CancellationToken);

        var answer = await _tools.MarkSessionRequestAsync(request.Id, "released", "manager-1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(answer.Marked);
        Assert.Equal("requested", answer.Request.State);
        Assert.Null(answer.Request.ClaimedBy);
        Assert.Equal(request.Id, (await _queue.NextAsync(TestContext.Current.CancellationToken))!.Id);
    }

    [Fact]
    public async Task A_dispatcher_that_does_not_hold_the_claim_cannot_mark_it()
    {
        var request = Entry();
        await _tools.ClaimSessionRequestAsync(request.Id, "manager-1", TestContext.Current.CancellationToken);

        var answer = await _tools.MarkSessionRequestAsync(request.Id, "started", "manager-2", "session-b", TestContext.Current.CancellationToken);

        Assert.False(answer.Marked);
        Assert.Equal("Another dispatcher holds the claim.", answer.Refusal);
        Assert.Equal("claimed", answer.Request.State);
    }

    [Fact]
    public async Task An_outcome_other_than_started_or_released_is_refused()
    {
        var request = Entry();

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            _tools.MarkSessionRequestAsync(request.Id, "linked", "manager-1", "session-a", TestContext.Current.CancellationToken));

        Assert.Contains("'started' or 'released'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_list_answers_every_request_oldest_first_with_its_brief()
    {
        var first = Entry();
        _clock.Advance(TimeSpan.FromSeconds(1));
        var second = Plan("+session-dispatch");

        var answer = await _tools.ListSessionRequestsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(answer.State);
        Assert.Equal(2, answer.Count);
        Assert.Equal([first.Id, second.Id], answer.Requests.Select(request => request.Id));

        var plan = answer.Requests[1];
        Assert.Equal("plan", plan.Kind);
        Assert.Equal("session-dispatch", plan.PlanTag);
        Assert.Null(plan.EntryId);
        Assert.Equal("/backlog-tools:execute-plan +session-dispatch", plan.Brief);
    }

    [Fact]
    public async Task The_list_narrows_to_one_state()
    {
        var waiting = Entry();
        var claimed = Entry();
        await _tools.ClaimSessionRequestAsync(claimed.Id, "manager-1", TestContext.Current.CancellationToken);

        var answer = await _tools.ListSessionRequestsAsync("Claimed", TestContext.Current.CancellationToken);

        Assert.Equal("claimed", answer.State);
        Assert.Equal(claimed.Id, Assert.Single(answer.Requests).Id);
        Assert.DoesNotContain(answer.Requests, request => request.Id == waiting.Id);
    }

    [Theory]
    [InlineData("waiting")]
    [InlineData("1")]
    [InlineData("+1")]
    [InlineData("claimed,started")]
    public async Task The_list_refuses_a_state_it_does_not_know(string state)
    {
        await Assert.ThrowsAsync<McpException>(() =>
            _tools.ListSessionRequestsAsync(state, TestContext.Current.CancellationToken));
    }
}
