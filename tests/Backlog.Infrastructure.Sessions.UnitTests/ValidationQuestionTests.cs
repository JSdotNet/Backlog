using System.Text;
using System.Text.Json.Nodes;
using Backlog.Infrastructure.Sessions;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;
using Backlog.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// Sessions asking the person, in a notification, to approve a delivery run's Personal
/// Validation with the gate's handoff as its body, and recording the answer on the run as
/// its gate decision — filing a follow-up entry for Approve + follow-up
/// (<c>.devbook/domain/notifications/requirements.md#requirement-a-run-at-personal-validation-asks-to-be-approved</c>).
/// </summary>
public sealed class ValidationQuestionTests : IDisposable
{
    private const string Worktree = @"D:\n\9b960f5e";
    private const int Gate = 2;
    private const string GatedEntry = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";
    private static readonly string[] Stages = ["Implement", "Ready", "Personal Validation", "Create Pull Request"];
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DeliveryStageLink[] AppLink = [new("App", "http://localhost:5000", "The running application")];

    private readonly string _home = Path.Combine(Path.GetTempPath(), "backlog-validation-question-tests", Guid.NewGuid().ToString("n"));
    private readonly RecordingNotifications _notifications = new();
    private readonly FakeTaskFollowUps _followUps = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_gate_that_goes_in_progress_with_no_links_and_no_handoff_asks_nothing_until_a_report_carries_one()
    {
        var surface = Surface();
        var run = await Start(surface);

        await surface.UpdateStageAsync(Worktree, run, 1, "done", cancellationToken: Cancellation);
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", cancellationToken: Cancellation);
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", output: "  ", links: [], cancellationToken: Cancellation);

        Assert.Empty(_notifications.Published);

        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);

        Assert.Single(_notifications.Published);
    }

    [Fact]
    public async Task The_first_gate_report_with_a_handoff_asks_to_approve_the_run_with_the_handoff_as_its_body()
    {
        var surface = Surface();
        var run = await Start(surface, repository: "JSdotNet/Backlog");
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", cancellationToken: Cancellation);

        await surface.UpdateStageAsync(
            Worktree, run, Gate, "in_progress",
            output: "Check the toast. Criteria: AC 1-3. Evidence: .qa-workspace/toast.png",
            links: AppLink,
            cancellationToken: Cancellation);

        var (id, draft) = Assert.Single(_notifications.Published);
        Assert.Equal(NotificationKind.Question, draft.Kind);
        Assert.Equal("Backlog · notifications:11 - Raise notifications · Approve or revise", draft.Title);
        Assert.Equal(new NotificationSubject("sessions", "run", run), draft.Subject);
        Assert.Equal(new NotificationSource("sessions", $"{run}@{Worktree}"), draft.Source);

        foreach (var part in new[] { "App", "http://localhost:5000", "The running application", "Check the toast. Criteria: AC 1-3. Evidence: .qa-workspace/toast.png", "JSdotNet/Backlog" })
        {
            Assert.Contains(part, draft.BodyMarkdown, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(Worktree, draft.BodyMarkdown, StringComparison.Ordinal);
        Assert.Equal(
            new NotificationOrigin("Claude Code", "JSdotNet/Backlog", Via: "Agent session"),
            draft.Origin);

        foreach (var outside in new[] { draft.Title, draft.Summary ?? string.Empty })
        {
            Assert.DoesNotContain("localhost", outside, StringComparison.Ordinal);
            Assert.DoesNotContain("toast", outside, StringComparison.OrdinalIgnoreCase);
        }

        var asked = (await File(run))["validationQuestion"]!.AsObject();
        Assert.Equal(id.ToString("D"), asked["notificationId"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_link_only_the_desktop_can_open_stays_a_link_marked_desktop_only()
    {
        var surface = Surface();
        var run = await Start(surface);

        await surface.UpdateStageAsync(
            Worktree, run, Gate, "in_progress",
            links:
            [
                new("App", "http://localhost:5000", "The running application"),
                new("Evidence", "file:///D:/n/9b960f5e/.qa-workspace/toast.png", null),
                new("Docs", "https://learn.microsoft.com/dotnet", null)
            ],
            cancellationToken: Cancellation);

        var body = Assert.Single(_notifications.Published).Draft.BodyMarkdown!;
        Assert.Contains("- [App](http://localhost:5000) (desktop only) — The running application\n", body, StringComparison.Ordinal);
        Assert.Contains("- [Evidence](file:///D:/n/9b960f5e/.qa-workspace/toast.png) (desktop only)\n", body, StringComparison.Ordinal);
        Assert.Contains("- [Docs](https://learn.microsoft.com/dotnet)\n", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_pull_request_the_gate_passes_comes_first_under_its_own_heading_in_the_order_sent()
    {
        var surface = Surface();
        var run = await Start(surface);

        await surface.UpdateStageAsync(
            Worktree, run, Gate, "in_progress",
            output: "Check the toast.",
            links:
            [
                new("App", "http://localhost:5000", null),
                new("Draft PR", "https://github.com/JSdotNet/Backlog/pull/1700", "Ready once approved"),
                new(null, "https://github.com/JSdotNet/Backlog-Desktop/pull/12/files", null)
            ],
            cancellationToken: Cancellation);

        var body = Assert.Single(_notifications.Published).Draft.BodyMarkdown!;
        var section = body.IndexOf("## Pull request\n", StringComparison.Ordinal);
        var first = body.IndexOf("- [Draft PR](https://github.com/JSdotNet/Backlog/pull/1700) — Ready once approved\n", StringComparison.Ordinal);
        var second = body.IndexOf("- [PR #12](https://github.com/JSdotNet/Backlog-Desktop/pull/12/files)\n", StringComparison.Ordinal);

        Assert.True(section > 0);
        Assert.True(section < first && first < second);
        Assert.True(second < body.IndexOf("**Approve**", StringComparison.Ordinal));
        Assert.True(section > body.IndexOf("reached Personal Validation", StringComparison.Ordinal));
        // Lifted out, not repeated: the address appears once, and not under Review links.
        Assert.Equal(2, body.Split("/pull/1700").Length);
        Assert.DoesNotContain("pull/1700", body[body.IndexOf("## Review links", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pull_request_the_run_recorded_earlier_is_shown_when_the_gate_passes_none()
    {
        var surface = Surface();
        var run = await Start(surface);
        await surface.UpdateStageAsync(
            Worktree, run, 1, "done",
            links: [new("Draft pull request", "https://github.com/JSdotNet/Backlog/pull/1701", null)],
            cancellationToken: Cancellation);

        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);

        var body = Assert.Single(_notifications.Published).Draft.BodyMarkdown!;
        Assert.Contains("## Pull request\n\n- [Draft pull request](https://github.com/JSdotNet/Backlog/pull/1701)\n", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_a_pull_request_approving_reads_as_marking_it_ready_and_without_one_it_is_still_opened_at_the_desktop()
    {
        var surface = Surface();
        var withPull = await Start(surface);
        var without = await Start(surface, skill: "flow-spec");

        await surface.UpdateStageAsync(
            Worktree, withPull, Gate, "in_progress",
            links: [new("PR", "https://github.com/JSdotNet/Backlog/pull/1702", null)],
            cancellationToken: Cancellation);
        await surface.UpdateStageAsync(Worktree, without, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);

        var (pulled, plain) = (_notifications.Published[0].Draft, _notifications.Published[1].Draft);

        Assert.Contains("mark the pull request ready for review", pulled.Summary, StringComparison.Ordinal);
        Assert.Contains("mark the pull request ready for review", pulled.BodyMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("still", pulled.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("still happens at the desktop", pulled.BodyMarkdown, StringComparison.Ordinal);
        Assert.InRange(pulled.Summary!.Length, 1, 180);

        Assert.Equal("The run reached Personal Validation. Your answer is recorded on the run; the pull request is still opened at the desktop.", plain.Summary);
        Assert.Contains("Opening the pull request still happens at the desktop, in the session that runs it.", plain.BodyMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("## Pull request", plain.BodyMarkdown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SessionPullRequestState.Merged)]
    [InlineData(SessionPullRequestState.Closed)]
    public async Task A_run_whose_pull_request_already_merged_or_closed_asks_nothing_at_its_gate(SessionPullRequestState state)
    {
        var pulls = new FixedPullRequestState(state);
        var surface = Surface(pullRequests: pulls);
        var run = await Start(surface);
        await surface.UpdateStageAsync(
            Worktree, run, 1, "done",
            links: [new("PR", "https://github.com/JSdotNet/Backlog/pull/1652", null)],
            cancellationToken: Cancellation);

        var updated = await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", output: "Check the toast.", links: AppLink, cancellationToken: Cancellation);

        Assert.Equal("in_progress", updated.Status);
        Assert.Empty(_notifications.Published);
        Assert.Equal(("JSdotNet/Backlog", 1652), Assert.Single(pulls.Asked.Distinct()));
    }

    [Theory]
    [InlineData(SessionPullRequestState.Open)]
    [InlineData(SessionPullRequestState.Draft)]
    [InlineData(null)]
    public async Task A_run_whose_pull_request_is_open_a_draft_or_unread_asks_as_before(SessionPullRequestState? state)
    {
        var surface = Surface(pullRequests: new FixedPullRequestState(state));
        var run = await Start(surface);

        await surface.UpdateStageAsync(
            Worktree, run, Gate, "in_progress",
            links: [new("PR", "https://github.com/JSdotNet/Backlog/pull/1700", null)],
            cancellationToken: Cancellation);

        Assert.Single(_notifications.Published);
    }

    [Theory]
    [InlineData(SessionPullRequestState.Open)]
    [InlineData(null)]
    public async Task A_run_whose_pull_request_ended_stays_armed_and_asks_once_a_later_report_reads_it_open_or_unread(SessionPullRequestState? later)
    {
        var pulls = new HeldPullRequestStates { State = SessionPullRequestState.Merged };
        var surface = Surface(pullRequests: pulls);
        var run = await Start(surface);
        DeliveryStageLink[] withPull = [new("PR", "https://github.com/JSdotNet/Backlog/pull/1652", null)];

        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: withPull, cancellationToken: Cancellation);
        Assert.Empty(_notifications.Published);
        Assert.True((await File(run))["validationQuestion"]!["armed"]!.GetValue<bool>());

        pulls.State = later;
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: withPull, cancellationToken: Cancellation);
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: withPull, cancellationToken: Cancellation);

        Assert.Single(_notifications.Published);
    }

    [Fact]
    public async Task A_pull_request_read_that_outlasts_the_gates_budget_asks_as_before()
    {
        var clock = new FakeTimeProvider(Noon);
        var pulls = new HeldPullRequestStates { Hang = true };
        var surface = Surface(pullRequests: pulls, clock: clock);
        var run = await Start(surface);

        var update = surface.UpdateStageAsync(
            Worktree, run, Gate, "in_progress",
            links: [new("PR", "https://github.com/JSdotNet/Backlog/pull/1652", null)],
            cancellationToken: Cancellation);
        await pulls.Entered.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        clock.Advance(ValidationQuestions.PullRequestReadBudget);

        Assert.Equal("in_progress", (await update.WaitAsync(TimeSpan.FromSeconds(10), Cancellation)).Status);
        Assert.Single(_notifications.Published);
    }

    [Fact]
    public async Task The_gates_budget_is_for_every_pull_request_the_run_recorded_together_not_for_each()
    {
        var clock = new FakeTimeProvider(Noon);
        var pulls = new HeldPullRequestStates { Hang = true };
        var surface = Surface(pullRequests: pulls, clock: clock);
        var run = await Start(surface);

        var update = surface.UpdateStageAsync(
            Worktree, run, Gate, "in_progress",
            links:
            [
                new("PR", "https://github.com/JSdotNet/Backlog/pull/1652", null),
                new("PR", "https://github.com/JSdotNet/Backlog-Desktop/pull/12", null)
            ],
            cancellationToken: Cancellation);

        // The first read answers merged after most of the budget; the second never answers.
        await pulls.Entered.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        clock.Advance(ValidationQuestions.PullRequestReadBudget * 0.6);
        pulls.Calls[0].SetResult(SessionPullRequestState.Merged);
        await pulls.Entered.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        clock.Advance(ValidationQuestions.PullRequestReadBudget * 0.4);

        await update.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        Assert.Single(_notifications.Published);
    }

    [Fact]
    public async Task A_pull_request_read_that_fails_asks_as_before_and_a_run_with_none_reads_nothing()
    {
        var failing = new FixedPullRequestState(SessionPullRequestState.Merged) { Fails = new HttpRequestException("offline") };
        var surface = Surface(pullRequests: failing);
        var withPull = await Start(surface);
        var without = await Start(surface, skill: "flow-spec");

        await surface.UpdateStageAsync(
            Worktree, withPull, Gate, "in_progress",
            links: [new("PR", "https://github.com/JSdotNet/Backlog/pull/1700", null)],
            cancellationToken: Cancellation);
        var asked = failing.Asked.Count;
        await surface.UpdateStageAsync(Worktree, without, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);

        Assert.Equal(2, _notifications.Published.Count);
        Assert.Equal(asked, failing.Asked.Count);
    }

    [Fact]
    public async Task A_long_run_title_is_cut_on_a_whole_character_to_fit_the_question()
    {
        var surface = Surface();
        var run = await Start(surface, title: new string('x', 58) + "\U0001F600" + " and the rest of the run title");

        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);

        var title = Assert.Single(_notifications.Published).Draft.Title;
        Assert.Equal(new string('x', 58) + "… · Approve or revise", title);
        Assert.True(title.Length <= 80);
    }

    [Fact]
    public async Task A_run_that_sends_no_repository_takes_it_from_the_worktrees_registered_clone()
    {
        var surface = Surface(new FixedRepository("JSdotNet/Backlog"));
        var run = await Start(surface);

        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);

        var draft = Assert.Single(_notifications.Published).Draft;
        Assert.Equal("Backlog · notifications:11 - Raise notifications · Approve or revise", draft.Title);
        Assert.Equal(new NotificationOrigin("Claude Code", "JSdotNet/Backlog", Via: "Agent session"), draft.Origin);
        Assert.Contains("in `JSdotNet/Backlog`", draft.BodyMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_repository_the_run_sends_wins_over_the_registered_clone()
    {
        var surface = Surface(new FixedRepository("JSdotNet/Elsewhere"));
        var run = await Start(surface, repository: "JSdotNet/Backlog");

        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);

        var draft = Assert.Single(_notifications.Published).Draft;
        Assert.StartsWith("Backlog · ", draft.Title, StringComparison.Ordinal);
        Assert.Equal("JSdotNet/Backlog", draft.Origin!.Place);
    }

    [Fact]
    public async Task A_run_in_no_known_repository_is_titled_by_its_change_and_names_no_place()
    {
        var surface = Surface(new FixedRepository(null));
        var run = await Start(surface);

        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);

        var draft = Assert.Single(_notifications.Published).Draft;
        Assert.Equal("notifications:11 - Raise notifications · Approve or revise", draft.Title);
        Assert.Equal(new NotificationOrigin("Claude Code", Via: "Agent session"), draft.Origin);
    }

    [Fact]
    public async Task A_handoff_longer_than_the_body_holds_is_cut_to_fit_and_ends_with_a_line_that_says_so()
    {
        var surface = Surface();
        var run = await Start(surface);

        // Three bytes a character in UTF-8, so a character count would let it through.
        var handoff = new string('€', 20_000);

        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", output: handoff, cancellationToken: Cancellation);

        var body = Assert.Single(_notifications.Published).Draft.BodyMarkdown!;
        Assert.InRange(Encoding.UTF8.GetByteCount(body), 30 * 1024, 32 * 1024);
        Assert.Contains("€€€", body, StringComparison.Ordinal);
        var last = body.Split('\n')[^1];
        Assert.Contains("truncated", last, StringComparison.OrdinalIgnoreCase);
        // MarkdownPreview renders only *…* as emphasis; _…_ would show its underscores.
        Assert.StartsWith("*", last, StringComparison.Ordinal);
        Assert.EndsWith("*", last, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData($"/backlog-tools:run-plan-item entry `{GatedEntry}`:\n# Raise notifications")]
    [InlineData("Backlog plan item `11` of plan `notifications`: raise them.")]
    public async Task A_run_that_names_its_entry_is_offered_approve_approve_with_a_follow_up_revise_and_reject(string prompt)
    {
        var surface = Surface();
        var run = await Start(surface, prompt: prompt);

        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);

        var spec = Assert.IsType<AnswerSpec>(Assert.Single(_notifications.Published).Draft.Answer);
        Assert.Equal(["approve", "approve-follow-up", "revise", "reject"], spec.Choices.Select(choice => choice.Id));
        Assert.Equal([false, true, true, false], spec.Choices.Select(choice => choice.TakesText));
        Assert.False(spec.AllowMultiple);
        Assert.Null(spec.Open);
        Assert.Equal(AnswerOn.AnyInstance, spec.AnswerOn);
    }

    [Fact]
    public async Task A_run_that_names_no_entry_is_offered_approve_revise_and_reject_with_no_follow_up()
    {
        var surface = Surface();
        var run = await Start(surface, prompt: "Fix the flaky toast test.");

        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);

        var spec = Assert.IsType<AnswerSpec>(Assert.Single(_notifications.Published).Draft.Answer);
        Assert.Equal(["approve", "revise", "reject"], spec.Choices.Select(choice => choice.Id));
        Assert.Equal([false, true, false], spec.Choices.Select(choice => choice.TakesText));
        Assert.DoesNotContain("entry is blocked", Assert.Single(_notifications.Published).Draft.BodyMarkdown!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_gate_reported_twice_with_its_handoff_asks_once()
    {
        var surface = Surface();
        var run = await Start(surface);

        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", output: "What to check.", links: AppLink, cancellationToken: Cancellation);

        Assert.Single(_notifications.Published);
    }

    [Fact]
    public async Task Re_reporting_the_gate_after_it_was_approved_asks_nothing_more()
    {
        var surface = Surface();
        var run = await Start(surface);
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);
        await Answers(surface).HandleAnswerAsync(Answer(run, "approve", null), Cancellation);

        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", output: "Again.", links: AppLink, cancellationToken: Cancellation);

        Assert.Single(_notifications.Published);
    }

    [Fact]
    public async Task After_a_revise_round_the_gate_asks_again_with_only_the_new_attempts_handoff()
    {
        var surface = Surface();
        var run = await Start(surface);
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", output: "First attempt handoff.", links: AppLink, cancellationToken: Cancellation);
        await Answers(surface).HandleAnswerAsync(Answer(run, "revise", "The toast overlaps the header"), Cancellation);
        await surface.SetRunContextAsync(Worktree, run, approval: "pending", cancellationToken: Cancellation);

        // The earlier attempt's links and output still sit on the stage: a bare report asks nothing.
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", cancellationToken: Cancellation);
        Assert.Single(_notifications.Published);

        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", output: "Second attempt handoff.", cancellationToken: Cancellation);

        Assert.Equal(2, _notifications.Published.Count);
        var body = _notifications.Published[^1].Draft.BodyMarkdown!;
        Assert.Contains("Second attempt handoff.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("First attempt handoff.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("approve", null, "approved", "Approved from a Backlog notification.", "approve")]
    [InlineData("revise", "The toast overlaps the header", "rejected", "The toast overlaps the header", "revise")]
    [InlineData("reject", null, "rejected", "Rejected from a Backlog notification.", "reject")]
    // An answer to a question the previous build published: open text, no choice.
    [InlineData(null, "Rename the setting first.", "rejected", "Rename the setting first.", "revise")]
    public async Task The_answer_is_recorded_on_the_run_as_its_gate_decision(string? choice, string? text, string decision, string note, string chosen)
    {
        var surface = Surface();
        var run = await Start(surface);
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);
        var answers = Answers(surface);

        await answers.HandleAnswerAsync(Answer(run, choice, text), Cancellation);

        var approval = (await File(run))["approval"]!.AsObject();
        Assert.Equal(decision, approval["personalValidation"]!.GetValue<string>());
        Assert.Equal(note, approval["note"]!.GetValue<string>());
        Assert.Equal(chosen, approval["choice"]!.GetValue<string>());
        Assert.Null(approval["followUpEntryId"]);
        Assert.Equal("2026-10-22T12:00:00.000Z", approval["decidedAt"]!.GetValue<string>());
        Assert.Equal("sessions", answers.Source);
        Assert.Empty(_followUps.Requests);
    }

    [Theory]
    [InlineData($"/backlog-tools:run-plan-item entry `{GatedEntry}`:\n# Raise notifications", GatedEntry, null, null)]
    [InlineData("Backlog plan item `11` of plan `notifications`: raise them.", null, "notifications", "11")]
    public async Task Approve_with_a_follow_up_files_one_entry_after_the_gated_entry_and_records_its_id(string prompt, string? entry, string? plan, string? item)
    {
        var surface = Surface();
        var run = await Start(surface, prompt: prompt);
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);
        var answer = Answer(run, "approve-follow-up", "Add the empty-state screenshot to the docs");

        await Answers(surface).HandleAnswerAsync(answer, Cancellation);

        var request = Assert.Single(_followUps.Requests);
        Assert.Equal(new GatedEntry(entry is null ? null : Guid.Parse(entry), plan, item), request.Gated);
        Assert.Equal("Add the empty-state screenshot to the docs", request.Text);
        Assert.Equal(answer.NotificationId, request.NotificationId);

        var approval = (await File(run))["approval"]!.AsObject();
        Assert.Equal("approved", approval["personalValidation"]!.GetValue<string>());
        Assert.Equal("Add the empty-state screenshot to the docs", approval["note"]!.GetValue<string>());
        Assert.Equal("approve-follow-up", approval["choice"]!.GetValue<string>());
        Assert.Equal(_followUps.Filed[answer.NotificationId].ToString("D"), approval["followUpEntryId"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_follow_up_answer_delivered_twice_leaves_the_run_unchanged_and_files_no_second_entry()
    {
        var surface = Surface();
        var run = await Start(surface, prompt: $"/backlog-tools:run-plan-item entry `{GatedEntry}`:\n# Raise notifications");
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);
        var answers = Answers(surface);
        var answer = Answer(run, "approve-follow-up", "Add the screenshot");

        await answers.HandleAnswerAsync(answer, Cancellation);
        var once = (await File(run)).ToJsonString();
        await answers.HandleAnswerAsync(answer, Cancellation);

        Assert.Equal(once, (await File(run)).ToJsonString());
        Assert.Single(_followUps.Filed);
    }

    [Fact]
    public async Task A_decision_the_desktop_recorded_after_the_question_wins_over_a_late_answer_and_files_nothing()
    {
        var surface = Surface();
        var run = await Start(surface, prompt: $"/backlog-tools:run-plan-item entry `{GatedEntry}`:\n# Raise notifications");
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);
        await Task.Delay(5, Cancellation);
        await surface.SetRunContextAsync(Worktree, run, approval: "approved", approvalNote: "Looks right.", cancellationToken: Cancellation);

        await Answers(surface).HandleAnswerAsync(Answer(run, "approve-follow-up", "Add the screenshot"), Cancellation);

        var approval = (await File(run))["approval"]!.AsObject();
        Assert.Equal("approved", approval["personalValidation"]!.GetValue<string>());
        Assert.Equal("Looks right.", approval["note"]!.GetValue<string>());
        Assert.Null(approval["followUpEntryId"]);
        Assert.Empty(_followUps.Requests);
    }

    [Fact]
    public async Task A_desktop_decision_taken_while_the_follow_up_is_filed_stands()
    {
        var surface = Surface();
        var run = await Start(surface, prompt: $"/backlog-tools:run-plan-item entry `{GatedEntry}`:\n# Raise notifications");
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        // The run's gate is not held while Tasks files: the desktop can decide meanwhile.
        _followUps.WhileFiling = async () =>
        {
            await Task.Delay(5, timeout.Token);
            await surface.SetRunContextAsync(Worktree, run, approval: "rejected", approvalNote: "Not yet.", cancellationToken: timeout.Token);
        };

        await Answers(surface).HandleAnswerAsync(Answer(run, "approve-follow-up", "Add the screenshot"), timeout.Token);

        var approval = (await File(run))["approval"]!.AsObject();
        Assert.Equal("rejected", approval["personalValidation"]!.GetValue<string>());
        Assert.Equal("Not yet.", approval["note"]!.GetValue<string>());
        Assert.Null(approval["followUpEntryId"]);
    }

    [Fact]
    public async Task A_follow_up_whose_gated_entry_is_gone_records_the_approval_with_the_words_and_no_follow_up()
    {
        var surface = Surface();
        var run = await Start(surface, prompt: $"/backlog-tools:run-plan-item entry `{GatedEntry}`:\n# Raise notifications");
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);
        _followUps.Answer = Error.NotFound("task.not_found", "No such entry.");

        await Answers(surface).HandleAnswerAsync(Answer(run, "approve-follow-up", "Add the screenshot"), Cancellation);

        var approval = (await File(run))["approval"]!.AsObject();
        Assert.Equal("approved", approval["personalValidation"]!.GetValue<string>());
        Assert.Equal("Add the screenshot", approval["note"]!.GetValue<string>());
        Assert.Null(approval["followUpEntryId"]);
    }

    [Fact]
    public async Task A_follow_up_the_store_could_not_file_throws_so_the_answer_is_delivered_again()
    {
        var surface = Surface();
        var run = await Start(surface, prompt: $"/backlog-tools:run-plan-item entry `{GatedEntry}`:\n# Raise notifications");
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);
        var answers = Answers(surface);
        var answer = Answer(run, "approve-follow-up", "Add the screenshot");
        _followUps.Fails = new IOException("database is locked");

        await Assert.ThrowsAsync<IOException>(() => answers.HandleAnswerAsync(answer, Cancellation));
        Assert.Null((await File(run))["approval"]!["personalValidation"]);

        _followUps.Fails = null;
        await answers.HandleAnswerAsync(answer, Cancellation);

        Assert.Equal(_followUps.Filed[answer.NotificationId].ToString("D"), (await File(run))["approval"]!["followUpEntryId"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_run_reads_back_with_its_decision_the_answer_chosen_and_the_follow_up()
    {
        var surface = Surface();
        var run = await Start(surface, prompt: $"/backlog-tools:run-plan-item entry `{GatedEntry}`:\n# Raise notifications");
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);
        var answer = Answer(run, "approve-follow-up", "Add the screenshot");
        await Answers(surface).HandleAnswerAsync(answer, Cancellation);

        var read = await surface.GetRunAsync(Worktree, run, Cancellation);

        Assert.Equal(
            new DeliveryRunApproval("approved", "Add the screenshot", Noon, "approve-follow-up", _followUps.Filed[answer.NotificationId]),
            read!.Approval);
    }

    [Fact]
    public async Task A_decision_the_desktop_records_later_drops_the_answer_chosen_from_the_notification()
    {
        var surface = Surface();
        var run = await Start(surface);
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);
        await Answers(surface).HandleAnswerAsync(Answer(run, "revise", "Split the migration."), Cancellation);

        await surface.SetRunContextAsync(Worktree, run, approval: "pending", cancellationToken: Cancellation);

        var approval = (await surface.GetRunAsync(Worktree, run, Cancellation))!.Approval!;
        Assert.Equal("pending", approval.Decision);
        Assert.Null(approval.Choice);
        Assert.Null(approval.FollowUpEntryId);
    }

    [Fact]
    public async Task A_replayed_answer_changes_nothing_and_the_next_gate_asks_again()
    {
        var surface = Surface();
        var run = await Start(surface);
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);
        var answers = Answers(surface);
        var rejected = Answer(run, "revise", "Split the migration.");

        await answers.HandleAnswerAsync(rejected, Cancellation);
        await surface.SetRunContextAsync(Worktree, run, approval: "pending", cancellationToken: Cancellation);
        await answers.HandleAnswerAsync(rejected, Cancellation);

        Assert.Equal("pending", (await File(run))["approval"]!["personalValidation"]!.GetValue<string>());

        // Back at the gate after the revision: asked again, once.
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);
        Assert.Equal(2, _notifications.Published.Count);
    }

    [Fact]
    public async Task An_answer_for_a_run_that_is_gone_or_a_key_that_names_none_is_dropped()
    {
        var answers = Answers(Surface());

        await answers.HandleAnswerAsync(Answer("run-gone", "approve", null), Cancellation);
        await answers.HandleAnswerAsync(
            new NotificationAnswer(Guid.NewGuid(), new NotificationSource("sessions", "not-a-key"), Guid.NewGuid(), Guid.NewGuid(), ["approve"], null, Noon),
            Cancellation);

        Assert.Empty(_followUps.Requests);
    }

    [Fact]
    public async Task The_gate_moving_on_at_the_desktop_closes_the_unanswered_question()
    {
        var surface = Surface();
        var run = await Start(surface);
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);
        var (id, _) = Assert.Single(_notifications.Published);

        await surface.UpdateStageAsync(Worktree, run, Gate, "done", cancellationToken: Cancellation);
        await surface.UpdateStageAsync(Worktree, run, Gate + 1, "in_progress", cancellationToken: Cancellation);

        var closed = Assert.Single(_notifications.ClosedById);
        Assert.Equal(id, closed.Id);
        Assert.Equal("Validation decided at the desktop", closed.Reason);
    }

    [Fact]
    public async Task An_answered_question_is_not_closed_when_the_gate_moves_on()
    {
        var surface = Surface();
        var run = await Start(surface);
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);
        await Answers(surface).HandleAnswerAsync(Answer(run, "approve", null), Cancellation);

        await surface.UpdateStageAsync(Worktree, run, Gate, "done", cancellationToken: Cancellation);

        Assert.Empty(_notifications.ClosedById);
    }

    [Fact]
    public async Task The_stage_update_stands_when_asking_fails_or_no_notifications_are_composed()
    {
        _notifications.Fails = new InvalidOperationException("locked");
        var failing = Surface();
        var absent = new LocalDeliverySurfaceLifecycle(_home, "machine", "Workshop PC");
        var one = await Start(failing);
        var two = await Start(absent, skill: "flow-spec");

        var updated = await failing.UpdateStageAsync(Worktree, one, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);
        await absent.UpdateStageAsync(Worktree, two, Gate, "in_progress", links: AppLink, cancellationToken: Cancellation);

        Assert.Equal("in_progress", updated.Status);
        Assert.Null((await File(one))["validationQuestion"]?["notificationId"]);
        Assert.Null((await File(two))["validationQuestion"]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }

    private LocalDeliverySurfaceLifecycle Surface(
        ISessionRepositoryResolver? repositories = null,
        ISessionPullRequestStates? pullRequests = null,
        TimeProvider? clock = null) =>
        new(_home, "machine", "Workshop PC", notifications: () => _notifications, repositories: repositories, pullRequests: () => pullRequests, clock: clock);

    private SessionValidationAnswers Answers(LocalDeliverySurfaceLifecycle surface) =>
        new(surface, followUps: _followUps);

    private static async Task<string> Start(LocalDeliverySurfaceLifecycle surface, string? repository = null, string skill = "flow-code", string? prompt = null, string title = "notifications:11 - Raise notifications")
    {
        var run = (await surface.StartRunAsync(Worktree, skill, title, Stages, repository: repository, cancellationToken: Cancellation)).RunId;

        if (prompt is not null) await surface.RecordPromptAsync(Worktree, run, prompt, cancellationToken: Cancellation);

        return run;
    }

    private async Task<JsonObject> File(string runId) =>
        (await new DeliveryRunStore(_home).ReadAsync(Worktree, runId, Cancellation))!;

    private NotificationAnswer Answer(string runId, string? choice, string? text)
    {
        var asked = _notifications.Published.LastOrDefault();
        var id = asked.Draft is null ? Guid.NewGuid() : asked.Id;

        return new NotificationAnswer(
            id,
            new NotificationSource("sessions", SessionNotificationSubjects.ValidationSourceKey(runId, Worktree)),
            Guid.NewGuid(),
            Guid.NewGuid(),
            choice is null ? [] : [choice],
            text,
            Noon);
    }

    /// <summary>GitHub at its boundary: every pull request reads in one state, or the read
    /// fails.</summary>
    private sealed class FixedPullRequestState(SessionPullRequestState? state) : ISessionPullRequestStates
    {
        public List<(string Repository, int Number)> Asked { get; } = [];

        public Exception? Fails { get; init; }

        public Task<SessionPullRequestState?> StateAsync(string repository, int number, CancellationToken cancellationToken = default)
        {
            Asked.Add((repository, number));

            return Fails is { } failure ? Task.FromException<SessionPullRequestState?>(failure) : Task.FromResult(state);
        }
    }

    /// <summary>GitHub at its boundary, slow: every read answers <see cref="State"/> at once,
    /// or — while <see cref="Hang"/> — waits until the test answers it, whatever the token
    /// says, the way a reader that ignores cancellation would.</summary>
    private sealed class HeldPullRequestStates : ISessionPullRequestStates
    {
        private readonly List<TaskCompletionSource<SessionPullRequestState?>> _calls = [];
        private readonly Lock _gate = new();

        public SessionPullRequestState? State { get; set; }

        public bool Hang { get; init; }

        public SemaphoreSlim Entered { get; } = new(0);

        public IReadOnlyList<TaskCompletionSource<SessionPullRequestState?>> Calls
        {
            get
            {
                lock (_gate) return [.. _calls];
            }
        }

        public Task<SessionPullRequestState?> StateAsync(string repository, int number, CancellationToken cancellationToken = default)
        {
            var call = new TaskCompletionSource<SessionPullRequestState?>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate) _calls.Add(call);
            if (!Hang) call.SetResult(State);
            Entered.Release();

            return call.Task;
        }
    }

    /// <summary>The repository port at its boundary: every folder lies in one repository, or
    /// in none.</summary>
    private sealed class FixedRepository(string? repository) : ISessionRepositoryResolver
    {
        public string? RepositoryOf(string workingFolder) => repository;
    }

    /// <summary>
    /// The Tasks port at its boundary, answering as its contract says: idempotent on the
    /// notification id, a set failure when told to, and a throw for a store that cannot be
    /// reached.
    /// </summary>
    private sealed class FakeTaskFollowUps : ITaskFollowUps
    {
        private readonly List<TaskFollowUpRequest> _requests = [];

        public IReadOnlyList<TaskFollowUpRequest> Requests => _requests;

        public Dictionary<Guid, Guid> Filed { get; } = [];

        public Error? Answer { get; set; }

        public Exception? Fails { get; set; }

        public Func<Task>? WhileFiling { get; set; }

        public async Task<Result<Guid>> FileAfterAsync(TaskFollowUpRequest request, CancellationToken cancellationToken = default)
        {
            _requests.Add(request);

            if (WhileFiling is { } meanwhile) await meanwhile().ConfigureAwait(false);
            if (Fails is { } failure) throw failure;
            if (Answer is { } error) return error;

            if (!Filed.TryGetValue(request.NotificationId, out var id))
            {
                id = Guid.NewGuid();
                Filed[request.NotificationId] = id;
            }

            return id;
        }
    }
}
