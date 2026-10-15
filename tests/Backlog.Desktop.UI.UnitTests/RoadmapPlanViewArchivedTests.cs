using Backlog.UI.Components.Roadmap;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The band of an archived repository (local ADR 0029,
/// <c>repository-management/requirements.md#the-roadmap-keeps-an-archived-repositorys-past-in-view</c>
/// and <c>#the-roadmap-hides-an-archived-repositorys-open-work</c>): drawn greyed and
/// marked archived with only its finished work, while the window the timeline draws
/// holds some of it; its open work drawn in no band, and never under No repository,
/// unless show archived is on, which draws the whole band.
/// <para>
/// "In view" is the timeline's whole drawn window, as the rest of the plan makes it
/// (person's answer, 2026-10-15): the archived band's own past never stretches it, so
/// work finished long before everything else is past the window's edge.
/// </para>
/// </summary>
public class RoadmapPlanViewArchivedTests
{
    private static readonly List<PlannedRepository> Configured =
    [
        new("backlog", "JSdotNet/Backlog", 1),
        new("budget", "innovadis-dev/Budgetbeheer", 2, Archived: true)
    ];

    /// <summary>Monday 12 October 2026.</summary>
    private static readonly RoadmapForecast OnMonday = new(
        new DateOnly(2026, 10, 12),
        new PacesInUseDto(7m, new Dictionary<string, decimal> { ["backlog"] = 8m, ["budget"] = 4m }));

    private static RoadmapItemDto Item(string title, DateOnly start, DateOnly end, params string[] repositories) =>
        new(Guid.NewGuid(), title, start, end, PlanningPriority.Medium, repositories, null, null, []);

    private static RoadmapGatheredLink Done(string key, string repository, DateOnly started, DateOnly completed) =>
        new(key, key.ToUpperInvariant(), 3, RollupOrigin.Tag, RoadmapProgress.Done, null, [repository],
            StartedOn: started, CompletedOn: completed);

    private static RoadmapTimelineModel Draw(
        IReadOnlyList<RoadmapItemDto> items,
        IReadOnlyDictionary<Guid, RoadmapItemRollupDto>? rollups = null,
        bool showArchived = false) =>
        RoadmapPlanView.From(
            new RoadmapPlanDto([.. items], [], [], null),
            Configured,
            rollups ?? new Dictionary<Guid, RoadmapItemRollupDto>(),
            OnMonday,
            showArchived);

    /// <summary>Finished in September: the drawn window around 12 October reaches back
    /// past it.</summary>
    private static (RoadmapItemDto Item, KeyValuePair<Guid, RoadmapItemRollupDto> Rollup) FinishedLastMonth()
    {
        var item = Item("Close the books", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "budget");
        return (item, new(item.Id, new RoadmapItemRollupDto(
        [
            Done("a", "innovadis-dev/Budgetbeheer", new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11)),
            Done("b", "innovadis-dev/Budgetbeheer", new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 18))
        ], [])));
    }

    [Fact]
    public void Finished_work_in_the_drawn_window_draws_the_archived_band_marked_archived()
    {
        var (finished, rollup) = FinishedLastMonth();

        var view = Draw([finished], new Dictionary<Guid, RoadmapItemRollupDto> { [rollup.Key] = rollup.Value });

        var band = Assert.Single(view.Groups, group => group.Id == "budget");
        Assert.True(band.Archived);

        var bar = Assert.Single(view.Bars);
        Assert.StartsWith("budget::", bar.RowId, StringComparison.Ordinal);
        Assert.Equal((new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 18)), (bar.Start, bar.End));
    }

    [Fact]
    public void An_active_band_is_not_marked_archived()
    {
        var view = Draw([Item("Ship", new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 30), "backlog")]);

        Assert.False(Assert.Single(view.Groups, group => group.Id == "backlog").Archived);
        Assert.False(Assert.Single(view.Groups, group => group.Id == RoadmapPlanView.NoRepositoryGroupId).Archived);
    }

    [Fact]
    public void Finished_work_before_the_drawn_window_draws_no_band_until_the_window_reaches_back_to_it()
    {
        var old = Item("Old books", new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), "budget");
        var rollups = new Dictionary<Guid, RoadmapItemRollupDto>
        {
            [old.Id] = new([Done("a", "innovadis-dev/Budgetbeheer", new DateOnly(2025, 1, 6), new DateOnly(2025, 1, 17))], [])
        };
        var current = Item("Ship", new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 30), "backlog");

        var scrolledPast = Draw([old, current], rollups);

        Assert.DoesNotContain(scrolledPast.Groups, group => group.Id == "budget");
        Assert.DoesNotContain(scrolledPast.Bars, bar => bar.Title == "Old books");
        Assert.DoesNotContain(scrolledPast.Bars, bar => bar.RowId.StartsWith(RoadmapPlanView.NoRepositoryGroupId, StringComparison.Ordinal)
            && bar.Title == "Old books");

        // Work elsewhere on the plan from that January draws the window back over it, and
        // the archived band with it.
        var history = Item("Started the site", new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), "backlog");
        var reachingBack = Draw([old, current, history], rollups);

        Assert.True(Assert.Single(reachingBack.Groups, group => group.Id == "budget").Archived);
        Assert.Contains(reachingBack.Bars, bar => bar.Title == "Old books");
    }

    [Fact]
    public void Open_work_of_an_archived_repository_is_drawn_in_no_band_and_not_under_no_repository()
    {
        var open = Item("Migrate the ledger", new DateOnly(2026, 10, 19), new DateOnly(2026, 11, 13), "budget");

        var view = Draw([open]);

        Assert.DoesNotContain(view.Groups, group => group.Id == "budget");
        Assert.Empty(view.Bars);

        // The No repository band still stands, empty, as it does on every chart.
        Assert.Single(view.Groups, group => group.Id == RoadmapPlanView.NoRepositoryGroupId);
    }

    [Fact]
    public void Open_work_beside_finished_work_leaves_only_the_finished_work_in_the_archived_band()
    {
        var (finished, rollup) = FinishedLastMonth();
        var open = Item("Migrate the ledger", new DateOnly(2026, 10, 19), new DateOnly(2026, 11, 13), "budget");

        var view = Draw([finished, open], new Dictionary<Guid, RoadmapItemRollupDto> { [rollup.Key] = rollup.Value });

        Assert.True(Assert.Single(view.Groups, group => group.Id == "budget").Archived);
        Assert.Equal(["Close the books"], view.Bars.Select(bar => bar.Title));
    }

    [Fact]
    public void Show_archived_draws_the_whole_band_with_its_open_work()
    {
        var (finished, rollup) = FinishedLastMonth();
        var open = Item("Migrate the ledger", new DateOnly(2026, 10, 19), new DateOnly(2026, 11, 13), "budget");

        var view = Draw(
            [finished, open],
            new Dictionary<Guid, RoadmapItemRollupDto> { [rollup.Key] = rollup.Value },
            showArchived: true);

        Assert.True(Assert.Single(view.Groups, group => group.Id == "budget").Archived);
        Assert.Equal(
            ["Close the books", "Migrate the ledger"],
            view.Bars.Select(bar => bar.Title).Order(StringComparer.Ordinal));
        Assert.All(view.Bars, bar => Assert.StartsWith("budget::", bar.RowId, StringComparison.Ordinal));
    }

    [Fact]
    public void Show_archived_draws_work_finished_before_the_window_too()
    {
        var old = Item("Old books", new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), "budget");
        var rollups = new Dictionary<Guid, RoadmapItemRollupDto>
        {
            [old.Id] = new([Done("a", "innovadis-dev/Budgetbeheer", new DateOnly(2025, 1, 6), new DateOnly(2025, 1, 17))], [])
        };

        var view = Draw([old, Item("Ship", new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 30), "backlog")], rollups, showArchived: true);

        Assert.Contains(view.Bars, bar => bar.Title == "Old books");
    }

    [Fact]
    public void An_open_item_filed_in_an_active_and_an_archived_repository_keeps_its_active_part()
    {
        var both = Item("Shared rollout", new DateOnly(2026, 10, 19), new DateOnly(2026, 11, 13), "backlog", "budget");

        var view = Draw([both]);

        var bar = Assert.Single(view.Bars);
        Assert.StartsWith("backlog::", bar.RowId, StringComparison.Ordinal);
        Assert.DoesNotContain(view.Groups, group => group.Id == "budget");
    }

    /// <summary>An item sized by its effort, 4 points in each repository, laid out from
    /// Monday: backlog's part at 8 points a week ends first, budget's at 4 later. Hidden or
    /// drawn, budget's part is part of the item's window, so a drag on the backlog part
    /// stores the same window either way, and a pin is measured from the item's real end.</summary>
    [Fact]
    public void A_drag_on_the_active_part_measures_the_item_over_its_hidden_archived_part_too()
    {
        var item = new RoadmapItemDto(
            Guid.NewGuid(), "Shared rollout", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), PlanningPriority.Medium,
            ["backlog", "budget"], null, null, [], null, "", null, ImportPlacement.Effort);
        var rollups = new Dictionary<Guid, RoadmapItemRollupDto>
        {
            [item.Id] = new(
            [
                new RoadmapGatheredLink("a", "A", 4, RollupOrigin.Tag, RoadmapProgress.Planned, null, ["JSdotNet/Backlog"]),
                new RoadmapGatheredLink("b", "B", 4, RollupOrigin.Tag, RoadmapProgress.Planned, null, ["innovadis-dev/Budgetbeheer"])
            ], [])
        };

        var hidden = Draw([item], rollups);
        var whole = Draw([item], rollups, showArchived: true);

        var visible = Assert.Single(hidden.Bars);
        Assert.Contains("@backlog", visible.Id, StringComparison.Ordinal);
        var budgetEnd = whole.Bars.Single(bar => bar.Id.Contains("@budget", StringComparison.Ordinal)).End;
        Assert.True(budgetEnd > visible.End);

        var move = new RoadmapChange(visible.Id, visible.RowId, visible.Start.AddDays(7), visible.End.AddDays(7), RoadmapDrag.Move);
        Assert.Equal(
            RoadmapPlanView.ItemWindowFor(whole.MeasuredBars, visible, move),
            RoadmapPlanView.ItemWindowFor(hidden.MeasuredBars, visible, move));
        Assert.Equal(budgetEnd.AddDays(7), RoadmapPlanView.ItemWindowFor(hidden.MeasuredBars, visible, move).End);

        var pull = new RoadmapChange(visible.Id, visible.RowId, visible.Start, visible.End.AddDays(2), RoadmapDrag.ResizeEnd);
        Assert.Equal(budgetEnd.AddDays(2), RoadmapPlanView.PinnedEndFor(hidden.MeasuredBars, visible, pull));

        // The hidden part is measured, never drawn.
        Assert.DoesNotContain(hidden.Bars, bar => bar.Id.Contains("@budget", StringComparison.Ordinal));
    }

    [Fact]
    public void The_active_part_of_an_item_says_its_other_part_is_in_an_archived_repository()
    {
        var both = Item("Shared rollout", new DateOnly(2026, 10, 19), new DateOnly(2026, 11, 13), "backlog", "budget");

        var bar = Assert.Single(Draw([both]).Bars);

        Assert.Contains("one of 2 repository parts, 1 in an archived repository and not drawn", bar.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_arrow_to_hidden_open_work_is_not_drawn()
    {
        var hidden = Item("Migrate the ledger", new DateOnly(2026, 10, 19), new DateOnly(2026, 11, 13), "budget");
        var waiting = Item("Report", new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 27), "backlog") with { DependsOn = [hidden.Id] };

        var view = Draw([hidden, waiting]);

        Assert.Equal(["Report"], view.Bars.Select(bar => bar.Title));
        Assert.Empty(view.Links);
    }
}
