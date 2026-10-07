using System.Net;

using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// What a fetch asks spec-manager for, and what it keeps of the answer: the open
/// items every time, the finished and archived ones only once they are news, and
/// the status and label names from a cache that knows when it is stale.
/// </summary>
public sealed class SpecManagerFetchTests
{
    private const string BacklogPath = $"/api/producten/{StubSpecManager.Product}/backlog";
    private const string StatusesPath = $"/api/producten/{StubSpecManager.Product}/backlogstatussen";
    private const string MembersPath = $"/api/producten/{StubSpecManager.Product}/backlogleden";

    private static readonly DateTimeOffset Since = new(2026, 10, 3, 0, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public async Task A_first_sync_reads_the_unarchived_backlog_and_keeps_only_what_is_not_finished()
    {
        using var scenario = new ConnectorScenario();

        var items = await scenario.FetchAsync(since: null);

        Assert.Equal([1, 2, 3, 4, 7, 8, 9, 10, 11], items.Select(Number));
        Assert.DoesNotContain(items, item => item.State is NormalisedSourceState.Done or NormalisedSourceState.Dropped);

        var request = Assert.Single(scenario.Server.To(BacklogPath));
        Assert.Equal("?gearchiveerd=false", request.Uri.Query);
    }

    [Fact]
    public async Task A_later_sync_also_asks_the_archive_for_what_changed_since_with_an_explicit_offset()
    {
        using var scenario = new ConnectorScenario();

        await scenario.FetchAsync(Since);

        var requests = scenario.Server.To(BacklogPath);
        Assert.Equal(2, requests.Count);
        Assert.Equal("?gearchiveerd=false", requests[0].Uri.Query);

        var archive = requests[1].Uri;
        Assert.StartsWith("?gearchiveerd=true&gewijzigdSinds=", archive.Query, StringComparison.Ordinal);
        // Round-trip, with the offset the moment was taken in, and the plus escaped
        // so the server does not read it as a space.
        Assert.Contains("%2B02", archive.Query, StringComparison.Ordinal);
        // The skew allowance taken off the last sync: 00:00 becomes 23:45 the day before.
        Assert.EndsWith("gewijzigdSinds=2026-10-02T23:45:00.0000000+02:00", Uri.UnescapeDataString(archive.Query), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_local_clock_ahead_of_the_server_still_sees_what_finished_just_after_the_last_sync()
    {
        using var scenario = new ConnectorScenario();

        // #6 finished at 15:00 by the server's clock. This machine's clock ran ten
        // minutes fast, so the sync that started just before that recorded 15:10.
        // Without the allowance #6 would be missing, and its task archived as vanished.
        var items = await scenario.FetchAsync(new DateTimeOffset(2026, 10, 4, 15, 10, 0, TimeSpan.Zero));

        Assert.Contains(items, item => Number(item) == 6 && item.State == NormalisedSourceState.Done);
        Assert.Equal(SpecManagerConnector.ClockSkewAllowance, TimeSpan.FromMinutes(15));
        var archive = scenario.Server.To(BacklogPath)[1].Uri;
        Assert.EndsWith("gewijzigdSinds=2026-10-04T14:55:00.0000000+00:00", Uri.UnescapeDataString(archive.Query), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_finished_item_older_than_the_allowance_is_still_left_out()
    {
        using var scenario = new ConnectorScenario();

        var items = await scenario.FetchAsync(new DateTimeOffset(2026, 10, 4, 15, 16, 0, TimeSpan.Zero));

        Assert.DoesNotContain(items, item => Number(item) == 6);
    }

    [Fact]
    public async Task The_account_is_named_after_the_first_fetch_while_it_still_has_the_default_name()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn(accountName: null);
        scenario.Backlog();
        scenario.Server.Json(HttpMethod.Get, MembersPath, "leden.json");

        await scenario.Connector.FetchAsync(StubSpecManager.Product, null, TestContext.Current.CancellationToken);
        await scenario.Connector.FetchAsync(StubSpecManager.Product, null, TestContext.Current.CancellationToken);

        Assert.Equal("Jip Jansen", scenario.Connector.Account?.DisplayName);
        Assert.Single(scenario.Server.To(MembersPath));
    }

    [Fact]
    public async Task Naming_the_account_after_a_fetch_is_tried_once_and_its_failure_ignored()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn(accountName: null);
        scenario.Backlog();
        scenario.Server.Route(HttpMethod.Get, MembersPath, (_, _) => throw new HttpRequestException("down"));

        var first = await scenario.Connector.FetchAsync(StubSpecManager.Product, null, TestContext.Current.CancellationToken);
        await scenario.Connector.FetchAsync(StubSpecManager.Product, null, TestContext.Current.CancellationToken);

        Assert.NotEmpty(first);
        Assert.Equal("spec-manager", scenario.Connector.Account?.DisplayName);
        Assert.Single(scenario.Server.To(MembersPath));
    }

    [Fact]
    public async Task An_account_with_a_name_is_not_named_again_after_a_fetch()
    {
        using var scenario = new ConnectorScenario();

        await scenario.FetchAsync();

        Assert.Empty(scenario.Server.To(MembersPath));
    }

    [Fact]
    public async Task A_later_sync_returns_every_open_item_again()
    {
        using var scenario = new ConnectorScenario();

        var items = await scenario.FetchAsync(Since);

        // Open items changed long before the last sync are still in the answer: an
        // item missing from it is one that vanished.
        Assert.Contains(items, item => Number(item) == 1);
        Assert.Contains(items, item => Number(item) == 11);
    }

    [Fact]
    public async Task A_later_sync_keeps_a_finished_item_only_when_it_changed_since()
    {
        using var scenario = new ConnectorScenario();

        var items = await scenario.FetchAsync(Since);

        Assert.Contains(items, item => Number(item) == 6 && item.State == NormalisedSourceState.Done);
        Assert.DoesNotContain(items, item => Number(item) == 5);
    }

    [Fact]
    public async Task A_later_sync_reads_the_archives_changes_as_dropped()
    {
        using var scenario = new ConnectorScenario();

        var items = await scenario.FetchAsync(Since);

        var dropped = Assert.Single(items, item => item.State == NormalisedSourceState.Dropped);
        Assert.Equal(12, Number(dropped));
    }

    [Fact]
    public async Task The_statuses_and_labels_are_read_once_within_the_hour()
    {
        using var scenario = new ConnectorScenario();

        await scenario.FetchAsync();
        scenario.Time.Advance(TimeSpan.FromMinutes(59));
        await scenario.FetchAsync();

        Assert.Single(scenario.Server.To(StatusesPath));
        Assert.Single(scenario.Server.To($"/api/producten/{StubSpecManager.Product}/backloglabels"));
    }

    [Fact]
    public async Task The_statuses_are_read_again_after_an_hour()
    {
        using var scenario = new ConnectorScenario();

        await scenario.FetchAsync();
        scenario.Time.Advance(TimeSpan.FromMinutes(61));
        await scenario.FetchAsync();

        Assert.Equal(2, scenario.Server.To(StatusesPath).Count);
    }

    [Fact]
    public async Task An_item_in_a_status_the_cache_does_not_know_reads_the_statuses_again()
    {
        using var scenario = new ConnectorScenario();
        await scenario.FetchAsync();

        // A status was added at the installation, and an item moved into it.
        scenario.Server.Json(HttpMethod.Get, StatusesPath, "statuses-extended.json");
        scenario.Backlog(current: "backlog-new-status.json");
        var items = await scenario.Connector.FetchAsync(StubSpecManager.Product, null, TestContext.Current.CancellationToken);

        Assert.Equal(2, scenario.Server.To(StatusesPath).Count);
        var item = Assert.Single(items);
        Assert.Equal("AI bezig", item.SourceStateName);
        Assert.Equal(NormalisedSourceState.Active, item.State);
    }

    [Fact]
    public async Task A_fetch_with_nobody_signed_in_fails_as_sign_in_required_and_asks_nothing()
    {
        using var scenario = new ConnectorScenario();
        scenario.Backlog();

        var failure = await Assert.ThrowsAsync<TaskConnectorFetchException>(() =>
            scenario.Connector.FetchAsync(StubSpecManager.Product, null, TestContext.Current.CancellationToken));

        Assert.Equal(TaskConnectorFetchFailure.SignInRequired, failure.Kind);
        Assert.Contains("Not signed in to spec-manager", failure.Message, StringComparison.Ordinal);
        Assert.Empty(scenario.Server.To(BacklogPath));
    }

    /// <summary>A target is the product's slug. The defect this answers: an
    /// <c>owner/repository</c> typed under spec-manager came back as a 404 and read
    /// as spec-manager being down. It says there is no such product, and where the
    /// slug is found.</summary>
    [Fact]
    public async Task A_product_spec_manager_does_not_have_says_to_use_the_slug_from_its_url()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Route(HttpMethod.Get, BacklogPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.NotFound, "{}"));

        var failure = await Assert.ThrowsAsync<TaskConnectorFetchException>(() =>
            scenario.Connector.FetchAsync(StubSpecManager.Product, null, TestContext.Current.CancellationToken));

        Assert.Equal(TaskConnectorFetchFailure.NotFound, failure.Kind);
        Assert.Contains($"no product named {StubSpecManager.Product}", failure.Message, StringComparison.Ordinal);
        Assert.Contains("slug", failure.Message, StringComparison.Ordinal);
        Assert.Contains("spec-manager URL", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_backlog_the_server_refuses_fails_the_fetch_as_no_access()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Route(HttpMethod.Get, BacklogPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.Forbidden, "{}"));

        var failure = await Assert.ThrowsAsync<TaskConnectorFetchException>(() =>
            scenario.Connector.FetchAsync(StubSpecManager.Product, null, TestContext.Current.CancellationToken));

        Assert.Equal(TaskConnectorFetchFailure.NoAccess, failure.Kind);
        Assert.Contains(StubSpecManager.Product, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A server error is spec-manager not answering properly, which the sync
    /// reports as not reached; it is left as it was thrown.</summary>
    [Fact]
    public async Task A_server_error_is_left_unclassified()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Route(HttpMethod.Get, BacklogPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.InternalServerError, "{}"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            scenario.Connector.FetchAsync(StubSpecManager.Product, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Its_targets_are_products_typed_as_their_slug()
    {
        using var scenario = new ConnectorScenario();

        Assert.Equal("Product", scenario.Connector.Descriptor.TargetLabel);
        Assert.Equal("product-slug", scenario.Connector.Descriptor.TargetPlaceholder);
        Assert.Contains("spec-manager URL", scenario.Connector.Descriptor.TargetHelp, StringComparison.Ordinal);
        Assert.False(scenario.Connector.Capabilities.TargetIsRepository);
    }

    [Fact]
    public async Task The_product_slug_is_escaped_in_the_path()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();

        await Assert.ThrowsAsync<TaskConnectorFetchException>(() =>
            scenario.Connector.FetchAsync("a product/../x", null, TestContext.Current.CancellationToken));

        var request = Assert.Single(scenario.Server.Requests);
        Assert.Equal("/api/producten/a%20product%2F..%2Fx/backlog", request.Uri.AbsolutePath);
    }

    // --- The products to pick from ------------------------------------------------

    private const string ProductsPath = "/api/producten";

    /// <summary>The settings page picks a product rather than having its slug typed:
    /// each one the account can see, stored as its slug and shown by its name. The
    /// defect this answers: a product's name typed as its target 404s, because the
    /// REST interface takes only the slug.</summary>
    [Fact]
    public async Task The_choices_are_the_products_the_account_can_see_stored_by_slug_and_named_by_name()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Route(HttpMethod.Get, ProductsPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.OK, """
            [
                { "id": "6f1c0e8e-0000-0000-0000-000000000001", "slug": "fincent", "naam": "Fincent", "repository": "x", "doelbranch": "main" },
                { "id": "6f1c0e8e-0000-0000-0000-000000000002", "slug": "backlog-demo", "naam": "", "repository": "y", "doelbranch": "main" }
            ]
            """));

        var choices = await scenario.Connector.ListTargetChoicesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [new ConnectorTargetChoice("fincent", "Fincent"), new ConnectorTargetChoice("backlog-demo", "backlog-demo")],
            choices.Choices);
        Assert.Null(choices.CannotList);
        Assert.Equal("sma_current", Assert.Single(scenario.Server.To(ProductsPath)).Bearer);
    }

    /// <summary>The app signs in with an agent token, which spec-manager does not
    /// yet accept on the product list. A refusal is a list that could not be had,
    /// said in a sentence that points at typing the slug instead, never a throw
    /// into the page.</summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_product_list_spec_manager_refuses_is_reported_as_cannot_list(HttpStatusCode status)
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Route(HttpMethod.Get, ProductsPath, (_, _) => StubSpecManager.Respond(status, "{}"));
        scenario.Server.Json(HttpMethod.Post, "/oauth/token", "token.json", HttpStatusCode.BadRequest);

        var choices = await scenario.Connector.ListTargetChoicesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(choices.Choices);
        Assert.Contains("slug", choices.CannotList, StringComparison.Ordinal);
        Assert.Contains("spec-manager URL", choices.CannotList, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_nobody_signed_in_the_products_cannot_be_listed_and_nothing_is_asked()
    {
        using var scenario = new ConnectorScenario();

        var choices = await scenario.Connector.ListTargetChoicesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(choices.Choices);
        Assert.Contains("Sign in", choices.CannotList, StringComparison.Ordinal);
        Assert.Empty(scenario.Server.To(ProductsPath));
    }

    private static int Number(SourceItem item) =>
        int.Parse(item.DisplayKey[1..].Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
}
