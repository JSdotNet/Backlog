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
    public async Task A_fetch_with_nobody_signed_in_throws_and_asks_nothing()
    {
        using var scenario = new ConnectorScenario();
        scenario.Backlog();

        var failure = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            scenario.Connector.FetchAsync(StubSpecManager.Product, null, TestContext.Current.CancellationToken));

        Assert.Contains("Not signed in to spec-manager", failure.Message, StringComparison.Ordinal);
        Assert.Empty(scenario.Server.To(BacklogPath));
    }

    [Fact]
    public async Task A_backlog_the_server_refuses_fails_the_fetch()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();
        scenario.Server.Route(HttpMethod.Get, BacklogPath, (_, _) => StubSpecManager.Respond(HttpStatusCode.Forbidden, "{}"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            scenario.Connector.FetchAsync(StubSpecManager.Product, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_product_slug_is_escaped_in_the_path()
    {
        using var scenario = new ConnectorScenario();
        scenario.SignedIn();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            scenario.Connector.FetchAsync("a product/../x", null, TestContext.Current.CancellationToken));

        var request = Assert.Single(scenario.Server.Requests);
        Assert.Equal("/api/producten/a%20product%2F..%2Fx/backlog", request.Uri.AbsolutePath);
    }

    private static int Number(SourceItem item) =>
        int.Parse(item.DisplayKey[1..].Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
}
