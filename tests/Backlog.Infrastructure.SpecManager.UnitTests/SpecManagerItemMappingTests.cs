using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// Every row of the connector's mapping table, read off the recorded backlog in
/// <c>Fixtures/backlog.json</c> and its archive.
/// </summary>
public sealed class SpecManagerItemMappingTests
{
    /// <summary>The last sync before the one under test, after #5 finished and
    /// before #6 did.</summary>
    private static readonly DateTimeOffset Since = new(2026, 10, 3, 0, 0, 0, TimeSpan.FromHours(2));

    private static async Task<SourceItem> ItemAsync(int nummer)
    {
        using var scenario = new ConnectorScenario();
        var items = await scenario.FetchAsync(Since);
        return items.Single(item => item.ExternalId == FixtureId(nummer));
    }

    [Fact]
    public async Task The_items_id_is_its_external_id_and_its_title_and_description_carry_over()
    {
        var item = await ItemAsync(1);

        Assert.Equal("a0000000-0000-0000-0000-000000000001", item.ExternalId);
        Assert.Equal("Open item in the first status", item.Title);
        Assert.Equal("The **first** status, assigned to me.", item.Body);
    }

    [Fact]
    public async Task An_item_without_a_description_has_an_empty_body()
    {
        Assert.Equal(string.Empty, (await ItemAsync(2)).Body);
    }

    [Fact]
    public async Task The_display_key_is_the_items_number_and_never_its_priority()
    {
        var item = await ItemAsync(1);

        // #1 ranks 7th on the board; the rank moves with every drag, the number never.
        Assert.Equal("#1", item.DisplayKey);
        Assert.DoesNotContain("7", item.DisplayKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_display_key_carries_a_priority()
    {
        using var scenario = new ConnectorScenario();
        var items = await scenario.FetchAsync(Since);

        // Every fixture item ranks differently from its number except where they
        // happen to agree, so the key must be the number for all of them.
        Assert.All(items, item => Assert.Matches(@"^#\d+( · [A-Z]+-\d+)?$", item.DisplayKey));
        Assert.Equal(
            ["#1", "#2", "#3 · SM-42", "#4", "#6", "#7", "#8", "#9", "#10", "#11", "#12"],
            items.Select(item => item.DisplayKey));
    }

    [Fact]
    public async Task A_Jira_linked_item_shows_its_Jira_key_after_its_number()
    {
        Assert.Equal("#3 · SM-42", (await ItemAsync(3)).DisplayKey);
    }

    [Fact]
    public async Task An_item_unlinked_from_Jira_shows_its_number_alone()
    {
        Assert.Equal("#4", (await ItemAsync(4)).DisplayKey);
    }

    [Fact]
    public async Task The_url_opens_the_item_on_its_products_backlog()
    {
        Assert.Equal(
            "https://spec.test/producten/backlog-demo/backlog?item=a0000000-0000-0000-0000-000000000003",
            (await ItemAsync(3)).Url);
    }

    [Fact]
    public async Task An_item_in_the_first_status_is_open()
    {
        var item = await ItemAsync(1);

        Assert.Equal(NormalisedSourceState.Open, item.State);
        Assert.Equal("Te doen", item.SourceStateName);
    }

    [Fact]
    public async Task An_item_in_a_status_before_the_first_work_status_is_open()
    {
        var item = await ItemAsync(2);

        Assert.Equal(NormalisedSourceState.Open, item.State);
        Assert.Equal("Verfijnd", item.SourceStateName);
    }

    [Fact]
    public async Task An_item_in_the_development_status_is_active()
    {
        var item = await ItemAsync(3);

        Assert.Equal(NormalisedSourceState.Active, item.State);
        Assert.Equal("In ontwikkeling", item.SourceStateName);
    }

    [Fact]
    public async Task An_item_in_a_status_after_the_first_work_status_is_active()
    {
        var item = await ItemAsync(4);

        Assert.Equal(NormalisedSourceState.Active, item.State);
        Assert.Equal("Review", item.SourceStateName);
    }

    [Fact]
    public async Task An_item_in_an_end_status_is_done_and_says_when_it_finished()
    {
        var item = await ItemAsync(6);

        Assert.Equal(NormalisedSourceState.Done, item.State);
        Assert.Equal("Klaar", item.SourceStateName);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 15, 0, 0, TimeSpan.Zero), item.CompletedAt);
    }

    [Fact]
    public async Task An_archived_item_is_dropped()
    {
        var item = await ItemAsync(12);

        Assert.Equal(NormalisedSourceState.Dropped, item.State);
        Assert.Equal("Dropped idea", item.Title);
    }

    [Fact]
    public async Task The_assignee_is_the_members_id_and_null_when_nobody_has_it()
    {
        Assert.Equal("30000000-0000-0000-0000-000000000001", (await ItemAsync(1)).Assignee);
        Assert.Null((await ItemAsync(2)).Assignee);
    }

    [Fact]
    public async Task The_update_stamp_is_the_items_last_change()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(2)), (await ItemAsync(1)).UpdatedAt);
    }

    [Fact]
    public async Task The_labels_are_the_label_names_then_the_issue_type_then_the_sprint()
    {
        Assert.Equal(["Frontend", "+release-2", "Story", "Sprint 12"], (await ItemAsync(1)).Labels);
    }

    [Fact]
    public async Task An_item_outside_a_sprint_carries_only_its_issue_type()
    {
        Assert.Equal(["Bug"], (await ItemAsync(2)).Labels);
    }

    [Fact]
    public async Task Effort_and_due_date_come_from_the_estimate_and_the_deadline()
    {
        var item = await ItemAsync(1);

        Assert.Equal(3, item.Effort);
        Assert.Equal(new DateOnly(2026, 11, 1), item.DueOn);
        Assert.Null(item.CompletedAt);
    }

    [Fact]
    public async Task An_unestimated_item_has_no_effort_and_no_due_date()
    {
        var item = await ItemAsync(2);

        Assert.Null(item.Effort);
        Assert.Null(item.DueOn);
    }

    [Fact]
    public async Task What_an_item_waits_on_is_named_by_the_external_ids_of_those_items()
    {
        Assert.Equal([FixtureId(1), FixtureId(6), FixtureId(2)], (await ItemAsync(9)).WaitsOn);
        Assert.Empty((await ItemAsync(1)).WaitsOn!);
    }

    [Fact]
    public void An_item_waited_on_that_this_fetch_did_not_read_is_still_named()
    {
        var item = Map(Bare() with { WachtOpIds = ["read-nowhere"] });

        Assert.Equal(["read-nowhere"], item.WaitsOn);
        Assert.False(item.IsBlocked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_finished_or_archived_item_is_never_blocked(bool archived)
    {
        var item = Map(Bare() with
        {
            IsGearchiveerd = archived,
            StatusId = archived ? "a" : "end",
            Belemmering = new BelemmeringDto("Still impeded", null, null),
            Geblokkeerd = true,
        });

        Assert.Equal(archived ? NormalisedSourceState.Dropped : NormalisedSourceState.Done, item.State);
        Assert.False(item.IsBlocked);
        Assert.Null(item.BlockedReason);
    }

    [Fact]
    public async Task An_impediment_blocks_the_item_with_its_reason()
    {
        var item = await ItemAsync(7);

        Assert.True(item.IsBlocked);
        Assert.Equal("Waiting for the API key", item.BlockedReason);
    }

    [Fact]
    public async Task An_impediment_without_a_reason_reads_impeded()
    {
        var item = await ItemAsync(8);

        Assert.True(item.IsBlocked);
        Assert.Equal("Impeded", item.BlockedReason);
    }

    [Fact]
    public async Task Waiting_on_an_open_item_blocks_and_names_only_the_open_ones()
    {
        var item = await ItemAsync(9);

        // #6 is done; #1 and #2 are not.
        Assert.True(item.IsBlocked);
        Assert.Equal("Waits on #1, #2", item.BlockedReason);
    }

    [Fact]
    public async Task Waiting_only_on_a_done_item_does_not_block()
    {
        var item = await ItemAsync(10);

        Assert.False(item.IsBlocked);
        Assert.Null(item.BlockedReason);
    }

    [Fact]
    public async Task The_servers_own_blocked_flag_wins_over_the_derived_one()
    {
        // #11 waits on the open #1, but the server says it is not blocked.
        var item = await ItemAsync(11);

        Assert.False(item.IsBlocked);
        Assert.Null(item.BlockedReason);
    }

    [Fact]
    public void The_servers_blocked_flag_answers_the_waiting_and_never_hides_an_impediment()
    {
        // spec-manager's geblokkeerd says whether a waited-on item is open (BACK-52);
        // an impediment is on the item itself and is no part of it.
        var impeded = Bare() with
        {
            Geblokkeerd = false,
            Belemmering = new BelemmeringDto("Server down", null, null),
        };

        var item = Map(impeded);

        Assert.True(item.IsBlocked);
        Assert.Equal("Server down", item.BlockedReason);
    }

    [Fact]
    public void A_server_that_says_blocked_about_an_item_this_fetch_did_not_read_names_it_from_its_own_list()
    {
        var waiting = Bare() with
        {
            WachtOpIds = ["archived-elsewhere"],
            WachtOp = [new AfgewachtItemDto("archived-elsewhere", 40, "Elsewhere", Afgerond: false)],
            Geblokkeerd = true,
        };

        var item = Map(waiting);

        Assert.True(item.IsBlocked);
        Assert.Equal("Waits on #40", item.BlockedReason);
        Assert.Equal(["archived-elsewhere"], item.WaitsOn);
    }

    [Fact]
    public async Task An_item_with_nothing_in_its_way_is_not_blocked()
    {
        Assert.False((await ItemAsync(1)).IsBlocked);
    }

    [Fact]
    public async Task The_references_are_the_items_own()
    {
        Assert.Equal(["arc42/05-building-block-view.md#tasks", "domain/tasks/domain.md"], (await ItemAsync(1)).References);
        Assert.Empty((await ItemAsync(2)).References!);
    }

    [Fact]
    public void With_no_status_flagged_as_work_the_first_is_open_and_the_rest_active()
    {
        var catalog = new ProductCatalog(
            [Status("b", 2), Status("a", 1), Status("c", 3), Status("d", 4, end: true)],
            [],
            DateTimeOffset.UnixEpoch);

        Assert.Equal(NormalisedSourceState.Open, catalog.StateOf(catalog.Statuses["a"]));
        Assert.Equal(NormalisedSourceState.Active, catalog.StateOf(catalog.Statuses["b"]));
        Assert.Equal(NormalisedSourceState.Active, catalog.StateOf(catalog.Statuses["c"]));
        Assert.Equal(NormalisedSourceState.Done, catalog.StateOf(catalog.Statuses["d"]));
    }

    [Fact]
    public void An_agent_status_counts_as_work_begun_like_development_and_review()
    {
        var catalog = new ProductCatalog(
            [Status("a", 1), Status("b", 2), Status("ai", 3, ai: true), Status("c", 4)],
            [],
            DateTimeOffset.UnixEpoch);

        Assert.Equal(NormalisedSourceState.Open, catalog.StateOf(catalog.Statuses["b"]));
        Assert.Equal(NormalisedSourceState.Active, catalog.StateOf(catalog.Statuses["ai"]));
        Assert.Equal(NormalisedSourceState.Active, catalog.StateOf(catalog.Statuses["c"]));
    }

    [Fact]
    public void A_status_the_catalog_does_not_have_reads_as_open()
    {
        var catalog = new ProductCatalog([Status("a", 1)], [], DateTimeOffset.UnixEpoch);

        Assert.Equal(NormalisedSourceState.Open, catalog.StateOf(null));
    }

    private static BacklogitemDto Bare() => new(
        "x", "Bare", null, "a", Prioriteit: 1, Nummer: 99, Verwijzingen: null, LabelIds: null, ToegewezenAanId: null,
        Jira: null, IsGearchiveerd: false, Inspanning: null, WachtOpIds: null, WachtOp: null, Geblokkeerd: null,
        Belemmering: null, Issuetype: null, Deadline: null, AfgerondOp: null, BijgewerktOp: DateTimeOffset.UnixEpoch,
        SprintId: null, Sprintnaam: null, ExterneSleutel: null);

    private static SourceItem Map(BacklogitemDto item) => SpecManagerItemMapper.Map(
        item,
        new ProductCatalog([Status("a", 1), Status("end", 2, end: true)], [], DateTimeOffset.UnixEpoch),
        new Dictionary<string, BacklogitemDto> { [item.Id] = item },
        "https://spec.test",
        "p");

    /// <summary>The fixture item numbered <paramref name="nummer"/>'s id.</summary>
    internal static string FixtureId(int nummer) =>
        "a0000000-0000-0000-0000-" + nummer.ToString("D12", System.Globalization.CultureInfo.InvariantCulture);

    private static BacklogstatusDto Status(string id, int order, bool end = false, bool ai = false) =>
        new(id, id.ToUpperInvariant(), order, IsEindstatus: end, IsAistatus: ai, IsReviewstatus: false, IsOntwikkelstatus: false);
}
