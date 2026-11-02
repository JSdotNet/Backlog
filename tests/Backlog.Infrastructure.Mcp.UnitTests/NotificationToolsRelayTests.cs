using System.Text.Json;

using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sessions.Abstractions;

using Microsoft.Extensions.Time.Testing;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// <c>ask</c>, <c>get_answer</c> and <c>close</c> over the Sessions question relay (local ADR
/// 0041): an <c>ask</c> with the relay's arguments registers a waiting question and answers its
/// route, id and the dialog wait; <c>get_answer</c> holds on the relay's completion source for
/// at most 55 seconds and answers open, answered with the dialog's answers, or closed with its
/// reason; <c>close</c> takes the waiting question's id and the relay's three reasons. A call
/// with none of the new arguments still goes to Notifications alone.
/// </summary>
public sealed class NotificationToolsRelayTests
{
    private const string Session = "sess-91";
    private const string ToolUse = "toolu_01";

    private static readonly DateTimeOffset Noon = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly AskQuestion Database = new(
        "Which database should the cache use?",
        "Database",
        [new AskOption("SQLite", "A local file"), new AskOption("Postgres", "A server")]);

    private static readonly AskQuestion Tags = new(
        "Which tags apply?",
        "Tags",
        [new AskOption("ui"), new AskOption("api"), new AskOption("docs")],
        MultiSelect: true);

    private readonly FakeTimeProvider _clock = new(Noon);
    private readonly FakeNotifications _notifications = new();
    private readonly FakeQuestionRelay _relay;

    public NotificationToolsRelayTests() => _relay = new FakeQuestionRelay(_clock);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private NotificationTools Tools(INotificationTimingSettings? timings = null) =>
        new(_notifications, _notifications, _relay, timings ?? new FixedTimings(TimeSpan.FromHours(3)));

    // --- ask ------------------------------------------------------------------------------

    [Fact]
    public async Task Ask_with_a_dialog_registers_through_the_relay_and_answers_the_route_the_id_and_the_dialog_wait()
    {
        _relay.IsAway = true;

        var payload = await Tools().AskAsync(sessionId: Session, toolUseId: ToolUse, questions: [Database, Tags], kind: "dialog", cancellationToken: Cancellation);

        var (session, toolUse, questions) = Assert.Single(_relay.Asked);
        Assert.Equal((Session, ToolUse), (session, toolUse));
        RelayQuestion[] expected =
        [
            new("Which database should the cache use?", "Database", [new RelayOption("SQLite", "A local file"), new RelayOption("Postgres", "A server")]),
            new("Which tags apply?", "Tags", [new RelayOption("ui"), new RelayOption("api"), new RelayOption("docs")], MultiSelect: true)
        ];
        Assert.Equal(expected, questions);
        Assert.Equal(_relay.Questions.Keys.Single(), payload.Id);
        Assert.Equal("backlog", payload.Route);
        Assert.Equal(3 * 60 * 60, payload.WaitSeconds);
        Assert.Empty(_notifications.Published);
    }

    [Fact]
    public async Task Ask_at_the_keyboard_answers_the_session_route()
    {
        var payload = await Tools().AskAsync(sessionId: Session, toolUseId: ToolUse, questions: [Database], cancellationToken: Cancellation);

        Assert.Equal("session", payload.Route);
    }

    [Fact]
    public async Task Ask_with_a_prose_question_registers_it_without_a_tool_use()
    {
        var payload = await Tools().AskAsync(sessionId: Session, questions: [new AskQuestion("What should the module be called?")], kind: "prose", cancellationToken: Cancellation);

        var asked = Assert.Single(_relay.Asked);
        Assert.Null(asked.ToolUseId);
        Assert.Equal(WaitingQuestionKind.Prose, _relay.Questions[payload.Id].Kind);
    }

    [Fact]
    public async Task The_dialog_wait_defaults_to_eight_hours_without_timing_settings()
    {
        var payload = await new NotificationTools(_notifications, _notifications, _relay)
            .AskAsync(sessionId: Session, toolUseId: ToolUse, questions: [Database], cancellationToken: Cancellation);

        Assert.Equal(8 * 60 * 60, payload.WaitSeconds);
    }

    [Theory]
    [InlineData(null, "dialog", "relay.tool_use_required")]
    [InlineData(ToolUse, "prose", "relay.kind_invalid")]
    [InlineData(ToolUse, "poll", "relay.kind_invalid")]
    public async Task A_kind_that_does_not_match_the_tool_use_is_refused_before_the_relay_is_asked(string? toolUseId, string kind, string code)
    {
        var failure = await Assert.ThrowsAsync<McpException>(() =>
            Tools().AskAsync(sessionId: Session, toolUseId: toolUseId, questions: [Database], kind: kind, cancellationToken: Cancellation));

        Assert.StartsWith(code, failure.Message, StringComparison.Ordinal);
        Assert.Empty(_relay.Asked);
    }

    [Fact]
    public async Task A_relayed_question_needs_the_session()
    {
        var failure = await Assert.ThrowsAsync<McpException>(() =>
            Tools().AskAsync(toolUseId: ToolUse, questions: [Database], cancellationToken: Cancellation));

        Assert.StartsWith("relay.session_required", failure.Message, StringComparison.Ordinal);
        Assert.Empty(_relay.Asked);
    }

    [Fact]
    public async Task Questions_the_relay_refuses_reach_the_client_with_its_code()
    {
        AskQuestion[] five = [Database, Database, Database, Database, Database];

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            Tools().AskAsync(sessionId: Session, toolUseId: ToolUse, questions: five, cancellationToken: Cancellation));

        Assert.StartsWith("relay.questions_invalid", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_relay_argument_with_no_relay_composed_answers_notifications_not_available()
    {
        var failure = await Assert.ThrowsAsync<McpException>(() =>
            new NotificationTools(_notifications, _notifications).AskAsync(sessionId: Session, toolUseId: ToolUse, questions: [Database], cancellationToken: Cancellation));

        Assert.StartsWith("notifications.not_available", failure.Message, StringComparison.Ordinal);
        Assert.Empty(_notifications.Published);
    }

    [Fact]
    public async Task Ask_without_the_relay_arguments_publishes_through_notifications_as_before_and_never_reaches_the_relay()
    {
        var payload = await Tools().AskAsync("Run the migration?", ["Yes", "No"], sessionId: Session, cancellationToken: Cancellation);

        var (id, draft) = Assert.Single(_notifications.Published);
        Assert.Equal(id, payload.Id);
        Assert.Null(payload.Route);
        Assert.Null(payload.WaitSeconds);
        Assert.Equal(new NotificationSubject("sessions", "session", Session), draft.Subject);
        Assert.Empty(_relay.Asked);
    }

    [Fact]
    public async Task Ask_without_the_relay_arguments_still_needs_its_title()
    {
        var failure = await Assert.ThrowsAsync<McpException>(() => Tools().AskAsync(choices: ["Yes", "No"], cancellationToken: Cancellation));

        Assert.StartsWith("notification.title_required", failure.Message, StringComparison.Ordinal);
    }

    // --- get_answer -----------------------------------------------------------------------

    [Fact]
    public async Task Get_answer_of_an_open_waiting_question_answers_open_and_marks_the_hook_as_still_waiting()
    {
        var id = await AskAsync();

        var payload = await Tools().GetAnswerAsync(id, Session, cancellationToken: Cancellation);

        Assert.Equal((id, "open"), (payload.Id, payload.Status));
        Assert.Null(payload.Answers);
        Assert.Null(payload.Reason);
        Assert.Equal((id, TimeSpan.Zero), Assert.Single(_relay.Waits));
        Assert.Equal(Noon, _relay.Questions[id].HookPolledAt);
    }

    [Fact]
    public async Task A_held_get_answer_is_released_early_by_the_answer_with_answers_keyed_by_question_text()
    {
        var id = await AskAsync(Database, Tags);

        var held = Tools().GetAnswerAsync(id, Session, waitSeconds: 50, cancellationToken: Cancellation);
        Assert.False(held.IsCompleted, "The call holds while nobody has answered.");

        _clock.Advance(TimeSpan.FromSeconds(5));
        Assert.False(held.IsCompleted);

        _relay.Answer(id, new RelayAnswer(["Postgres"]), new RelayAnswer(["ui", "docs"]));
        var payload = await held;

        Assert.Equal("answered", payload.Status);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Which database should the cache use?"] = "Postgres",
                ["Which tags apply?"] = "ui, docs"
            },
            payload.Answers);
        Assert.Equal(Noon + TimeSpan.FromSeconds(5), payload.AnsweredAt);
        Assert.Null(payload.Reason);
    }

    [Fact]
    public async Task An_open_answer_comes_back_as_its_text()
    {
        var id = await AskAsync(Database);
        _relay.Answer(id, RelayAnswer.Open("Redis, for now"));

        var payload = await Tools().GetAnswerAsync(id, Session, cancellationToken: Cancellation);

        Assert.Equal("Redis, for now", payload.Answers!["Which database should the cache use?"]);
    }

    [Fact]
    public async Task A_held_get_answer_is_released_early_by_a_close_with_its_reason()
    {
        var id = await AskAsync();
        var tools = Tools();

        var held = tools.GetAnswerAsync(id, Session, waitSeconds: 50, cancellationToken: Cancellation);
        Assert.False(held.IsCompleted);

        await tools.CloseAsync(id, "session-ended", Session, cancellationToken: Cancellation);
        var payload = await held;

        Assert.Equal(("closed", "session-ended"), (payload.Status, payload.Reason));
        Assert.Null(payload.Answers);
    }

    [Fact]
    public async Task A_held_get_answer_answers_open_once_its_wait_ends()
    {
        var id = await AskAsync();

        var held = Tools().GetAnswerAsync(id, Session, waitSeconds: 20, cancellationToken: Cancellation);

        _clock.Advance(TimeSpan.FromSeconds(19));
        Assert.False(held.IsCompleted);

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("open", (await held).Status);
    }

    [Theory]
    [InlineData(55)]
    [InlineData(56)]
    [InlineData(600)]
    public async Task One_get_answer_holds_at_most_55_seconds(int waitSeconds)
    {
        var id = await AskAsync();

        var held = Tools().GetAnswerAsync(id, Session, waitSeconds: waitSeconds, cancellationToken: Cancellation);

        Assert.Equal(TimeSpan.FromSeconds(55), Assert.Single(_relay.Waits).Wait);
        _clock.Advance(TimeSpan.FromSeconds(54));
        Assert.False(held.IsCompleted);

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("open", (await held).Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_wait_under_a_second_is_refused(int waitSeconds)
    {
        var id = await AskAsync();

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools().GetAnswerAsync(id, Session, waitSeconds: waitSeconds, cancellationToken: Cancellation));

        Assert.StartsWith("relay.wait_invalid", failure.Message, StringComparison.Ordinal);
        Assert.Empty(_relay.Waits);
    }

    [Fact]
    public async Task Another_sessions_waiting_question_is_not_found()
    {
        var id = await AskAsync();

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools().GetAnswerAsync(id, "sess-other", cancellationToken: Cancellation));

        Assert.StartsWith("notification.not_found", failure.Message, StringComparison.Ordinal);
        Assert.Empty(_relay.Waits);
    }

    [Fact]
    public async Task Get_answer_of_a_notification_id_still_answers_from_notifications()
    {
        var id = _notifications.Seed(NotificationState.Open, subject: new NotificationSubject("sessions", "session", Session));

        var payload = await Tools().GetAnswerAsync(id, Session, cancellationToken: Cancellation);

        Assert.Equal("pending", payload.Status);
        Assert.Null(payload.Answers);
        Assert.Empty(_relay.Waits);
    }

    // --- close ----------------------------------------------------------------------------

    [Fact]
    public async Task Close_answered_in_the_session_reads_the_dialogs_answers_back_into_labels()
    {
        var id = await AskAsync(Database, Tags);

        var payload = await Tools().CloseAsync(
            id,
            "answered-in-session",
            Session,
            new Dictionary<string, string>
            {
                ["Which database should the cache use?"] = "SQLite",
                ["Which tags apply?"] = "api, docs"
            },
            Cancellation);

        var (closed, reason, answers) = Assert.Single(_relay.Closes);
        Assert.Equal((id, WaitingQuestionCloseReason.AnsweredInSession), (closed, reason));
        RelayAnswer[] expected = [new(["SQLite"]), new(["api", "docs"])];
        Assert.Equal(expected, answers!);
        Assert.Equal(("answered", "answered-in-session"), (payload.Status, payload.Reason));
        Assert.Equal("api, docs", payload.Answers!["Which tags apply?"]);
    }

    [Fact]
    public async Task An_answer_in_the_session_that_names_no_option_is_kept_as_open_text()
    {
        var id = await AskAsync(Database);

        await Tools().CloseAsync(id, "answered-in-session", Session, new Dictionary<string, string> { ["Which database should the cache use?"] = "Neither, use Redis" }, Cancellation);

        RelayAnswer[] expected = [RelayAnswer.Open("Neither, use Redis")];
        Assert.Equal(expected, Assert.Single(_relay.Closes).Answers!);
    }

    [Fact]
    public async Task Answered_in_the_session_without_every_answer_is_refused_by_the_relay()
    {
        var id = await AskAsync(Database, Tags);

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools().CloseAsync(
            id, "answered-in-session", Session, new Dictionary<string, string> { ["Which tags apply?"] = "ui" }, Cancellation));

        Assert.StartsWith("relay.answers_invalid", failure.Message, StringComparison.Ordinal);
        Assert.True(_relay.Questions[id].IsOpen);
    }

    [Theory]
    [InlineData("timed-out", WaitingQuestionCloseReason.TimedOut)]
    [InlineData("session-ended", WaitingQuestionCloseReason.SessionEnded)]
    public async Task Close_takes_the_relays_reasons(string spelling, WaitingQuestionCloseReason reason)
    {
        var id = await AskAsync();

        var payload = await Tools().CloseAsync(id, spelling, Session, cancellationToken: Cancellation);

        Assert.Equal(reason, Assert.Single(_relay.Closes).Reason);
        Assert.Equal(("closed", spelling), (payload.Status, payload.Reason));
        Assert.Empty(_notifications.Closed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Decided without it")]
    public async Task A_waiting_question_refuses_a_reason_that_is_not_the_relays(string? reason)
    {
        var id = await AskAsync();

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools().CloseAsync(id, reason, Session, cancellationToken: Cancellation));

        Assert.StartsWith("relay.reason_invalid", failure.Message, StringComparison.Ordinal);
        Assert.Empty(_relay.Closes);
    }

    [Fact]
    public async Task Closing_a_question_already_ended_reaches_the_client_with_its_code()
    {
        var id = await AskAsync();
        await Tools().CloseAsync(id, "timed-out", Session, cancellationToken: Cancellation);

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools().CloseAsync(id, "timed-out", Session, cancellationToken: Cancellation));

        Assert.StartsWith("relay.already_ended", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Close_of_a_notification_id_still_withdraws_it_through_notifications()
    {
        var id = _notifications.Seed(NotificationState.Open, subject: new NotificationSubject("sessions", "session", Session));

        var payload = await Tools().CloseAsync(id, "Decided without it", Session, cancellationToken: Cancellation);

        Assert.Equal((id, "Decided without it"), Assert.Single(_notifications.Closed));
        Assert.Equal("closed", payload.Status);
        Assert.Empty(_relay.Closes);
    }

    // --- the wire ---------------------------------------------------------------------------

    [Fact]
    public void A_payload_without_the_relays_fields_serializes_as_before()
    {
        var id = Guid.NewGuid();

        var published = JsonSerializer.Serialize(new NotificationPublishedPayload(id), McpJsonUtilities.DefaultOptions);
        var answer = JsonSerializer.Serialize(new NotificationAnswerPayload(id, "pending", [], null, null, null), McpJsonUtilities.DefaultOptions);

        Assert.Equal($"{{\"id\":\"{id:D}\"}}", published);
        Assert.Equal($"{{\"id\":\"{id:D}\",\"status\":\"pending\",\"choices\":[]}}", answer);
    }

    [Fact]
    public void The_answers_are_keyed_by_the_question_text_as_written()
    {
        var payload = new NotificationAnswerPayload(Guid.NewGuid(), "answered", [], null, Noon, null, Answers: new Dictionary<string, string> { ["Which Tags apply?"] = "ui, docs" });

        var json = JsonSerializer.Serialize(payload, McpJsonUtilities.DefaultOptions);

        Assert.Contains("\"answers\":{\"Which Tags apply?\":\"ui, docs\"}", json, StringComparison.Ordinal);
    }

    private async Task<Guid> AskAsync(params AskQuestion[] questions) =>
        (await Tools().AskAsync(
            sessionId: Session,
            toolUseId: ToolUse,
            questions: questions.Length == 0 ? [Database] : questions,
            cancellationToken: Cancellation)).Id;

    private sealed class FixedTimings(TimeSpan dialogWait) : INotificationTimingSettings
    {
        public TimeSpan AwayAfter => TimeSpan.FromMinutes(10);

        public TimeSpan NotifyAfter => TimeSpan.FromMinutes(15);

        public TimeSpan DialogWait => dialogWait;

        public string? Set(TimeSpan awayAfter, TimeSpan notifyAfter, TimeSpan dialogWait) => null;

        public event Action? Changed
        {
            add { }
            remove { }
        }
    }
}

/// <summary>
/// The Sessions relay's port over waiting questions held in memory, with the domain's own
/// transitions. A held wait awaits a completion source that <see cref="Answer"/> or a close
/// completes, as the relay's does.
/// </summary>
internal sealed class FakeQuestionRelay(TimeProvider clock) : IQuestionRelay
{
    private readonly Dictionary<Guid, TaskCompletionSource> _ended = [];

    public bool IsAway { get; set; }

    public Dictionary<Guid, WaitingQuestion> Questions { get; } = [];

    public List<(string SessionId, string? ToolUseId, IReadOnlyList<RelayQuestion> Questions)> Asked { get; } = [];

    public List<(Guid Id, TimeSpan Wait)> Waits { get; } = [];

    public List<(Guid Id, WaitingQuestionCloseReason Reason, IReadOnlyList<RelayAnswer>? Answers)> Closes { get; } = [];

    /// <summary>Records the accepted answers to every question, as Backlog would.</summary>
    public void Answer(Guid id, params RelayAnswer[] answers)
    {
        Questions[id] = Questions[id] with
        {
            State = WaitingQuestionState.Answered,
            Answers = answers,
            EndedAt = clock.GetUtcNow()
        };
        Release(id);
    }

    public Task<QuestionRelayResult> AskAsync(string sessionId, string? toolUseId, IReadOnlyList<RelayQuestion> questions, CancellationToken cancellationToken = default)
    {
        Asked.Add((sessionId, toolUseId, questions));

        var kind = toolUseId is null ? WaitingQuestionKind.Prose : WaitingQuestionKind.Dialog;
        if (WaitingQuestions.Problem(kind, questions) is { } problem)
            return Task.FromResult(QuestionRelayResult.Refused("relay.questions_invalid", problem));

        var question = WaitingQuestions.Ask(sessionId, toolUseId, questions, IsAway, TimeSpan.FromMinutes(15), clock.GetUtcNow());
        Questions[question.Id] = question;

        return Task.FromResult(QuestionRelayResult.Of(question));
    }

    public Task<WaitingQuestion?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Questions.GetValueOrDefault(id));

    public async Task<WaitingQuestion?> WaitAsync(Guid id, TimeSpan wait, CancellationToken cancellationToken = default)
    {
        Waits.Add((id, wait));

        if (!Questions.TryGetValue(id, out var question)) return null;
        if (!question.IsOpen) return question;

        Questions[id] = WaitingQuestions.Polled(question, clock.GetUtcNow());
        if (wait <= TimeSpan.Zero) return Questions[id];

        if (!_ended.TryGetValue(id, out var ended)) _ended[id] = ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await ended.Task.WaitAsync(wait, clock, cancellationToken);
        }
        catch (TimeoutException)
        {
        }

        return Questions[id];
    }

    public Task<QuestionRelayResult> CloseAsync(Guid id, WaitingQuestionCloseReason reason, IReadOnlyList<RelayAnswer>? answers = null, CancellationToken cancellationToken = default)
    {
        Closes.Add((id, reason, answers));

        var outcome = WaitingQuestions.Close(Questions[id], reason, answers, clock.GetUtcNow());
        if (!outcome.Succeeded)
        {
            return Task.FromResult(QuestionRelayResult.Refused(
                outcome.Question!.IsOpen ? "relay.answers_invalid" : "relay.already_ended",
                outcome.Refusal!));
        }

        Questions[id] = outcome.Question!;
        Release(id);

        return Task.FromResult(QuestionRelayResult.Of(outcome.Question!));
    }

    private void Release(Guid id)
    {
        if (_ended.Remove(id, out var ended)) ended.TrySetResult();
    }
}
