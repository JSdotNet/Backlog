using System.Net;

using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// Completing an item at its source — what finishing a linked task becomes on
/// spec-manager: the item is moved to the product's first end status by its order,
/// with the person's own token. Everything spec-manager says no to comes back as a
/// sentence for the task to show, never as a failure.
/// </summary>
public sealed class SpecManagerCompleteTests
{
    private const string ItemId = "20000000-0000-0000-0000-000000000004";
    private const string StatusPath = $"/api/products/{StubSpecManager.Product}/backlog/{ItemId}/status";
    private const string StatusesPath = $"/api/products/{StubSpecManager.Product}/backlog-statuses";

    /// <summary>"Klaar", the one end status in the recorded statuses.</summary>
    private const string EndStatus = "10000000-0000-0000-0000-000000000005";

    private static readonly SourceRef Item = new(
        SpecManagerConnector.ConnectorId,
        StubSpecManager.Product,
        ItemId,
        $"https://spec.test/producten/{StubSpecManager.Product}/backlog?item={ItemId}",
        "#4",
        null,
        "In ontwikkeling",
        new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero),
        normalisedState: NormalisedSourceState.Active);

    [Fact]
    public void It_says_it_can_complete()
    {
        using var scenario = new ConnectorScenario();

        Assert.True(scenario.Connector.Capabilities.CanComplete);
    }

    [Fact]
    public async Task Completing_moves_the_item_to_the_first_end_status_with_the_persons_token()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Route(HttpMethod.Put, StatusPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.OK, "{}"));

        var refusal = await scenario.Connector.CompleteAsync(Item, TestContext.Current.CancellationToken);

        Assert.Null(refusal);
        var request = Assert.Single(scenario.Server.To(StatusPath));
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal($$"""{"statusId":"{{EndStatus}}"}""", request.Body);
        Assert.Equal("sma_current", request.Bearer);
    }

    /// <summary>The end status is the first by the product's order, not the first
    /// listed: a board may list its statuses in any order.</summary>
    [Fact]
    public async Task The_end_status_is_the_first_by_order_not_by_listing()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Route(HttpMethod.Get, StatusesPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.OK, """
            { "statuses": [
                { "id": "s-gearchiveerd", "name": "Vervallen", "order": 9, "isEndStatus": true },
                { "id": "s-todo", "name": "Te doen", "order": 1, "isEndStatus": false },
                { "id": "s-klaar", "name": "Klaar", "order": 5, "isEndStatus": true }
            ] }
            """));
        scenario.Server.Route(HttpMethod.Put, StatusPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.OK, "{}"));

        Assert.Null(await scenario.Connector.CompleteAsync(Item, TestContext.Current.CancellationToken));
        Assert.Equal("""{"statusId":"s-klaar"}""", Assert.Single(scenario.Server.To(StatusPath)).Body);
    }

    [Fact]
    public async Task With_nobody_signed_in_it_asks_for_a_sign_in_and_sends_nothing()
    {
        using var scenario = new ConnectorScenario();

        var refusal = await scenario.Connector.CompleteAsync(Item, TestContext.Current.CancellationToken);

        Assert.Equal("Sign in to spec-manager to complete items there.", refusal);
        Assert.Empty(scenario.Server.Requests);
    }

    [Fact]
    public async Task A_product_with_no_end_status_is_refused_without_a_write()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Route(HttpMethod.Get, StatusesPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.OK, """
            { "statuses": [ { "id": "s-todo", "name": "Te doen", "order": 1, "isEndStatus": false } ] }
            """));

        var refusal = await scenario.Connector.CompleteAsync(Item, TestContext.Current.CancellationToken);

        Assert.Contains("no end status", refusal);
        Assert.Empty(scenario.Server.To(StatusPath));
    }

    /// <summary>A product whose agent switch is off answers a 403 with spec-manager's
    /// own error body, a <c>message</c> beside an optional <c>kind</c>; the person reads the
    /// message.</summary>
    [Fact]
    public async Task A_refusal_says_the_status_and_spec_managers_message()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Route(HttpMethod.Put, StatusPath, (_, _) => StubSpecManager.Respond(
            HttpStatusCode.Forbidden,
            """{"message":"Statuswijzigingen via een agent staan uit voor dit product."}"""));

        var refusal = await scenario.Connector.CompleteAsync(Item, TestContext.Current.CancellationToken);

        Assert.Equal("spec-manager refused (403): Statuswijzigingen via een agent staan uit voor dit product.", refusal);
    }

    /// <summary>A refusal in problem details — what the framework answers before
    /// spec-manager's own handling is reached — says its <c>detail</c>.</summary>
    [Fact]
    public async Task A_refusal_in_problem_details_says_the_detail()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Route(HttpMethod.Put, StatusPath, (_, _) => StubSpecManager.Respond(
            HttpStatusCode.BadRequest,
            """{"type":"about:blank","title":"Bad Request","status":400,"detail":"The body is not a status change."}"""));

        Assert.Equal(
            "spec-manager refused (400): The body is not a status change.",
            await scenario.Connector.CompleteAsync(Item, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_problem_with_only_a_title_says_the_title()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Route(HttpMethod.Put, StatusPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.Conflict, """{"title":"Het item is gearchiveerd."}"""));

        Assert.Equal(
            "spec-manager refused (409): Het item is gearchiveerd.",
            await scenario.Connector.CompleteAsync(Item, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_refusal_with_no_reason_says_the_status_alone()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Route(HttpMethod.Put, StatusPath, (_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        Assert.Equal(
            "spec-manager refused (500).",
            await scenario.Connector.CompleteAsync(Item, TestContext.Current.CancellationToken));
    }

    /// <summary>The write goes through the same refresh as the reads: a token refused
    /// before its time is refreshed once and the write sent again, body and all.</summary>
    [Fact]
    public async Task A_refused_token_is_refreshed_and_the_write_sent_again()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Route(HttpMethod.Post, "/oauth/token", (_, _) => StubSpecManager.Respond(
            HttpStatusCode.OK,
            """{"access_token":"sma_second","refresh_token":"smr_second","token_type":"Bearer","expires_in":3600}"""));
        scenario.Server.Route(HttpMethod.Put, StatusPath, (request, _) =>
            request.Headers.Authorization?.Parameter == "sma_second"
                ? StubSpecManager.Respond(HttpStatusCode.OK, "{}")
                : StubSpecManager.Respond(HttpStatusCode.Unauthorized, "{}"));

        Assert.Null(await scenario.Connector.CompleteAsync(Item, TestContext.Current.CancellationToken));

        var writes = scenario.Server.To(StatusPath);
        Assert.Equal(["sma_current", "sma_second"], writes.Select(request => request.Bearer));
        Assert.All(writes, request => Assert.Equal($$"""{"statusId":"{{EndStatus}}"}""", request.Body));
    }
}
