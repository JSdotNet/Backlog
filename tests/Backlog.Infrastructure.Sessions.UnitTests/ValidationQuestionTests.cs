using System.Text.Json.Nodes;
using Backlog.Infrastructure.Sessions;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Tests;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// Sessions asking the person, in a notification, to approve a delivery run's Personal
/// Validation, and recording the answer on the run as its gate decision
/// (<c>.devbook/domain/notifications/context.md</c>).
/// </summary>
public sealed class ValidationQuestionTests : IDisposable
{
    private const string Worktree = @"D:\n\9b960f5e";
    private const int Gate = 2;
    private static readonly string[] Stages = ["Implement", "Ready", "Personal Validation", "Create Pull Request"];
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly string _home = Path.Combine(Path.GetTempPath(), "backlog-validation-question-tests", Guid.NewGuid().ToString("n"));
    private readonly RecordingNotifications _notifications = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reaching_personal_validation_asks_to_approve_the_run_with_approve_reject_and_an_open_answer()
    {
        var surface = Surface();
        var run = await Start(surface, repository: "JSdotNet/Backlog");

        await surface.UpdateStageAsync(Worktree, run, 1, "done", cancellationToken: Cancellation);
        Assert.Empty(_notifications.Published);

        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", cancellationToken: Cancellation);

        var (id, draft) = Assert.Single(_notifications.Published);
        Assert.Equal(NotificationKind.Question, draft.Kind);
        Assert.Equal("Approve the validation of notifications:11 - Raise notifications?", draft.Title);
        Assert.Equal(new NotificationSubject("sessions", "run", run), draft.Subject);
        Assert.Equal(new NotificationSource("sessions", $"{run}@{Worktree}"), draft.Source);
        var spec = Assert.IsType<AnswerSpec>(draft.Answer);
        Assert.Equal(["approve", "reject"], spec.Choices.Select(choice => choice.Id));
        Assert.Equal(["Approve", "Reject"], spec.Choices.Select(choice => choice.Label));
        Assert.NotNull(spec.Open);
        Assert.Equal(AnswerOn.AnyInstance, spec.AnswerOn);
        Assert.Contains("JSdotNet/Backlog", draft.BodyMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain(Worktree, draft.BodyMarkdown, StringComparison.Ordinal);

        var asked = (await File(run))["validationQuestion"]!.AsObject();
        Assert.Equal(id.ToString("D"), asked["notificationId"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_gate_reported_twice_while_its_question_is_open_asks_once()
    {
        var surface = Surface();
        var run = await Start(surface);

        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", cancellationToken: Cancellation);
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", cancellationToken: Cancellation);

        Assert.Single(_notifications.Published);
    }

    [Fact]
    public async Task Re_reporting_the_gate_after_it_was_approved_asks_nothing_more()
    {
        var surface = Surface();
        var run = await Start(surface);
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", cancellationToken: Cancellation);
        await new SessionValidationAnswers(surface).HandleAnswerAsync(Answer(run, "approve", null), Cancellation);

        await surface.UpdateStageAsync(
            Worktree, run, Gate, "in_progress",
            links: [new DeliveryStageLink("App", "http://localhost:5000", "The running application")],
            cancellationToken: Cancellation);

        Assert.Single(_notifications.Published);
    }

    [Fact]
    public async Task A_decision_the_desktop_recorded_after_the_question_wins_over_a_late_answer()
    {
        var surface = Surface();
        var run = await Start(surface);
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", cancellationToken: Cancellation);
        await Task.Delay(5, Cancellation);
        await surface.SetRunContextAsync(Worktree, run, approval: "approved", approvalNote: "Looks right.", cancellationToken: Cancellation);

        await new SessionValidationAnswers(surface).HandleAnswerAsync(Answer(run, "reject", null), Cancellation);

        var approval = (await File(run))["approval"]!.AsObject();
        Assert.Equal("approved", approval["personalValidation"]!.GetValue<string>());
        Assert.Equal("Looks right.", approval["note"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("approve", null, "approved", "Approved from a Backlog notification.")]
    [InlineData("reject", null, "rejected", "Rejected from a Backlog notification.")]
    [InlineData(null, "Rename the setting first.", "rejected", "Rename the setting first.")]
    public async Task The_answer_is_recorded_on_the_run_as_its_gate_decision(string? choice, string? text, string decision, string note)
    {
        var surface = Surface();
        var run = await Start(surface);
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", cancellationToken: Cancellation);
        var answers = new SessionValidationAnswers(surface);

        await answers.HandleAnswerAsync(Answer(run, choice, text), Cancellation);

        var approval = (await File(run))["approval"]!.AsObject();
        Assert.Equal(decision, approval["personalValidation"]!.GetValue<string>());
        Assert.Equal(note, approval["note"]!.GetValue<string>());
        Assert.Equal("2026-10-22T12:00:00.000Z", approval["decidedAt"]!.GetValue<string>());
        Assert.Equal("sessions", answers.Source);
    }

    [Fact]
    public async Task A_replayed_answer_changes_nothing_and_the_next_gate_asks_again()
    {
        var surface = Surface();
        var run = await Start(surface);
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", cancellationToken: Cancellation);
        var answers = new SessionValidationAnswers(surface);
        var rejected = Answer(run, null, "Split the migration.");

        await answers.HandleAnswerAsync(rejected, Cancellation);
        await surface.SetRunContextAsync(Worktree, run, approval: "pending", cancellationToken: Cancellation);
        await answers.HandleAnswerAsync(rejected, Cancellation);

        Assert.Equal("pending", (await File(run))["approval"]!["personalValidation"]!.GetValue<string>());

        // Back at the gate after the revision: asked again, once.
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", cancellationToken: Cancellation);
        Assert.Equal(2, _notifications.Published.Count);
    }

    [Fact]
    public async Task An_answer_for_a_run_that_is_gone_or_a_key_that_names_none_is_dropped()
    {
        var answers = new SessionValidationAnswers(Surface());

        await answers.HandleAnswerAsync(Answer("run-gone", "approve", null), Cancellation);
        await answers.HandleAnswerAsync(
            new NotificationAnswer(Guid.NewGuid(), new NotificationSource("sessions", "not-a-key"), Guid.NewGuid(), Guid.NewGuid(), ["approve"], null, Noon),
            Cancellation);
    }

    [Fact]
    public async Task The_gate_moving_on_at_the_desktop_closes_the_unanswered_question()
    {
        var surface = Surface();
        var run = await Start(surface);
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", cancellationToken: Cancellation);
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
        await surface.UpdateStageAsync(Worktree, run, Gate, "in_progress", cancellationToken: Cancellation);
        await new SessionValidationAnswers(surface).HandleAnswerAsync(Answer(run, "approve", null), Cancellation);

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

        var updated = await failing.UpdateStageAsync(Worktree, one, Gate, "in_progress", cancellationToken: Cancellation);
        await absent.UpdateStageAsync(Worktree, two, Gate, "in_progress", cancellationToken: Cancellation);

        Assert.Equal("in_progress", updated.Status);
        Assert.Null((await File(one))["validationQuestion"]);
        Assert.Null((await File(two))["validationQuestion"]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }

    private LocalDeliverySurfaceLifecycle Surface() =>
        new(_home, "machine", "Workshop PC", notifications: () => _notifications);

    private static async Task<string> Start(LocalDeliverySurfaceLifecycle surface, string? repository = null, string skill = "flow-code") =>
        (await surface.StartRunAsync(Worktree, skill, "notifications:11 - Raise notifications", Stages, repository: repository, cancellationToken: Cancellation)).RunId;

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
}
