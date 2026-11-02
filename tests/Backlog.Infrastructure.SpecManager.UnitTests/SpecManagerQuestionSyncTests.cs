using Backlog.Infrastructure.SpecManager.Questions;
using Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// A pull: which questions are published, which close and why, and what is asked of
/// spec-manager each time.
/// </summary>
public sealed class SpecManagerQuestionSyncTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_questions_assigned_to_the_person_and_the_unassigned_ones_they_can_answer_are_published()
    {
        using var scenario = new QuestionScenario();

        Assert.True(await scenario.Sync.PullAsync(whole: false, Token));

        Assert.Equal(
            new[] { scenario.IdOf(QuestionScenario.AssignedToMe), scenario.IdOf(QuestionScenario.Unassigned) }.Order(),
            scenario.Ledger.Published.Select(entry => entry.Id).Order());
        Assert.Equal(NotificationSeverity.Warning, scenario.Ledger.Draft(scenario.IdOf(QuestionScenario.AssignedToMe)).Severity);
        Assert.Equal(NotificationSeverity.Info, scenario.Ledger.Draft(scenario.IdOf(QuestionScenario.Unassigned)).Severity);
    }

    [Fact]
    public async Task Only_questions_assigned_to_me_leaves_the_unassigned_ones_out()
    {
        using var scenario = new QuestionScenario();
        scenario.OnlyAssignedToMe(true);

        await scenario.Sync.PullAsync(whole: false, Token);

        Assert.Equal([scenario.IdOf(QuestionScenario.AssignedToMe)], scenario.Ledger.Published.Select(entry => entry.Id));
    }

    [Fact]
    public async Task The_first_pull_is_whole_and_the_next_asks_from_fifteen_minutes_before_it()
    {
        using var scenario = new QuestionScenario();

        await scenario.Sync.PullAsync(whole: false, Token);
        scenario.Connector.Time.Advance(TimeSpan.FromMinutes(5));
        await scenario.Sync.PullAsync(whole: false, Token);

        var queries = scenario.Connector.Server.To(QuestionScenario.QuestionsPath).Select(request => request.Uri.Query).ToList();
        Assert.Equal(
            [
                "?state=open",
                "?state=answered",
                "?state=open&changedSince=2026-10-05T11%3A45%3A00.0000000%2B00%3A00",
                "?state=answered&changedSince=2026-10-05T11%3A45%3A00.0000000%2B00%3A00",
            ],
            queries);
    }

    [Fact]
    public async Task A_whole_pull_asks_without_changed_since_again()
    {
        using var scenario = new QuestionScenario();

        await scenario.Sync.PullAsync(whole: false, Token);
        await scenario.Sync.PullAsync(whole: true, Token);

        Assert.All(scenario.Connector.Server.To(QuestionScenario.QuestionsPath), request => Assert.DoesNotContain("changedSince", request.Uri.Query, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_question_answered_in_spec_manager_closes_with_who_answered_and_what()
    {
        using var scenario = new QuestionScenario();
        scenario.Questions("questions-open.json", "[]");
        var held = scenario.IdOf(QuestionScenario.AnsweredByKees);
        await scenario.Ledger.PublishAsync(Question(scenario, QuestionScenario.AnsweredByKees), Token);

        scenario.Questions("[]", "questions-answered.json");
        await scenario.Sync.PullAsync(whole: false, Token);

        Assert.Equal(NotificationState.Closed, scenario.Ledger.StateOf(held));
        Assert.Equal("Answered in spec-manager by Kees de Vries: No", scenario.Ledger.ReasonOf(held));
    }

    [Fact]
    public async Task A_question_answered_here_is_left_as_answered_when_spec_manager_reports_it()
    {
        using var scenario = new QuestionScenario();
        var held = scenario.IdOf(QuestionScenario.AnsweredByKees);
        await scenario.Ledger.PublishAsync(Question(scenario, QuestionScenario.AnsweredByKees), Token);
        scenario.Ledger.Answer(held);

        scenario.Questions("[]", "questions-answered.json");
        await scenario.Sync.PullAsync(whole: false, Token);

        Assert.Equal(NotificationState.Answered, scenario.Ledger.StateOf(held));
    }

    [Fact]
    public async Task A_question_reassigned_away_is_withdrawn()
    {
        using var scenario = new QuestionScenario();
        await scenario.Sync.PullAsync(whole: false, Token);
        var mine = scenario.IdOf(QuestionScenario.AssignedToMe);

        var reassigned = Fixtures.Read("questions-open.json").Replace(
            "\"assignee\": { \"id\": \"30000000-0000-0000-0000-000000000001\", \"name\": \"Jip Jansen\" }",
            "\"assignee\": { \"id\": \"30000000-0000-0000-0000-000000000002\", \"name\": \"Kees de Vries\" }",
            StringComparison.Ordinal);
        scenario.Questions(reassigned, "[]");
        await scenario.Sync.PullAsync(whole: false, Token);

        Assert.Equal(NotificationState.Closed, scenario.Ledger.StateOf(mine));
        Assert.Equal(SpecManagerQuestionMapper.WithdrawnReason, scenario.Ledger.ReasonOf(mine));
    }

    [Fact]
    public async Task Turning_on_only_questions_assigned_to_me_withdraws_the_unassigned_ones()
    {
        using var scenario = new QuestionScenario();
        await scenario.Sync.PullAsync(whole: false, Token);

        scenario.OnlyAssignedToMe(true);
        await scenario.Sync.PullAsync(whole: true, Token);

        Assert.Equal(NotificationState.Closed, scenario.Ledger.StateOf(scenario.IdOf(QuestionScenario.Unassigned)));
        Assert.Equal(NotificationState.Open, scenario.Ledger.StateOf(scenario.IdOf(QuestionScenario.AssignedToMe)));
    }

    [Fact]
    public async Task A_deleted_question_is_withdrawn_by_the_next_whole_pull_and_not_before()
    {
        using var scenario = new QuestionScenario();
        await scenario.Sync.PullAsync(whole: false, Token);
        var mine = scenario.IdOf(QuestionScenario.AssignedToMe);

        scenario.Questions("[]", "[]");
        await scenario.Sync.PullAsync(whole: false, Token);
        Assert.Equal(NotificationState.Open, scenario.Ledger.StateOf(mine));

        await scenario.Sync.PullAsync(whole: true, Token);
        Assert.Equal(NotificationState.Closed, scenario.Ledger.StateOf(mine));
        Assert.Equal(SpecManagerQuestionMapper.WithdrawnReason, scenario.Ledger.ReasonOf(mine));
    }

    [Fact]
    public async Task Publishing_again_on_the_next_pull_keeps_one_notification_per_question()
    {
        using var scenario = new QuestionScenario();

        await scenario.Sync.PullAsync(whole: false, Token);
        await scenario.Sync.PullAsync(whole: true, Token);

        Assert.Equal(2, scenario.Ledger.Published.Count);
    }

    [Fact]
    public async Task A_question_on_a_linked_task_is_about_the_task()
    {
        using var scenario = new QuestionScenario();
        var task = Guid.NewGuid();
        scenario.Linked.Items["20000000-0000-0000-0000-000000000007"] = new SpecManagerLinkedWork(task, "Rounding fix");

        await scenario.Sync.PullAsync(whole: false, Token);

        var draft = scenario.Ledger.Draft(scenario.IdOf(QuestionScenario.AssignedToMe));
        Assert.Equal(new NotificationSubject("tasks", "task", task.ToString("D")), draft.Subject);
        Assert.Equal("Agent session · Rounding fix", draft.Origin!.Via);
    }

    [Fact]
    public async Task Nobody_signed_in_pulls_nothing()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Store.Remove(scenario.Root);

        Assert.False(await scenario.Sync.PullAsync(whole: true, Token));

        Assert.Empty(scenario.Connector.Server.To(QuestionScenario.QuestionsPath));
        Assert.Empty(scenario.Ledger.Published);
    }

    [Fact]
    public async Task A_disabled_product_is_not_pulled()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Targets.Update(SpecManagerConnector.ConnectorId, StubSpecManager.Product, target => target with { Enabled = false });

        await scenario.Sync.PullAsync(whole: true, Token);

        Assert.Empty(scenario.Connector.Server.To(QuestionScenario.QuestionsPath));
    }

    [Fact]
    public async Task A_product_that_refuses_is_reported_and_publishes_nothing()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Server.Route(HttpMethod.Get, QuestionScenario.QuestionsPath, (_, _) => StubSpecManager.Respond(System.Net.HttpStatusCode.Forbidden, "{}"));

        Assert.False(await scenario.Sync.PullAsync(whole: true, Token));
        Assert.Empty(scenario.Ledger.Published);
    }

    /// <summary>A product slugged <c>notification</c> keys its questions as the other
    /// notifications' alerts are keyed; a whole pull of it still closes none of them.</summary>
    [Fact]
    public async Task A_whole_pull_never_closes_the_alert_of_another_spec_manager_notification()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Targets.Save(new Backlog.Modules.Tasks.Abstractions.Connectors.ConnectedTarget(SpecManagerConnector.ConnectorId, "notification"));
        scenario.Connector.Server.Route(HttpMethod.Get, "/api/products/notification/backlog-questions", (_, _) => StubSpecManager.Respond(System.Net.HttpStatusCode.OK, "[]"));
        var alert = SpecManagerNotificationMapper.ToDraft(NotificationScenario.Fixture(NotificationScenario.Reply), scenario.Root, false, null);
        var id = (await scenario.Ledger.PublishAsync(alert, Token)).Value;

        Assert.True(await scenario.Sync.PullAsync(whole: true, Token));

        Assert.Equal(NotificationState.Open, scenario.Ledger.StateOf(id));
    }

    private static NotificationDraft Question(QuestionScenario scenario, string questionId) => new(
        new NotificationSource("spec-manager", $"{StubSpecManager.Product}/{questionId}"),
        new NotificationSubject("spec-manager", "question", questionId),
        NotificationKind.Question,
        NotificationSeverity.Info,
        "A question",
        string.Empty,
        string.Empty,
        new AnswerSpec([new AnswerChoice("1", "Yes"), new AnswerChoice("2", "No")]),
        Id: scenario.IdOf(questionId));
}
