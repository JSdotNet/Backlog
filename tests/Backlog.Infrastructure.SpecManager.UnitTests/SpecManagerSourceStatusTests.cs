using System.Net;

using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// The statuses a product's items can be in, as the settings page offers them a
/// row of the target's status map: the product's own, in its order, each with the
/// state the fetch normalises it to. A list that cannot be had is no rows, never a
/// failure.
/// </summary>
public sealed class SpecManagerSourceStatusTests
{
    private const string StatusesPath = $"/api/products/{StubSpecManager.Product}/backlog-statuses";

    [Fact]
    public async Task The_product_s_statuses_are_listed_in_its_order_with_their_states()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();

        var statuses = await scenario.Connector.ListSourceStatusesAsync(StubSpecManager.Product, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new SourceStatus("Te doen", NormalisedSourceState.Open),
                new SourceStatus("Verfijnd", NormalisedSourceState.Open),
                new SourceStatus("In ontwikkeling", NormalisedSourceState.Active),
                new SourceStatus("Review", NormalisedSourceState.Active),
                new SourceStatus("Klaar", NormalisedSourceState.Done),
            ],
            statuses);
    }

    /// <summary>The case the map exists for: a product with no status flagged as
    /// work reads every status after the first as Active, and says so here, so the
    /// page's default for each row is the status the sync would give.</summary>
    [Fact]
    public async Task A_product_with_no_work_status_lists_every_status_after_the_first_as_active()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Route(HttpMethod.Get, StatusesPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.OK, """
            { "statuses": [
                { "id": "s-klaar", "name": "Klaar", "order": 3, "isEndStatus": true },
                { "id": "s-verfijnd", "name": "Verfijnd", "order": 2, "isEndStatus": false },
                { "id": "s-todo", "name": "Te doen", "order": 1, "isEndStatus": false }
            ] }
            """));

        var statuses = await scenario.Connector.ListSourceStatusesAsync(StubSpecManager.Product, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new SourceStatus("Te doen", NormalisedSourceState.Open),
                new SourceStatus("Verfijnd", NormalisedSourceState.Active),
                new SourceStatus("Klaar", NormalisedSourceState.Done),
            ],
            statuses);
    }

    /// <summary>A map key is read trimmed and without regard to case, so two
    /// statuses spelled alike are one row — the first by the product's order.</summary>
    [Fact]
    public async Task Statuses_spelled_alike_are_listed_once_first_by_order()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Route(HttpMethod.Get, StatusesPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.OK, """
            { "statuses": [
                { "id": "s-review-2", "name": "review ", "order": 3, "isEndStatus": false },
                { "id": "s-todo", "name": "Te doen", "order": 1, "isEndStatus": false },
                { "id": "s-review", "name": "Review", "order": 2, "isEndStatus": false }
            ] }
            """));

        var statuses = await scenario.Connector.ListSourceStatusesAsync(StubSpecManager.Product, TestContext.Current.CancellationToken);

        Assert.Equal(["Te doen", "Review"], statuses.Select(status => status.Name));
    }

    /// <summary>A spec-manager that does not answer in time, or whose circuit is
    /// open, is no rows, the way a refusal is.</summary>
    [Fact]
    public async Task A_spec_manager_the_resilience_pipeline_gave_up_on_has_none()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Route(HttpMethod.Get, StatusesPath, (_, _) => throw new Polly.Timeout.TimeoutRejectedException());

        var statuses = await scenario.Connector.ListSourceStatusesAsync(StubSpecManager.Product, TestContext.Current.CancellationToken);

        Assert.Empty(statuses);
    }

    [Fact]
    public async Task With_nobody_signed_in_there_are_none_and_nothing_is_asked()
    {
        using var scenario = new ConnectorScenario();

        var statuses = await scenario.Connector.ListSourceStatusesAsync(StubSpecManager.Product, TestContext.Current.CancellationToken);

        Assert.Empty(statuses);
        Assert.Empty(scenario.Server.Requests);
    }

    [Fact]
    public async Task A_product_spec_manager_will_not_list_has_none()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Route(HttpMethod.Get, StatusesPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.Forbidden, "{}"));

        var statuses = await scenario.Connector.ListSourceStatusesAsync(StubSpecManager.Product, TestContext.Current.CancellationToken);

        Assert.Empty(statuses);
    }
}
