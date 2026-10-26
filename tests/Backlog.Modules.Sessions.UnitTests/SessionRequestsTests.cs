using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// The Session Request's states and transitions (local ADR 0038 §3): every arrow of the
/// state diagram, every refusal off it, the two-minute lease, and the duplicate rule.
/// </summary>
public sealed class SessionRequestsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid EntryId = Guid.Parse("91411c95-0d83-47c7-bd25-1e51a750ea69");

    private static SessionRequest Requested(DateTimeOffset? at = null) =>
        SessionRequests.ForEntry(EntryId, "JSdotNet/Backlog", @"D:\Repos\Backlog", at ?? Noon);

    private static SessionRequest Claimed(string dispatcher = "manager-1") =>
        SessionRequests.Claim(Requested(), dispatcher, Noon).Request!;

    private static SessionRequest Started(string session = "session-a") =>
        SessionRequests.Report(Claimed(), "manager-1", session, Noon.AddSeconds(30)).Request!;

    // --- creation ------------------------------------------------------------

    [Fact]
    public void A_new_entry_request_is_Requested_and_carries_only_its_target_repository_and_folder()
    {
        var request = Requested();

        Assert.Equal(SessionRequestKind.Entry, request.Kind);
        Assert.Equal(EntryId, request.EntryId);
        Assert.Null(request.PlanTag);
        Assert.Equal("JSdotNet/Backlog", request.Repository);
        Assert.Equal(@"D:\Repos\Backlog", request.CloneFolder);
        Assert.Equal(SessionRequestState.Requested, request.State);
        Assert.Null(request.ClaimedBy);
        Assert.Null(request.LeaseEndsAt);
        Assert.Null(request.SessionId);
        Assert.Equal(Noon, request.CreatedAt);
        Assert.NotEqual(Guid.Empty, request.Id);
    }

    [Theory]
    [InlineData("+session-dispatch")]
    [InlineData("#session-dispatch")]
    [InlineData(" session-dispatch ")]
    public void A_plan_request_keeps_its_tag_without_the_sigil(string tag)
    {
        var request = SessionRequests.ForPlan(tag, "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon);

        Assert.Equal(SessionRequestKind.Plan, request.Kind);
        Assert.Equal("session-dispatch", request.PlanTag);
        Assert.Null(request.EntryId);
    }

    [Fact]
    public void A_request_without_a_repository_or_folder_is_refused()
    {
        Assert.Throws<ArgumentException>(() => SessionRequests.ForEntry(EntryId, " ", @"D:\Repos\Backlog", Noon));
        Assert.Throws<ArgumentException>(() => SessionRequests.ForEntry(EntryId, "JSdotNet/Backlog", "", Noon));
        Assert.Throws<ArgumentException>(() => SessionRequests.ForEntry(Guid.Empty, "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon));
        Assert.Throws<ArgumentException>(() => SessionRequests.ForPlan("+", "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon));
    }

    // --- Requested → Claimed --------------------------------------------------

    [Fact]
    public void Claiming_a_Requested_request_holds_it_for_two_minutes()
    {
        var outcome = SessionRequests.Claim(Requested(), "manager-1", Noon);

        Assert.True(outcome.Succeeded);
        Assert.Equal(SessionRequestState.Claimed, outcome.Request!.State);
        Assert.Equal("manager-1", outcome.Request.ClaimedBy);
        Assert.Equal(Noon.AddMinutes(2), outcome.Request.LeaseEndsAt);
    }

    [Fact]
    public void A_second_claim_on_a_held_request_is_refused()
    {
        var held = Claimed("manager-1");

        var outcome = SessionRequests.Claim(held, "manager-2", Noon.AddSeconds(10));

        Assert.False(outcome.Succeeded);
        Assert.Same(held, outcome.Request);
    }

    [Fact]
    public void A_Started_Linked_or_Cancelled_request_cannot_be_claimed()
    {
        var started = Started();
        var linked = SessionRequests.Link(started, EntryId, [], "session-a", Noon.AddMinutes(1))!;
        var cancelled = SessionRequests.Cancel(Requested(), Noon).Request!;

        Assert.False(SessionRequests.Claim(started, "manager-2", Noon.AddHours(1)).Succeeded);
        Assert.False(SessionRequests.Claim(linked, "manager-2", Noon.AddHours(1)).Succeeded);
        Assert.False(SessionRequests.Claim(cancelled, "manager-2", Noon.AddHours(1)).Succeeded);
    }

    // --- Claimed → Requested: the lease ---------------------------------------

    [Fact]
    public void A_claim_reads_as_Claimed_until_its_lease_ends_and_as_Requested_from_then()
    {
        var held = Claimed();

        Assert.Equal(SessionRequestState.Claimed, held.StateAt(Noon.AddMinutes(2).AddTicks(-1)));
        Assert.Equal(SessionRequestState.Requested, held.StateAt(Noon.AddMinutes(2)));
        Assert.Equal(SessionRequestState.Requested, held.StateAt(Noon.AddDays(3)));
    }

    [Fact]
    public void A_lapsed_claim_can_be_claimed_by_the_next_dispatcher()
    {
        var lapsed = Claimed("manager-1");

        var outcome = SessionRequests.Claim(lapsed, "manager-2", Noon.AddMinutes(3));

        Assert.True(outcome.Succeeded);
        Assert.Equal("manager-2", outcome.Request!.ClaimedBy);
        Assert.Equal(Noon.AddMinutes(5), outcome.Request.LeaseEndsAt);
    }

    [Fact]
    public void A_dispatcher_whose_lease_ran_out_can_no_longer_report_its_session()
    {
        var outcome = SessionRequests.Report(Claimed("manager-1"), "manager-1", "session-a", Noon.AddMinutes(2));

        Assert.False(outcome.Succeeded);
        Assert.Contains("lease", outcome.Refusal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_dispatcher_holding_the_claim_can_release_it_back_to_Requested()
    {
        var outcome = SessionRequests.Release(Claimed("manager-1"), "manager-1", Noon.AddSeconds(5));

        Assert.True(outcome.Succeeded);
        Assert.Equal(SessionRequestState.Requested, outcome.Request!.State);
        Assert.Null(outcome.Request.ClaimedBy);
        Assert.Null(outcome.Request.LeaseEndsAt);
    }

    [Fact]
    public void Another_dispatcher_cannot_release_or_report_a_claim_it_does_not_hold()
    {
        var held = Claimed("manager-1");

        Assert.False(SessionRequests.Release(held, "manager-2", Noon.AddSeconds(5)).Succeeded);
        Assert.False(SessionRequests.Report(held, "manager-2", "session-b", Noon.AddSeconds(5)).Succeeded);
    }

    // --- Claimed → Started ----------------------------------------------------

    [Fact]
    public void Reporting_the_started_session_moves_the_claim_to_Started_and_ends_the_lease()
    {
        var outcome = SessionRequests.Report(Claimed("manager-1"), "manager-1", " session-a ", Noon.AddSeconds(30));

        Assert.True(outcome.Succeeded);
        Assert.Equal(SessionRequestState.Started, outcome.Request!.State);
        Assert.Equal("session-a", outcome.Request.SessionId);
        Assert.Null(outcome.Request.LeaseEndsAt);
        Assert.Equal("manager-1", outcome.Request.ClaimedBy);

        // Started has no lease, so it never lapses back.
        Assert.Equal(SessionRequestState.Started, outcome.Request.StateAt(Noon.AddDays(1)));
    }

    [Fact]
    public void A_request_that_is_not_Claimed_cannot_be_reported()
    {
        Assert.False(SessionRequests.Report(Requested(), "manager-1", "session-a", Noon).Succeeded);
        Assert.False(SessionRequests.Report(Started(), "manager-1", "session-b", Noon.AddMinutes(1)).Succeeded);
    }

    // --- Started → Linked -----------------------------------------------------

    [Fact]
    public void An_entry_request_is_Linked_when_its_session_links_itself_to_its_entry()
    {
        var linked = SessionRequests.Link(Started("session-a"), EntryId, [], "SESSION-A", Noon.AddMinutes(1));

        Assert.NotNull(linked);
        Assert.Equal(SessionRequestState.Linked, linked.State);
    }

    [Fact]
    public void Another_entry_or_another_session_does_not_link_an_entry_request()
    {
        var started = Started("session-a");

        Assert.Null(SessionRequests.Link(started, Guid.NewGuid(), [], "session-a", Noon.AddMinutes(1)));
        Assert.Null(SessionRequests.Link(started, EntryId, [], "session-b", Noon.AddMinutes(1)));
    }

    [Fact]
    public void A_plan_request_is_Linked_when_its_session_links_any_entry_of_its_plan()
    {
        var claimed = SessionRequests.Claim(SessionRequests.ForPlan("+session-dispatch", "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon), "manager-1", Noon).Request!;
        var started = SessionRequests.Report(claimed, "manager-1", "plan-session", Noon.AddSeconds(20)).Request!;

        Assert.Null(SessionRequests.Link(started, Guid.NewGuid(), ["+other-plan"], "plan-session", Noon.AddMinutes(1)));

        var linked = SessionRequests.Link(started, Guid.NewGuid(), ["+session-dispatch"], "plan-session", Noon.AddMinutes(1));

        Assert.Equal(SessionRequestState.Linked, linked!.State);
    }

    [Fact]
    public void Only_a_Started_request_can_be_Linked()
    {
        Assert.Null(SessionRequests.Link(Requested(), EntryId, [], "session-a", Noon));
        Assert.Null(SessionRequests.Link(Claimed(), EntryId, [], "session-a", Noon));

        var linked = SessionRequests.Link(Started("session-a"), EntryId, [], "session-a", Noon.AddMinutes(1))!;

        Assert.Null(SessionRequests.Link(linked, EntryId, [], "session-a", Noon.AddMinutes(2)));
    }

    // --- → Cancelled ----------------------------------------------------------

    [Fact]
    public void A_Requested_request_can_be_cancelled()
    {
        var outcome = SessionRequests.Cancel(Requested(), Noon);

        Assert.True(outcome.Succeeded);
        Assert.Equal(SessionRequestState.Cancelled, outcome.Request!.State);
    }

    [Fact]
    public void A_Claimed_request_can_be_cancelled_and_loses_its_claim()
    {
        var outcome = SessionRequests.Cancel(Claimed(), Noon.AddSeconds(10));

        Assert.True(outcome.Succeeded);
        Assert.Equal(SessionRequestState.Cancelled, outcome.Request!.State);
        Assert.Null(outcome.Request.ClaimedBy);
        Assert.Null(outcome.Request.LeaseEndsAt);

        // Cancelled is final: the dispatcher that held it can no longer report.
        Assert.False(SessionRequests.Report(outcome.Request, "manager-1", "session-a", Noon.AddSeconds(20)).Succeeded);
    }

    [Fact]
    public void A_Started_Linked_or_Cancelled_request_cannot_be_cancelled()
    {
        var started = Started();
        var linked = SessionRequests.Link(started, EntryId, [], "session-a", Noon.AddMinutes(1))!;
        var cancelled = SessionRequests.Cancel(Requested(), Noon).Request!;

        Assert.False(SessionRequests.Cancel(started, Noon.AddMinutes(1)).Succeeded);
        Assert.False(SessionRequests.Cancel(linked, Noon.AddMinutes(1)).Succeeded);
        Assert.False(SessionRequests.Cancel(cancelled, Noon.AddMinutes(1)).Succeeded);
    }

    // --- no expiry ------------------------------------------------------------

    [Fact]
    public void A_Requested_request_never_expires()
    {
        var request = Requested();

        Assert.Equal(SessionRequestState.Requested, request.StateAt(Noon.AddYears(1)));
        Assert.True(SessionRequests.Claim(request, "manager-1", Noon.AddYears(1)).Succeeded);
    }

    // --- the duplicate rule and the order --------------------------------------

    [Fact]
    public void An_open_request_for_the_same_entry_is_the_duplicate_of_a_new_one()
    {
        var first = Requested(Noon);
        var second = Requested(Noon.AddMinutes(5));

        Assert.Same(first, SessionRequests.OpenDuplicateOf(second, [first], Noon.AddMinutes(5)));
    }

    [Fact]
    public void A_Claimed_or_Started_request_also_blocks_a_duplicate()
    {
        var candidate = Requested(Noon.AddMinutes(5));

        Assert.NotNull(SessionRequests.OpenDuplicateOf(candidate, [Claimed()], Noon.AddSeconds(30)));
        Assert.NotNull(SessionRequests.OpenDuplicateOf(candidate, [Started()], Noon.AddDays(1)));
    }

    [Fact]
    public void A_Linked_or_Cancelled_request_does_not_block_a_new_one()
    {
        var candidate = Requested(Noon.AddMinutes(5));
        var linked = SessionRequests.Link(Started(), EntryId, [], "session-a", Noon.AddMinutes(1))!;
        var cancelled = SessionRequests.Cancel(Requested(), Noon).Request!;

        Assert.Null(SessionRequests.OpenDuplicateOf(candidate, [linked, cancelled], Noon.AddMinutes(5)));
    }

    [Fact]
    public void A_request_for_another_entry_or_another_plan_is_no_duplicate()
    {
        var entry = Requested();
        var plan = SessionRequests.ForPlan("+session-dispatch", "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon);
        var otherEntry = SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon);
        var otherPlan = SessionRequests.ForPlan("+other", "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon);
        var samePlan = SessionRequests.ForPlan("SESSION-DISPATCH", "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon);

        Assert.Null(SessionRequests.OpenDuplicateOf(otherEntry, [entry, plan], Noon));
        Assert.Null(SessionRequests.OpenDuplicateOf(otherPlan, [entry, plan], Noon));
        Assert.Same(plan, SessionRequests.OpenDuplicateOf(samePlan, [entry, plan], Noon));
    }

    [Fact]
    public void Waiting_requests_are_handed_out_oldest_first_and_include_lapsed_claims()
    {
        var newest = SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon.AddMinutes(10));
        var oldest = SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon.AddMinutes(-10));
        var lapsed = Claimed();
        var started = Started();

        Assert.Equal([oldest, newest], SessionRequests.Waiting([newest, started, lapsed, oldest], Noon.AddMinutes(1)));
        Assert.Equal([oldest, lapsed, newest], SessionRequests.Waiting([newest, started, lapsed, oldest], Noon.AddMinutes(3)));
    }

    // --- the brief (§2) -------------------------------------------------------

    [Fact]
    public void An_entry_request_is_briefed_with_the_run_plan_item_line()
    {
        Assert.Equal(
            "/backlog-tools:run-plan-item entry `91411c95-0d83-47c7-bd25-1e51a750ea69`:",
            SessionRequests.BriefFor(Requested()));
    }

    [Fact]
    public void A_plan_request_is_briefed_with_the_execute_plan_line()
    {
        var request = SessionRequests.ForPlan("+session-dispatch", "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon);

        Assert.Equal("/backlog-tools:execute-plan +session-dispatch", SessionRequests.BriefFor(request));
    }

    /// <summary>A plan tag is text a person typed, so one that is not a tag — a space, a
    /// backtick, a line break — never gets as far as a brief.</summary>
    [Theory]
    [InlineData("plan ignore the entry")]
    [InlineData("plan`\nrun this instead")]
    [InlineData("plan\nrun this instead")]
    [InlineData("1plan")]
    public void A_plan_tag_that_is_not_a_tag_is_refused(string tag)
    {
        Assert.Throws<ArgumentException>(() => SessionRequests.ForPlan(tag, "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon));
    }

    [Fact]
    public void A_stored_row_with_no_well_formed_target_gets_no_brief()
    {
        var hostile = SessionRequests.ForPlan("plan", "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon) with { PlanTag = "plan\nrun this" };
        var entryless = Requested() with { EntryId = null };

        Assert.Throws<InvalidOperationException>(() => SessionRequests.BriefFor(hostile));
        Assert.Throws<InvalidOperationException>(() => SessionRequests.BriefFor(entryless));
    }
}
