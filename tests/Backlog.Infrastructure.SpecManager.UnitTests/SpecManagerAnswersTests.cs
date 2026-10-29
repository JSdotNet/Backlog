using System.Net;

using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Infrastructure.SpecManager.OAuth;
using Backlog.Infrastructure.SpecManager.Questions;
using Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// An answer given in Backlog sent back to spec-manager: what is put, that sending it again
/// is the same answer, and what the person is told when spec-manager will not take it.
/// </summary>
public sealed class SpecManagerAnswersTests
{
    private const string AnswerPath = $"{QuestionScenario.QuestionsPath}/{QuestionScenario.AssignedToMe}/answer";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void It_answers_for_the_spec_manager_source()
    {
        using var scenario = new QuestionScenario();

        Assert.Equal("spec-manager", Handler(scenario).Source);
    }

    [Fact]
    public async Task A_chosen_option_is_put_by_its_position_with_the_note()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Server.Route(HttpMethod.Put, AnswerPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.OK, "{}"));

        await Handler(scenario).HandleAnswerAsync(Answer(["2"], null, "Per line."), Token);

        Assert.Equal("""{"option":2,"other":null,"note":"Per line."}""", Assert.Single(scenario.Connector.Server.To(AnswerPath)).Body);
    }

    [Fact]
    public async Task An_open_answer_is_put_as_other()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Server.Route(HttpMethod.Put, AnswerPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.OK, "{}"));

        await Handler(scenario).HandleAnswerAsync(Answer([], "  Round per invoice  ", null), Token);

        Assert.Equal("""{"option":null,"other":"Round per invoice","note":null}""", Assert.Single(scenario.Connector.Server.To(AnswerPath)).Body);
    }

    /// <summary>Delivery is at least once: the same answer handed over twice is sent twice and
    /// taken twice as the same answer — spec-manager lets whoever answered give it again.</summary>
    [Fact]
    public async Task The_same_answer_handed_over_again_succeeds_again()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Server.Route(HttpMethod.Put, AnswerPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.OK, "{}"));
        var handler = Handler(scenario);
        var answer = Answer(["1"], null, null);

        await handler.HandleAnswerAsync(answer, Token);
        await handler.HandleAnswerAsync(answer, Token);

        Assert.Equal(2, scenario.Connector.Server.To(AnswerPath).Count);
        Assert.Empty(scenario.Ledger.Published);
    }

    [Fact]
    public async Task Signed_out_the_answer_is_kept_for_replay_and_the_sign_in_line_says_why()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Store.Remove(scenario.Root);
        var changed = 0;
        scenario.Connector.SignIn.AccountChanged += () => changed++;

        await Assert.ThrowsAsync<SpecManagerSignInRequiredException>(() => Handler(scenario).HandleAnswerAsync(Answer(["1"], null, null), Token));

        Assert.Equal("Sign in to spec-manager to send answers.", scenario.Connector.Connector.Notice);
        Assert.Equal(1, changed);
        Assert.Empty(scenario.Connector.Server.To(AnswerPath));
    }

    [Fact]
    public async Task An_answer_that_arrives_clears_the_sign_in_notice()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.SignIn.SetAnswersWaiting(true);
        scenario.Connector.Server.Route(HttpMethod.Put, AnswerPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.OK, "{}"));

        await Handler(scenario).HandleAnswerAsync(Answer(["1"], null, null), Token);

        Assert.Null(scenario.Connector.Connector.Notice);
    }

    [Fact]
    public async Task A_spec_manager_that_cannot_be_reached_keeps_the_answer_for_replay()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Server.Route(HttpMethod.Put, AnswerPath, (_, _) => throw new HttpRequestException("No route to host."));

        await Assert.ThrowsAsync<HttpRequestException>(() => Handler(scenario).HandleAnswerAsync(Answer(["1"], null, null), Token));

        Assert.Empty(scenario.Ledger.Published);
    }

    /// <summary>A server error is no answer about the question: the answer is kept, and no
    /// alert says it was not recorded.</summary>
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_server_error_keeps_the_answer_and_raises_no_alert(HttpStatusCode status)
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Server.Route(HttpMethod.Put, AnswerPath, (_, _) => StubSpecManager.Respond(status, "{}"));
        var handler = Handler(scenario);

        await Assert.ThrowsAsync<HttpRequestException>(() => handler.HandleAnswerAsync(Answer(["1"], null, null), Token));
        await handler.Alerts;

        Assert.Empty(scenario.Ledger.Published);
        Assert.Null(scenario.Connector.Connector.Notice);
    }

    [Fact]
    public async Task A_token_refused_again_after_the_refresh_keeps_the_answer_for_a_sign_in()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Server.Route(HttpMethod.Put, AnswerPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.Unauthorized, "{}"));

        await Assert.ThrowsAsync<SpecManagerSignInRequiredException>(() => Handler(scenario).HandleAnswerAsync(Answer(["1"], null, null), Token));

        Assert.Equal(SpecManagerSignIn.AnswersWaitingNotice, scenario.Connector.Connector.Notice);
    }

    [Fact]
    public async Task With_answering_from_a_connected_app_off_an_alert_says_to_turn_it_on_and_links_the_settings()
    {
        using var scenario = new QuestionScenario();
        await PublishQuestionAsync(scenario);
        scenario.Connector.Server.Route(HttpMethod.Put, AnswerPath, (_, _) => StubSpecManager.Respond(
            HttpStatusCode.Forbidden,
            """{"message":"Answering questions from a connected app is switched off for this product. Answer in spec-manager, or ask whoever holds product.features to switch it on in the product settings."}"""));
        var handler = Handler(scenario);

        await handler.HandleAnswerAsync(Answer(["1"], null, null), Token);
        await handler.Alerts;

        var alert = Alert(scenario);
        Assert.Equal(NotificationKind.Alert, alert.Kind);
        Assert.Equal(NotificationSeverity.Warning, alert.Severity);
        Assert.Equal($"Turn on 'Answer questions from a connected app' for {StubSpecManager.Product} in spec-manager", alert.Summary);
        Assert.Equal($"{scenario.Root}/producten/{StubSpecManager.Product}/instellingen/features", alert.DeepLink);
        Assert.Contains("Which rounding rule applies to invoice totals?", alert.BodyMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answered_by_someone_else_first_an_alert_says_who_and_what()
    {
        using var scenario = new QuestionScenario();
        await PublishQuestionAsync(scenario);
        scenario.Questions("[]", $$"""
            [ {
              "id": "{{QuestionScenario.AssignedToMe}}", "itemId": null, "itemNumber": null, "itemTitle": null,
              "text": "Which rounding rule applies to invoice totals?", "context": null,
              "options": [ { "position": 1, "label": "Round half even" }, { "position": 2, "label": "Round half up" } ],
              "assignee": null, "askedBy": { "id": "x", "name": "Kees de Vries" }, "askedAt": "2026-10-05T11:00:00+00:00",
              "source": "spec-manager", "openSpecChange": "c",
              "answer": { "option": 2, "optionLabel": "Round half up", "other": null, "note": null,
                          "answeredBy": { "id": "ada", "name": "Ada Lovelace" }, "answeredAt": "2026-10-05T11:10:00+00:00" }
            } ]
            """);
        scenario.Connector.Server.Route(HttpMethod.Put, AnswerPath, (_, _) => StubSpecManager.Respond(
            HttpStatusCode.BadRequest,
            """{"message":"This question was answered by someone else; only they can change the answer."}"""));
        var handler = Handler(scenario);

        await handler.HandleAnswerAsync(Answer(["1"], null, null), Token);
        await handler.Alerts;

        Assert.Equal(
            "Your answer to 'Which rounding rule applies to invoice totals?' was not recorded: Ada Lovelace answered 'Round half up' first",
            Alert(scenario).Summary);
    }

    [Fact]
    public async Task A_question_deleted_before_the_answer_arrived_is_said_so()
    {
        using var scenario = new QuestionScenario();
        await PublishQuestionAsync(scenario);
        scenario.Connector.Server.Route(HttpMethod.Put, AnswerPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.NotFound, """{"message":"Question not found."}"""));
        var handler = Handler(scenario);

        await handler.HandleAnswerAsync(Answer(["1"], null, null), Token);
        await handler.Alerts;

        Assert.Contains("the question is gone from spec-manager", Alert(scenario).Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Any_other_refusal_is_said_with_spec_managers_reason()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Server.Route(HttpMethod.Put, AnswerPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.Forbidden, """{"message":"MCP is switched off for this product."}"""));
        var handler = Handler(scenario);

        await handler.HandleAnswerAsync(Answer(["1"], null, null), Token);
        await handler.Alerts;

        var alert = Alert(scenario);
        Assert.Equal("Your answer to 'the question' was not recorded in spec-manager", alert.Summary);
        Assert.Contains("spec-manager refused it (403): MCP is switched off for this product.", alert.BodyMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_handed_over_again_raises_one_alert()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Server.Route(HttpMethod.Put, AnswerPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.NotFound, "{}"));
        var handler = Handler(scenario);

        await handler.HandleAnswerAsync(Answer(["1"], null, null), Token);
        await handler.HandleAnswerAsync(Answer(["1"], null, null), Token);
        await handler.Alerts;

        Assert.Single(scenario.Ledger.Published);
    }

    [Fact]
    public async Task An_answer_whose_source_key_names_no_question_sends_nothing()
    {
        using var scenario = new QuestionScenario();

        await Handler(scenario).HandleAnswerAsync(Answer(["1"], null, null) with { Source = new NotificationSource("spec-manager", "nothing") }, Token);

        Assert.DoesNotContain(scenario.Connector.Server.Requests, request => request.Method == HttpMethod.Put);
    }

    private static SpecManagerAnswers Handler(QuestionScenario scenario) => new(
        scenario.Connector.Client,
        scenario.Connector.SignIn,
        () => scenario.Ledger,
        () => scenario.Ledger,
        defaultInstallation: false);

    private static NotificationAnswer Answer(IReadOnlyList<string> choices, string? text, string? note) => new(
        SpecManagerQuestionIds.For("https://spec.test", QuestionScenario.AssignedToMe),
        new NotificationSource("spec-manager", $"{StubSpecManager.Product}/{QuestionScenario.AssignedToMe}"),
        Guid.NewGuid(),
        Guid.NewGuid(),
        choices,
        text,
        DateTimeOffset.UnixEpoch,
        note);

    /// <summary>The question as a pull publishes it, so an alert can quote its title.</summary>
    private static async Task PublishQuestionAsync(QuestionScenario scenario)
    {
        await scenario.Sync.PullAsync(whole: true, Token);
        scenario.Ledger.Answer(scenario.IdOf(QuestionScenario.AssignedToMe));
    }

    private static NotificationDraft Alert(QuestionScenario scenario) =>
        scenario.Ledger.Draft(SpecManagerQuestionIds.RefusalFor(scenario.Root, QuestionScenario.AssignedToMe));
}
