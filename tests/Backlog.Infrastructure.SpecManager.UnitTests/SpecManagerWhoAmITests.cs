using System.Net;

using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// "Assigned to me" needs to know who "me" is, in the spelling an item's assignee
/// uses: the member id of whoever the token belongs to.
/// </summary>
public sealed class SpecManagerWhoAmITests
{
    private const string MembersPath = $"/api/producten/{StubSpecManager.Product}/backlogleden";

    [Fact]
    public async Task Who_am_I_is_the_member_behind_the_token()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Json(HttpMethod.Get, MembersPath, "leden.json");

        var me = await scenario.Connector.WhoAmIAsync(TestContext.Current.CancellationToken);

        Assert.Equal("30000000-0000-0000-0000-000000000001", me);
        Assert.Equal("sma_current", Assert.Single(scenario.Server.To(MembersPath)).Bearer);
    }

    [Fact]
    public async Task Who_am_I_is_spelled_the_way_an_items_assignee_is()
    {
        using var scenario = new ConnectorScenario();
        scenario.Server.Json(HttpMethod.Get, MembersPath, "leden.json");
        var items = await scenario.FetchAsync();

        var me = await scenario.Connector.WhoAmIAsync(TestContext.Current.CancellationToken);

        Assert.Contains(items, item => item.Assignee == me);
    }

    [Fact]
    public async Task Who_am_I_is_null_while_the_installation_has_no_members_path()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();

        Assert.Null(await scenario.Connector.WhoAmIAsync(TestContext.Current.CancellationToken));
        Assert.Single(scenario.Server.To(MembersPath));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Who_am_I_is_null_when_the_members_are_refused(HttpStatusCode status)
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Route(HttpMethod.Get, MembersPath, (_, _) => StubSpecManager.Respond(status, "{}"));

        Assert.Null(await scenario.Connector.WhoAmIAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Who_am_I_is_null_with_nobody_signed_in()
    {
        using var scenario = new ConnectorScenario();
        scenario.Server.Json(HttpMethod.Get, MembersPath, "leden.json");

        Assert.Null(await scenario.Connector.WhoAmIAsync(TestContext.Current.CancellationToken));
        Assert.Empty(scenario.Server.Requests);
    }

    [Fact]
    public async Task Who_am_I_asks_the_first_enabled_spec_manager_target()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Targets.Clear();
        scenario.Targets.Save(new ConnectedTarget("github", "JSdotNet/Backlog"));
        scenario.Targets.Save(new ConnectedTarget(SpecManagerConnector.ConnectorId, "paused", Enabled: false));
        scenario.Targets.Save(new ConnectedTarget(SpecManagerConnector.ConnectorId, StubSpecManager.Product));
        scenario.Server.Json(HttpMethod.Get, MembersPath, "leden.json");

        Assert.Equal("30000000-0000-0000-0000-000000000001", await scenario.Connector.WhoAmIAsync(TestContext.Current.CancellationToken));
        Assert.Single(scenario.Server.Requests);
    }

    [Fact]
    public async Task Who_am_I_is_null_with_no_product_connected()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Targets.Clear();

        Assert.Null(await scenario.Connector.WhoAmIAsync(TestContext.Current.CancellationToken));
        Assert.Empty(scenario.Server.Requests);
    }
}
