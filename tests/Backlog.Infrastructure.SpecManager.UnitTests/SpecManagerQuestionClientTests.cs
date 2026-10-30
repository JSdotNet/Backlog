using System.Net;

using Backlog.Infrastructure.SpecManager.Api;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// The three calls the question source makes — the questions, an answer, the notification
/// stream — each carrying the person's token and answering a token refused before its time
/// the way every other path does: one refresh, one retry.
/// </summary>
public sealed class SpecManagerQuestionClientTests
{
    private const string AnswerPath = $"{QuestionScenario.QuestionsPath}/{QuestionScenario.AssignedToMe}/answer";

    private static HttpResponseMessage Refreshed() => StubSpecManager.Respond(
        HttpStatusCode.OK,
        """{"access_token":"sma_second","refresh_token":"smr_second","token_type":"Bearer","expires_in":3600}""");

    [Fact]
    public async Task The_questions_are_read_by_state_and_changed_since_with_the_persons_token()
    {
        using var scenario = new QuestionScenario();
        var since = new DateTimeOffset(2026, 10, 5, 11, 45, 0, TimeSpan.Zero);

        var open = await scenario.Connector.Client.GetQuestionsAsync(StubSpecManager.Product, "open", since, TestContext.Current.CancellationToken);

        Assert.Equal(3, open.Count);
        var first = open[0];
        Assert.Equal(QuestionScenario.AssignedToMe, first.Id);
        Assert.Equal(7, first.ItemNumber);
        Assert.Equal(QuestionScenario.Me, first.Assignee!.Id);
        Assert.True(first.FromAgentSession);
        Assert.True(first.CanAnswer);
        Assert.Equal(3, first.Options!.Count);

        var request = Assert.Single(scenario.Connector.Server.To(QuestionScenario.QuestionsPath));
        Assert.Equal("?state=open&changedSince=2026-10-05T11%3A45%3A00.0000000%2B00%3A00", request.Uri.Query);
        Assert.Equal("sma_current", request.Bearer);
    }

    [Fact]
    public async Task Without_changed_since_the_whole_state_is_asked_for()
    {
        using var scenario = new QuestionScenario();

        var answered = await scenario.Connector.Client.GetQuestionsAsync(StubSpecManager.Product, "answered", null, TestContext.Current.CancellationToken);

        var question = Assert.Single(answered);
        Assert.Equal("No", question.Answer!.Said);
        Assert.Equal("Kees de Vries", question.Answer.AnsweredBy!.Name);
        Assert.Equal("?state=answered", Assert.Single(scenario.Connector.Server.To(QuestionScenario.QuestionsPath)).Uri.Query);
    }

    [Fact]
    public async Task A_refused_token_on_the_questions_is_refreshed_and_asked_again()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Server.Route(HttpMethod.Post, "/oauth/token", (_, _) => Refreshed());
        scenario.Connector.Server.Route(HttpMethod.Get, QuestionScenario.QuestionsPath, (request, _) =>
            request.Headers.Authorization?.Parameter == "sma_second"
                ? StubSpecManager.Respond(HttpStatusCode.OK, "[]")
                : StubSpecManager.Respond(HttpStatusCode.Unauthorized, "{}"));

        var open = await scenario.Connector.Client.GetQuestionsAsync(StubSpecManager.Product, "open", null, TestContext.Current.CancellationToken);

        Assert.Empty(open);
        Assert.Equal(["sma_current", "sma_second"], scenario.Connector.Server.To(QuestionScenario.QuestionsPath).Select(request => request.Bearer));
    }

    [Fact]
    public async Task An_answer_is_put_as_the_option_the_other_text_and_the_note()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Server.Route(HttpMethod.Put, AnswerPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.OK, "{}"));

        await scenario.Connector.Client.AnswerQuestionAsync(
            StubSpecManager.Product,
            QuestionScenario.AssignedToMe,
            new BacklogAnswerRequest(2, null, "Per line, please."),
            TestContext.Current.CancellationToken);

        var request = Assert.Single(scenario.Connector.Server.To(AnswerPath));
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("""{"option":2,"other":null,"note":"Per line, please."}""", request.Body);
        Assert.Equal("sma_current", request.Bearer);
    }

    [Fact]
    public async Task A_refused_token_on_an_answer_is_refreshed_and_the_answer_sent_again()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Server.Route(HttpMethod.Post, "/oauth/token", (_, _) => Refreshed());
        scenario.Connector.Server.Route(HttpMethod.Put, AnswerPath, (request, _) =>
            request.Headers.Authorization?.Parameter == "sma_second"
                ? StubSpecManager.Respond(HttpStatusCode.OK, "{}")
                : StubSpecManager.Respond(HttpStatusCode.Unauthorized, "{}"));

        await scenario.Connector.Client.AnswerQuestionAsync(
            StubSpecManager.Product, QuestionScenario.AssignedToMe, new BacklogAnswerRequest(null, "Neither", null), TestContext.Current.CancellationToken);

        var sent = scenario.Connector.Server.To(AnswerPath);
        Assert.Equal(["sma_current", "sma_second"], sent.Select(request => request.Bearer));
        Assert.All(sent, request => Assert.Equal("""{"option":null,"other":"Neither","note":null}""", request.Body));
    }

    [Fact]
    public async Task A_refused_answer_carries_the_status_and_spec_managers_reason()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Server.Route(HttpMethod.Put, AnswerPath, (_, _) => StubSpecManager.Respond(
            HttpStatusCode.Forbidden,
            """{"message":"Answering questions from a connected app is switched off for this product."}"""));

        var refused = await Assert.ThrowsAsync<SpecManagerRefusedException>(() => scenario.Connector.Client.AnswerQuestionAsync(
            StubSpecManager.Product, QuestionScenario.AssignedToMe, new BacklogAnswerRequest(1, null, null), TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
        Assert.Equal("Answering questions from a connected app is switched off for this product.", refused.Reason);
    }

    [Fact]
    public async Task The_stream_is_asked_for_as_server_sent_events_and_a_refused_token_is_refreshed()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Server.Route(HttpMethod.Post, "/oauth/token", (_, _) => Refreshed());
        scenario.Connector.Server.Route(HttpMethod.Get, "/api/notifications/stream", (request, _) =>
            request.Headers.Authorization?.Parameter == "sma_second"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("event: changed\ndata: {}\n\n") }
                : StubSpecManager.Respond(HttpStatusCode.Unauthorized, "{}"));

        using var response = await scenario.Connector.Client.OpenNotificationStreamAsync(TestContext.Current.CancellationToken);

        Assert.Equal("event: changed\ndata: {}\n\n", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(["sma_current", "sma_second"], scenario.Connector.Server.To("/api/notifications/stream").Select(request => request.Bearer));
    }

    [Fact]
    public async Task A_stream_spec_manager_refuses_throws_with_its_status()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Server.Route(HttpMethod.Get, "/api/notifications/stream", (_, _) => StubSpecManager.Respond(HttpStatusCode.Forbidden, "{}"));

        var refused = await Assert.ThrowsAsync<HttpRequestException>(() => scenario.Connector.Client.OpenNotificationStreamAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task Nobody_signed_in_sends_no_question_call()
    {
        using var scenario = new ConnectorScenario();

        await Assert.ThrowsAsync<SpecManagerSignInRequiredException>(() =>
            scenario.Client.GetQuestionsAsync(StubSpecManager.Product, "open", null, TestContext.Current.CancellationToken));

        Assert.Empty(scenario.Server.To(QuestionScenario.QuestionsPath));
    }
}
