using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel.Results;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// <c>notify</c>, <c>ask</c>, <c>get_answer</c> and <c>close</c>: each hands its arguments to
/// Notifications' <see cref="INotifications"/> port as an <c>mcp</c> source keyed by the call,
/// names the calling session as the subject when it is given one, answers with the notification's
/// id or where it stands, and turns a refusal into the failure a client sees.
/// </summary>
public sealed class NotificationToolsTests
{
    private const string Session = "sess-91";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // --- notify ---------------------------------------------------------------------------

    [Fact]
    public async Task Notify_publishes_an_alert_from_the_mcp_source_about_the_calling_session()
    {
        var port = new FakeNotifications();

        var payload = await Tools(port).NotifyAsync("Migration done", "0042 applied", "**All** rows moved.", "warning", Session, Cancellation);

        var draft = Assert.Single(port.Published).Draft;
        Assert.Equal(port.Published[0].Id, payload.Id);
        Assert.Equal(NotificationKind.Alert, draft.Kind);
        Assert.Equal(NotificationSeverity.Warning, draft.Severity);
        Assert.Equal(("Migration done", "0042 applied", "**All** rows moved."), (draft.Title, draft.Summary, draft.BodyMarkdown));
        Assert.Equal("mcp", draft.Source.Context);
        Assert.True(Guid.TryParse(draft.Source.Key, out _), "The source key is the call's own id.");
        Assert.Equal(new NotificationSubject("sessions", "session", Session), draft.Subject);
        Assert.Null(draft.Answer);
    }

    [Fact]
    public async Task Notify_without_a_session_is_about_nothing_and_info_by_default()
    {
        var port = new FakeNotifications();

        await Tools(port).NotifyAsync("Heads up", cancellationToken: Cancellation);

        var draft = Assert.Single(port.Published).Draft;
        Assert.Null(draft.Subject);
        Assert.Equal(NotificationSeverity.Info, draft.Severity);
        Assert.Equal((string.Empty, string.Empty), (draft.Summary, draft.BodyMarkdown));
    }

    [Fact]
    public async Task Every_call_is_its_own_source_key()
    {
        var port = new FakeNotifications();
        var tools = Tools(port);

        await tools.NotifyAsync("One", cancellationToken: Cancellation);
        await tools.NotifyAsync("Two", cancellationToken: Cancellation);

        Assert.NotEqual(port.Published[0].Draft.Source.Key, port.Published[1].Draft.Source.Key);
    }

    [Theory]
    [InlineData("loud")]
    [InlineData("error")]
    public async Task An_unknown_severity_is_refused_before_the_port_is_asked(string severity)
    {
        var port = new FakeNotifications();

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools(port).NotifyAsync("Heads up", severity: severity, cancellationToken: Cancellation));

        Assert.StartsWith("notification.unknown_severity", failure.Message, StringComparison.Ordinal);
        Assert.Empty(port.Published);
    }

    [Fact]
    public async Task A_refused_publish_reaches_the_client_with_its_code()
    {
        var port = new FakeNotifications { PublishAnswer = NotificationsErrors.NotAvailable() };

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools(port).NotifyAsync("Heads up", cancellationToken: Cancellation));

        Assert.StartsWith("notifications.not_available", failure.Message, StringComparison.Ordinal);
    }

    // --- ask ------------------------------------------------------------------------------

    [Fact]
    public async Task Ask_publishes_a_single_choice_question_about_the_calling_session_and_answers_its_id_at_once()
    {
        var port = new FakeNotifications();

        var payload = await Tools(port).AskAsync(
            "Run the migration against staging?",
            [" Yes ", "No"],
            summary: "0042_add_device_kind",
            severity: "critical",
            sessionId: Session,
            cancellationToken: Cancellation);

        var (id, draft) = Assert.Single(port.Published);
        Assert.Equal(id, payload.Id);
        Assert.Equal(NotificationKind.Question, draft.Kind);
        Assert.Equal(NotificationSeverity.Critical, draft.Severity);
        Assert.Equal("mcp", draft.Source.Context);
        Assert.Equal(new NotificationSubject("sessions", "session", Session), draft.Subject);

        var spec = draft.Answer!;
        Assert.Equal([new AnswerChoice("Yes", "Yes"), new AnswerChoice("No", "No")], spec.Choices);
        Assert.False(spec.AllowMultiple);
        Assert.Null(spec.Open);

        // Never blocks: the port was asked to publish and nothing else, so nothing waited on an answer.
        Assert.Empty(port.Closed);
    }

    [Fact]
    public async Task Ask_without_a_session_is_about_the_question_itself()
    {
        var port = new FakeNotifications();

        await Tools(port).AskAsync("Pick a name", ["Signals", "Pulse"], cancellationToken: Cancellation);

        var draft = Assert.Single(port.Published).Draft;
        Assert.Equal(new NotificationSubject("mcp", "ask", draft.Source.Key), draft.Subject);
    }

    [Fact]
    public async Task Ask_carries_allow_multiple()
    {
        var port = new FakeNotifications();

        await Tools(port).AskAsync("Which journeys should QA rerun?", ["Pair", "Capture", "Roadmap"], allowMultiple: true, cancellationToken: Cancellation);

        var spec = Assert.Single(port.Published).Draft.Answer!;
        Assert.True(spec.AllowMultiple);
        Assert.Equal(3, spec.Choices.Count);
    }

    [Fact]
    public async Task Ask_with_choices_and_an_open_answer_carries_both()
    {
        var port = new FakeNotifications();

        await Tools(port).AskAsync("Pick a name for the module, or suggest one", ["Signals", "Pulse", "Alerts"], openAnswer: true, cancellationToken: Cancellation);

        var spec = Assert.Single(port.Published).Draft.Answer!;
        Assert.Equal(3, spec.Choices.Count);
        Assert.Equal(new OpenAnswer(), spec.Open);
    }

    [Fact]
    public async Task Ask_with_only_a_placeholder_is_an_open_question()
    {
        var port = new FakeNotifications();

        await Tools(port).AskAsync("What should the module be called?", placeholder: "Name the module", cancellationToken: Cancellation);

        var spec = Assert.Single(port.Published).Draft.Answer!;
        Assert.Empty(spec.Choices);
        Assert.Equal("Name the module", spec.Open!.Placeholder);
    }

    [Fact]
    public async Task A_question_the_rules_refuse_reaches_the_client_with_its_code()
    {
        var port = new FakeNotifications
        {
            PublishAnswer = NotificationsErrors.InvalidAnswerSpec("A question offers 2 to 6 set choices.")
        };

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools(port).AskAsync("Go?", ["Yes"], cancellationToken: Cancellation));

        Assert.StartsWith("notification.invalid_answer_spec", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Yes", " ")]
    [InlineData("Yes", null)]
    public async Task A_choice_without_a_label_is_refused_before_the_port_is_asked(string first, string? second)
    {
        var port = new FakeNotifications();

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools(port).AskAsync("Go?", [first, second!], cancellationToken: Cancellation));

        Assert.StartsWith("notification.invalid_answer_spec", failure.Message, StringComparison.Ordinal);
        Assert.Empty(port.Published);
    }

    [Fact]
    public async Task Two_choices_with_one_label_are_refused_whatever_their_case()
    {
        var port = new FakeNotifications();

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools(port).AskAsync("Go?", ["Yes", "yes "], cancellationToken: Cancellation));

        Assert.Contains("own label", failure.Message, StringComparison.Ordinal);
        Assert.Empty(port.Published);
    }

    // --- get_answer -----------------------------------------------------------------------

    [Fact]
    public async Task Get_answer_is_pending_while_nobody_answered()
    {
        var port = new FakeNotifications();
        var id = port.Seed(NotificationState.Open);

        var payload = await Tools(port).GetAnswerAsync(id, cancellationToken: Cancellation);

        Assert.Equal((id, "pending"), (payload.Id, payload.Status));
        Assert.Empty(payload.Choices);
        Assert.Null(payload.Text);
        Assert.Null(payload.AnsweredAt);
        Assert.Null(payload.Reason);
    }

    [Fact]
    public async Task Get_answer_answers_the_chosen_choices_and_the_text()
    {
        var port = new FakeNotifications();
        var at = new DateTimeOffset(2026, 10, 22, 9, 30, 0, TimeSpan.Zero);
        var id = port.Seed(NotificationState.Answered, answer: (["Signals"], "because it is short", at));

        var payload = await Tools(port).GetAnswerAsync(id, cancellationToken: Cancellation);

        Assert.Equal("answered", payload.Status);
        Assert.Equal(["Signals"], payload.Choices);
        Assert.Equal("because it is short", payload.Text);
        Assert.Equal(at, payload.AnsweredAt);
        Assert.Null(payload.Reason);
    }

    [Fact]
    public async Task Get_answer_answers_closed_with_its_reason()
    {
        var port = new FakeNotifications();
        var id = port.Seed(NotificationState.Closed, closeReason: "Session archived");

        var payload = await Tools(port).GetAnswerAsync(id, cancellationToken: Cancellation);

        Assert.Equal(("closed", "Session archived"), (payload.Status, payload.Reason));
    }

    [Fact]
    public async Task Get_answer_answers_an_acknowledged_alert()
    {
        var port = new FakeNotifications();
        var id = port.Seed(NotificationState.Acknowledged, kind: NotificationKind.Alert);

        Assert.Equal("acknowledged", (await Tools(port).GetAnswerAsync(id, cancellationToken: Cancellation)).Status);
    }

    [Fact]
    public async Task Get_answer_refuses_a_notification_another_context_raised_as_not_found()
    {
        var port = new FakeNotifications();
        var id = port.Seed(NotificationState.Answered, source: new NotificationSource("sync", "pair-7"));

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools(port).GetAnswerAsync(id, cancellationToken: Cancellation));

        Assert.StartsWith("notification.not_found", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_answer_refuses_another_sessions_question_when_the_caller_names_its_own()
    {
        var port = new FakeNotifications();
        var id = port.Seed(NotificationState.Open, subject: new NotificationSubject("sessions", "session", "sess-other"));

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools(port).GetAnswerAsync(id, Session, Cancellation));

        Assert.StartsWith("notification.not_found", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_answer_refuses_a_sessions_question_to_a_caller_that_names_no_session()
    {
        var port = new FakeNotifications();
        var id = port.Seed(NotificationState.Open, subject: new NotificationSubject("sessions", "session", Session));

        await Assert.ThrowsAsync<McpException>(() => Tools(port).GetAnswerAsync(id, cancellationToken: Cancellation));
        Assert.Equal("pending", (await Tools(port).GetAnswerAsync(id, Session, Cancellation)).Status);
    }

    [Fact]
    public async Task A_question_asked_without_a_session_answers_whoever_holds_its_id()
    {
        var port = new FakeNotifications();
        var tools = Tools(port);
        var id = (await tools.AskAsync("Pick a name", ["Signals", "Pulse"], cancellationToken: Cancellation)).Id;

        Assert.Equal("pending", (await tools.GetAnswerAsync(id, cancellationToken: Cancellation)).Status);
        Assert.Equal("pending", (await tools.GetAnswerAsync(id, Session, Cancellation)).Status);
    }

    [Fact]
    public async Task Get_answer_of_an_unknown_id_is_not_found()
    {
        var failure = await Assert.ThrowsAsync<McpException>(() => Tools(new FakeNotifications()).GetAnswerAsync(Guid.NewGuid(), cancellationToken: Cancellation));

        Assert.StartsWith("notification.not_found", failure.Message, StringComparison.Ordinal);
    }

    // --- close ----------------------------------------------------------------------------

    [Fact]
    public async Task Close_withdraws_a_notification_this_server_published_with_its_reason()
    {
        var port = new FakeNotifications();
        var id = port.Seed(NotificationState.Open, subject: new NotificationSubject("sessions", "session", Session));

        var payload = await Tools(port).CloseAsync(id, "Decided without it", Session, Cancellation);

        Assert.Equal((id, "Decided without it"), Assert.Single(port.Closed));
        Assert.Equal(("closed", "Decided without it"), (payload.Status, payload.Reason));
    }

    [Fact]
    public async Task Close_refuses_a_notification_another_context_raised_and_closes_nothing()
    {
        var port = new FakeNotifications();
        var id = port.Seed(NotificationState.Open, source: new NotificationSource("devbook", "remark-3"));

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools(port).CloseAsync(id, cancellationToken: Cancellation));

        Assert.StartsWith("notification.not_found", failure.Message, StringComparison.Ordinal);
        Assert.Empty(port.Closed);
    }

    [Fact]
    public async Task Close_refuses_another_sessions_notification_when_the_caller_names_its_own()
    {
        var port = new FakeNotifications();
        var id = port.Seed(NotificationState.Open, subject: new NotificationSubject("sessions", "session", "sess-other"));

        await Assert.ThrowsAsync<McpException>(() => Tools(port).CloseAsync(id, sessionId: Session, cancellationToken: Cancellation));

        Assert.Empty(port.Closed);
    }

    [Fact]
    public async Task Close_withdraws_an_alert_too()
    {
        var port = new FakeNotifications();
        var tools = Tools(port);
        var id = (await tools.NotifyAsync("Heads up", cancellationToken: Cancellation)).Id;

        var payload = await tools.CloseAsync(id, cancellationToken: Cancellation);

        Assert.Equal("closed", payload.Status);
        Assert.Null(payload.Reason);
    }

    [Fact]
    public async Task Closing_one_already_settled_reaches_the_client_with_its_code()
    {
        var port = new FakeNotifications();
        var id = port.Seed(NotificationState.Answered, answer: (["Yes"], null, DateTimeOffset.UnixEpoch));

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools(port).CloseAsync(id, cancellationToken: Cancellation));

        Assert.StartsWith("notification.already_settled", failure.Message, StringComparison.Ordinal);
    }

    // --- the group ------------------------------------------------------------------------

    [Fact]
    public void The_tools_follow_the_notifications_switch_alone()
    {
        var features = new FakeAppFeatureSettings([.. BacklogMcpTools.Groups.Select(group => group.FeatureKey)]);

        features.SetEnabled(NotificationsFeatures.Notifications, enabled: false);

        Assert.All(BacklogMcpTools.Notifications.ToolNames, name => Assert.False(BacklogMcpTools.IsExposed(name, features)));
        Assert.All(
            BacklogMcpTools.Groups.Where(group => group != BacklogMcpTools.Notifications).SelectMany(group => group.ToolNames),
            name => Assert.True(BacklogMcpTools.IsExposed(name, features)));
    }

    [Fact]
    public async Task The_answer_handler_takes_the_mcp_source_and_does_nothing_with_it()
    {
        var handler = new McpNotificationAnswers();

        Assert.Equal("mcp", handler.Source);
        await handler.HandleAnswerAsync(
            new NotificationAnswer(Guid.NewGuid(), new NotificationSource("mcp", "k"), Guid.NewGuid(), Guid.NewGuid(), ["Yes"], null, DateTimeOffset.UnixEpoch),
            Cancellation);
    }

    private static NotificationTools Tools(FakeNotifications port) => new(port, port);
}

/// <summary>Notifications' ports, recording what a tool asked of them, over an in-memory set of
/// notifications a test seeds.</summary>
internal sealed class FakeNotifications : INotifications, INotificationStatuses
{
    private readonly Dictionary<Guid, NotificationStatus> _statuses = [];

    public List<(Guid Id, NotificationDraft Draft)> Published { get; } = [];

    public List<(Guid Id, string? Reason)> Closed { get; } = [];

    public Error? PublishAnswer { get; init; }

    /// <summary>Stores a notification as the module would have, and answers its id.</summary>
    public Guid Seed(
        NotificationState state,
        NotificationSource? source = null,
        NotificationSubject? subject = null,
        NotificationKind kind = NotificationKind.Question,
        string? closeReason = null,
        (IReadOnlyList<string> Choices, string? Text, DateTimeOffset At)? answer = null)
    {
        var id = Guid.NewGuid();
        source ??= new NotificationSource("mcp", Guid.NewGuid().ToString("D"));

        _statuses[id] = new NotificationStatus(
            id,
            source,
            subject,
            kind,
            state,
            closeReason,
            answer is { } given
                ? new NotificationAnswer(id, source, Guid.NewGuid(), Guid.NewGuid(), given.Choices, given.Text, given.At)
                : null);

        return id;
    }

    public Task<Result<Guid>> PublishAsync(NotificationDraft draft, CancellationToken cancellationToken = default)
    {
        if (PublishAnswer is { } error) return Task.FromResult(Result.Failure<Guid>(error));

        var id = Guid.NewGuid();
        Published.Add((id, draft));
        _statuses[id] = new NotificationStatus(id, draft.Source, draft.Subject, draft.Kind, NotificationState.Open, null, null);
        return Task.FromResult(Result.Success(id));
    }

    public Task<Result> CloseAsync(Guid notificationId, string? reason = null, CancellationToken cancellationToken = default)
    {
        if (!_statuses.TryGetValue(notificationId, out var status)) return Task.FromResult(Result.Failure(NotificationsErrors.NotFound(notificationId)));
        if (status.State != NotificationState.Open) return Task.FromResult(Result.Failure(NotificationsErrors.AlreadySettled(status.State)));

        Closed.Add((notificationId, reason));
        _statuses[notificationId] = status with { State = NotificationState.Closed, CloseReason = reason };
        return Task.FromResult(Result.Success());
    }

    public Task<Result<NotificationStatus>> GetStatusAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_statuses.TryGetValue(notificationId, out var status)
            ? Result.Success(status)
            : Result.Failure<NotificationStatus>(NotificationsErrors.NotFound(notificationId)));

    public Task<Result<int>> CloseForSubjectAsync(NotificationSubject subject, string reason, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<NotificationAnswer?>> GetAnswerAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
