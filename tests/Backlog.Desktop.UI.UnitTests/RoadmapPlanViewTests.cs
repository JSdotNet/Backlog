using Backlog.UI.Components.Roadmap;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// How a stored plan becomes a picture: which band something lands in, what a lane
/// becomes, how priority is made visible, and which arrows survive. Asserted on the
/// mapping rather than on rendered markup, because every one of these decisions is
/// made before a single element exists.
/// </summary>
public class RoadmapPlanViewTests
{
    private static readonly List<PlannedRepository> Configured =
    [
        new("backlog", "JSdotNet/Backlog", 1),
        new("fincent", "JSdotNet/Fincent", 2)
    ];

    private static RoadmapItemDto Item(
        string title,
        int startDay = 5,
        int endDay = 9,
        PlanningPriority priority = PlanningPriority.Medium,
        string[]? repositories = null,
        string? lane = null,
        Guid[]? dependsOn = null,
        Guid? id = null,
        Guid? taskId = null,
        string tag = "",
        string[]? knowledge = null,
        ImportPlacement? placedBy = null) =>
        new(
            id ?? Guid.NewGuid(),
            title,
            new DateOnly(2026, 1, startDay),
            new DateOnly(2026, 1, endDay),
            priority,
            repositories ?? [],
            lane,
            taskId,
            dependsOn ?? [],
            null,
            tag,
            knowledge,
            placedBy);

    private static RoadmapMilestoneDto Milestone(
        string title,
        int day = 30,
        MilestoneKind kind = MilestoneKind.Release,
        string[]? repositories = null,
        Guid[]? dependsOn = null,
        Guid? id = null,
        bool planWide = false) =>
        new(
            id ?? Guid.NewGuid(),
            title,
            new DateOnly(2026, 1, day),
            kind,
            repositories ?? [],
            null,
            dependsOn ?? [],
            planWide);

    private static RoadmapPlanDto Plan(
        IEnumerable<RoadmapItemDto>? items = null,
        IEnumerable<RoadmapMilestoneDto>? milestones = null,
        IEnumerable<PlanContradictionDto>? contradictions = null,
        IReadOnlyDictionary<string, int>? bands = null) =>
        new([.. items ?? []], [.. milestones ?? []], [.. contradictions ?? []], bands);

    /// <summary>The bands drawn for configured repositories, without the no-repository
    /// band every chart ends with.</summary>
    private static IEnumerable<RoadmapGroup> RepositoryBands(RoadmapTimelineModel view) =>
        view.Groups.Where(band => band.Id != RoadmapPlanView.NoRepositoryGroupId);

    [Fact]
    public void AnEmptyPlanHasNothingToDraw()
    {
        var view = RoadmapPlanView.From(Plan(), Configured);

        Assert.False(view.HasAnythingToDraw);
        Assert.Empty(view.Groups);
        Assert.Empty(view.Bars);
    }

    [Fact]
    public void BandsFollowTheOrderRepositoriesAreConfiguredIn()
    {
        var view = RoadmapPlanView.From(
            Plan([Item("In Fincent", repositories: ["fincent"]), Item("In Backlog", repositories: ["backlog"])]),
            Configured);

        Assert.Equal(["backlog", "fincent", RoadmapPlanView.NoRepositoryGroupId], view.Groups.Select(group => group.Id));
        // Labelled by alias, not full name: the label is written down the side of the
        // band, so its length is a floor on how short the band can be. The full name
        // is still what the Repository filter offers.
        Assert.Equal(["backlog", "fincent", RoadmapPlanView.NoRepositoryGroupTitle], view.Groups.Select(group => group.Title));
    }

    [Fact]
    public void ARepositoryBandWithNothingInItIsNotDrawn()
    {
        var view = RoadmapPlanView.From(Plan([Item("Only in Backlog", repositories: ["backlog"])]), Configured);

        // Fincent has nothing planned, so it draws no band; the no-repository band is
        // drawn whatever is in it.
        Assert.Equal(["backlog", RoadmapPlanView.NoRepositoryGroupId], view.Groups.Select(group => group.Id));
    }

    [Fact]
    public void WorkWithNoRepositoryLandsInTheNoRepositoryBand_Last()
    {
        var view = RoadmapPlanView.From(
            Plan([Item("Filed", repositories: ["backlog"]), Item("Not filed")]),
            Configured);

        Assert.Equal(["backlog", RoadmapPlanView.NoRepositoryGroupId], view.Groups.Select(group => group.Id));
        var notFiled = view.Bars.Single(bar => bar.Title == "Not filed");
        Assert.StartsWith($"{RoadmapPlanView.NoRepositoryGroupId}::", notFiled.RowId, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNoRepositoryBandIsDrawnWithOneEmptyPlannedRow_WhenNothingIsFiledThere()
    {
        var view = RoadmapPlanView.From(Plan([Item("Filed", repositories: ["backlog"])]), Configured);

        var band = view.Groups[^1];
        Assert.Equal(RoadmapPlanView.NoRepositoryGroupId, band.Id);
        Assert.Equal(RoadmapPlanView.NoRepositoryGroupTitle, band.Title);

        // Last and colourless is enough to tell it apart: it shows no name, while each
        // repository's band does.
        Assert.True(band.Unnamed);
        Assert.All(view.Groups.SkipLast(1), group => Assert.False(group.Unnamed));

        // One lane to place the next project in before it has a repository, with room
        // under the band's name for the default pace it would be placed at.
        var row = Assert.Single(band.RowList);
        Assert.Equal($"{RoadmapPlanView.NoRepositoryGroupId}::Planned", row.Id);
        Assert.Equal(string.Empty, row.Title);
        Assert.Equal(RoadmapRowKind.Bars, row.Kind);
        Assert.Equal(RoadmapPlanView.PaceRows, band.ContentRows);
        Assert.DoesNotContain(view.Bars, bar => bar.RowId == row.Id);
    }

    [Fact]
    public void AnAliasThatIsNoLongerConfigured_LandsInTheNoRepositoryBandRatherThanMakingABandOfItsOwn()
    {
        var view = RoadmapPlanView.From(Plan([Item("Old work", repositories: ["retired"])]), Configured);

        var band = Assert.Single(view.Groups);
        Assert.Equal(RoadmapPlanView.NoRepositoryGroupId, band.Id);
        Assert.Contains(Assert.Single(view.Bars).RowId, band.RowList.Select(row => row.Id));
    }

    [Fact]
    public void AnAliasThatIsNoLongerConfigured_ComesBackWhenItsRepositoryIsConfiguredAgain()
    {
        var plan = Plan([Item("Old work", repositories: ["retired"])]);

        var view = RoadmapPlanView.From(plan, [.. Configured, new("retired", "JSdotNet/Retired", 3)]);

        Assert.Equal(["retired", RoadmapPlanView.NoRepositoryGroupId], view.Groups.Select(group => group.Id));
        Assert.StartsWith("retired::", Assert.Single(view.Bars).RowId, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkFiledByARepositorysFullName_LandsInThatRepositorysBand_WhateverItsCase()
    {
        // An imported plan may name its repositories in owner/name form rather than by
        // alias, and in its own casing.
        var item = Item("Imported", repositories: ["jsdotnet/backlog", "JSDOTNET/FINCENT"]);

        var view = RoadmapPlanView.From(Plan([item]), Configured);

        Assert.Equal(["backlog", "fincent"], RepositoryBands(view).Select(group => group.Id));
        Assert.Equal(2, view.Bars.Count);
        Assert.All(view.Bars, bar => Assert.Contains(bar.FacetList, facet => facet.Name == "Repository"
            && (facet.Value == "JSdotNet/Backlog" || facet.Value == "JSdotNet/Fincent")));
    }

    [Fact]
    public void WorkNamingOneConfiguredAndOneUnknownRepository_IsDrawnInTheConfiguredBandAlone()
    {
        var view = RoadmapPlanView.From(
            Plan([Item("Half known", repositories: ["backlog", "retired"])]),
            Configured);

        Assert.Equal("backlog", Assert.Single(RepositoryBands(view)).Id);

        // Not also drawn under no repository: one configured name is enough to file it.
        Assert.StartsWith("backlog::", Assert.Single(view.Bars).RowId, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkNamingTwoRepositoriesIsDrawnOncePerRepository_EachPartInItsOwnBand()
    {
        var item = Item("Spans both", repositories: ["backlog", "fincent"]);
        var view = RoadmapPlanView.From(Plan([item]), Configured);

        Assert.Equal(2, view.Bars.Count);
        var backlog = Assert.Single(view.Bars, bar => bar.RowId.StartsWith("backlog::", StringComparison.Ordinal));
        var fincent = Assert.Single(view.Bars, bar => bar.RowId.StartsWith("fincent::", StringComparison.Ordinal));

        // Both parts are the one stored item, sharing its window: opening or moving
        // either is opening or moving the item.
        Assert.Equal(item.Id, RoadmapPlanView.NodeIdOf(backlog.Id));
        Assert.Equal(item.Id, RoadmapPlanView.NodeIdOf(fincent.Id));
        Assert.NotEqual(backlog.Id, fincent.Id);
        Assert.Equal((backlog.Start, backlog.End), (fincent.Start, fincent.End));

        // Each part is found under its own repository only.
        Assert.Equal([new RoadmapFacet("Repository", "JSdotNet/Backlog")], backlog.FacetList.Where(facet => facet.Name == "Repository"));
        Assert.Equal([new RoadmapFacet("Repository", "JSdotNet/Fincent")], fincent.FacetList.Where(facet => facet.Name == "Repository"));
        Assert.Contains("one of 2 repository parts", backlog.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkNamingOneRepositoryKeepsItsBareId()
    {
        var item = Item("One place", repositories: ["backlog"]);

        var bar = Assert.Single(RoadmapPlanView.From(Plan([item]), Configured).Bars);

        Assert.Equal(item.Id.ToString(), bar.Id);
        Assert.DoesNotContain("repository parts", bar.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void EachRepositorysPart_CarriesTheTasksFiledThere()
    {
        var item = Item("Spans both", repositories: ["backlog", "fincent"]);
        var rollup = new RoadmapItemRollupDto(
        [
            new RoadmapGatheredLink("a", "A", 3, RollupOrigin.Tag, RoadmapProgress.Done, null, ["JSdotNet/Backlog"]),
            new RoadmapGatheredLink("b", "B", 5, RollupOrigin.Tag, RoadmapProgress.Planned, null, ["fincent"]),
            // Filed in both: counted in both parts.
            new RoadmapGatheredLink("c", "C", 2, RollupOrigin.Tag, RoadmapProgress.Planned, null, ["JSdotNet/Backlog", "JSdotNet/Fincent"]),
            // Filed nowhere: goes to the first part, so it stays in the item's progress.
            new RoadmapGatheredLink("d", "D", 1, RollupOrigin.Tag, RoadmapProgress.Planned)
        ], []);

        var view = RoadmapPlanView.From(
            Plan([item]),
            Configured,
            new Dictionary<Guid, RoadmapItemRollupDto> { [item.Id] = rollup });

        var backlog = Assert.Single(view.Bars, bar => bar.RowId.StartsWith("backlog::", StringComparison.Ordinal));
        var fincent = Assert.Single(view.Bars, bar => bar.RowId.StartsWith("fincent::", StringComparison.Ordinal));

        Assert.Equal(["a", "c", "d"], backlog.StepList.Select(step => step.Id));
        Assert.Equal(["b", "c"], fincent.StepList.Select(step => step.Id));
        Assert.Equal(3, backlog.DoneEffort);
        Assert.Equal(0, fincent.DoneEffort);
    }

    // --- Each repository draws its own part at its own pace -----------------------

    /// <summary>Monday 12 October 2026, on the default working week: backlog gets through 8
    /// points a week, fincent 4, and work under no repository the default 7.</summary>
    private static readonly RoadmapForecast OnMonday12October = new(
        new DateOnly(2026, 10, 12),
        new PacesInUseDto(7m, new Dictionary<string, decimal> { ["backlog"] = 8m, ["fincent"] = 4m }));

    private static DateOnly October(int day) => new(2026, 10, day);

    /// <summary>An item the import sized by its effort, filed under
    /// <paramref name="repositories"/>, stored over a window long gone.</summary>
    private static RoadmapItemDto SizedByEffort(params string[] repositories) =>
        new(Guid.NewGuid(), "Plan", October(1), October(2), PlanningPriority.Medium, repositories, null, null, [],
            PlacedByImport: ImportPlacement.Effort);

    private static RoadmapGatheredLink Filed(string key, int? effort, params string[] repositories) =>
        new(key, key.ToUpperInvariant(), effort, RollupOrigin.Tag, RoadmapProgress.Planned, null, repositories);

    private static RoadmapTimelineModel OnMonday(
        RoadmapItemDto item,
        IReadOnlyList<PlannedRepository> configured,
        RoadmapForecast forecast,
        params RoadmapGatheredLink[] tasks) =>
        RoadmapPlanView.From(
            Plan([item]),
            configured,
            new Dictionary<Guid, RoadmapItemRollupDto> { [item.Id] = new(tasks, []) },
            forecast);

    /// <summary>AC1: an 8-point task filed under backlog (8 a week) and fincent (4 a week)
    /// counts in full in both parts, each at its own pace.</summary>
    [Fact]
    public void ATaskFiledInTwoRepositories_IsDrawnInEachBand_AtThatBandsOwnPace()
    {
        var item = SizedByEffort("backlog", "fincent");

        var view = OnMonday(item, Configured, OnMonday12October, Filed("a", 8, "JSdotNet/Backlog", "JSdotNet/Fincent"));

        var backlog = Assert.Single(view.Bars, bar => bar.RowId.StartsWith("backlog::", StringComparison.Ordinal));
        var fincent = Assert.Single(view.Bars, bar => bar.RowId.StartsWith("fincent::", StringComparison.Ordinal));
        Assert.Equal((October(12), October(16)), (backlog.Start, backlog.End));
        Assert.Equal((October(12), October(23)), (fincent.Start, fincent.End));
    }

    /// <summary>AC4: one repository's slice of a large plan. 52 points over seven
    /// repositories, 16 of them open in fincent at 40 a week and waiting on no other
    /// repository: fincent's band spans two days, not the whole plan's window.</summary>
    [Fact]
    public void OneRepositorysSliceOfALargePlan_SpansOnlyThatRepositorysWork()
    {
        string[] aliases = ["app", "site", "docs", "api", "ops", "data", "fincent"];
        var configured = aliases.Select((alias, index) => new PlannedRepository(alias, $"JSdotNet/{alias}", index % 5 + 1)).ToList();
        var forecast = new RoadmapForecast(
            October(12),
            new PacesInUseDto(7m, aliases.ToDictionary(alias => alias, alias => alias == "fincent" ? 40m : 6m)));

        var item = SizedByEffort(aliases);
        RoadmapGatheredLink[] tasks =
        [
            .. aliases.Where(alias => alias != "fincent").Select((alias, index) => Filed($"t{index}", 6, $"JSdotNet/{alias}")),
            Filed("f1", 8, "JSdotNet/fincent"),
            Filed("f2", 8, "JSdotNet/fincent")
        ];

        var view = OnMonday(item, configured, forecast, tasks);

        var fincent = Assert.Single(view.Bars, bar => bar.RowId.StartsWith("fincent::", StringComparison.Ordinal));
        Assert.Equal((October(12), October(13)), (fincent.Start, fincent.End));
        Assert.Equal(52, tasks.Sum(task => task.Effort));
        Assert.All(
            view.Bars.Where(bar => bar != fincent),
            bar => Assert.Equal((October(12), October(16)), (bar.Start, bar.End)));
    }

    /// <summary>AC6: a repository the item names that holds none of its tasks draws over the
    /// window the other parts make, so the item still shows it is filed there.</summary>
    [Fact]
    public void ANamedRepositoryHoldingNoTask_DrawsOverTheWindowTheOtherPartsMake()
    {
        var item = SizedByEffort("backlog", "fincent");

        var view = OnMonday(item, Configured, OnMonday12October, Filed("a", 12, "JSdotNet/Backlog"));

        var backlog = Assert.Single(view.Bars, bar => bar.RowId.StartsWith("backlog::", StringComparison.Ordinal));
        var fincent = Assert.Single(view.Bars, bar => bar.RowId.StartsWith("fincent::", StringComparison.Ordinal));
        Assert.Equal((October(12), October(21)), (backlog.Start, backlog.End));
        Assert.Equal((backlog.Start, backlog.End), (fincent.Start, fincent.End));
        Assert.Empty(fincent.StepList);
    }

    /// <summary>AC6: a part holding only unestimated tasks takes one working week, at
    /// whatever pace its repository goes.</summary>
    [Fact]
    public void APartWithNothingSized_TakesOneWorkingWeek()
    {
        var item = SizedByEffort("backlog", "fincent");

        var view = OnMonday(item, Configured, OnMonday12October, Filed("a", 2, "JSdotNet/Backlog"), Filed("b", null, "JSdotNet/Fincent"));

        var fincent = Assert.Single(view.Bars, bar => bar.RowId.StartsWith("fincent::", StringComparison.Ordinal));
        Assert.Equal((October(12), October(16)), (fincent.Start, fincent.End));
    }

    [Fact]
    public void AnArrowToASplitItem_RunsWithinEachRepository()
    {
        var first = Item("First", repositories: ["backlog", "fincent"]);
        var then = Item("Then", startDay: 12, endDay: 16, repositories: ["backlog", "fincent"], dependsOn: [first.Id]);

        var view = RoadmapPlanView.From(Plan([first, then]), Configured);

        Assert.Equal(2, view.Links.Count);
        Assert.Contains(new RoadmapLink($"{first.Id}@backlog", $"{then.Id}@backlog"), view.Links);
        Assert.Contains(new RoadmapLink($"{first.Id}@fincent", $"{then.Id}@fincent"), view.Links);
    }

    [Fact]
    public void LanesBecomeRowsInsideTheirBand()
    {
        var view = RoadmapPlanView.From(
            Plan(
            [
                Item("Platform work", repositories: ["backlog"], lane: "platform"),
                Item("Migration work", startDay: 12, endDay: 16, repositories: ["backlog"], lane: "migration"),
                Item("More platform", startDay: 19, endDay: 23, repositories: ["backlog"], lane: "platform")
            ]),
            Configured);

        var band = Assert.Single(RepositoryBands(view));
        Assert.Equal(["platform", "migration"], band.RowList.Select(row => row.Title));
        Assert.All(band.RowList, row => Assert.Equal(RoadmapRowKind.Bars, row.Kind));
    }

    [Fact]
    public void WorkWithNoLane_GetsTheRepositorysOwnRow_WithNoLaneTitle()
    {
        var view = RoadmapPlanView.From(Plan([Item("Unfiled lane", repositories: ["backlog"])]), Configured);

        var row = Assert.Single(view.Groups[0].RowList);
        Assert.Equal(string.Empty, row.Title);
        Assert.Equal("Planned", RoadmapPlanView.LaneOf(row.Id));
    }

    [Fact]
    public void WorkThatOverlapsInOneLane_IsStackedOnRowsOfItsOwn_SoNoBarHidesAnother()
    {
        // Three imported plans in the default lane, every window overlapping the
        // other two: each needs a row of its own.
        var view = RoadmapPlanView.From(
            Plan(
            [
                Item("A", startDay: 5, endDay: 16, repositories: ["backlog"]),
                Item("C", startDay: 5, endDay: 9, repositories: ["backlog"]),
                Item("B", startDay: 8, endDay: 30, repositories: ["backlog"])
            ]),
            Configured);

        var rows = Assert.Single(RepositoryBands(view)).RowList;
        Assert.All(rows, row => Assert.Equal(string.Empty, row.Title));
        Assert.Equal(3, rows.Select(row => row.Id).Distinct().Count());

        // Every bar is on a row the chart draws, and no two bars sharing a row overlap.
        Assert.All(view.Bars, bar => Assert.Contains(bar.RowId, rows.Select(row => row.Id)));
        foreach (var row in view.Bars.GroupBy(bar => bar.RowId))
        {
            var ordered = row.OrderBy(bar => bar.Start).ToList();
            for (var index = 1; index < ordered.Count; index++)
            {
                Assert.True(ordered[index].Start > ordered[index - 1].End, $"{ordered[index - 1].Title} and {ordered[index].Title} overlap on {row.Key}");
            }
        }
    }

    [Fact]
    public void WorkThatDoesNotOverlap_SharesItsLanesRow()
    {
        var view = RoadmapPlanView.From(
            Plan(
            [
                Item("First", startDay: 5, endDay: 9, repositories: ["backlog"]),
                Item("Second", startDay: 12, endDay: 16, repositories: ["backlog"]),
                Item("Third", startDay: 10, endDay: 11, repositories: ["backlog"])
            ]),
            Configured);

        var row = Assert.Single(Assert.Single(RepositoryBands(view)).RowList);
        Assert.All(view.Bars, bar => Assert.Equal(row.Id, bar.RowId));
    }

    [Fact]
    public void AStackedRowIsReused_OnceTheWorkOnItHasEnded()
    {
        var view = RoadmapPlanView.From(
            Plan(
            [
                Item("Long", startDay: 5, endDay: 30, repositories: ["backlog"]),
                Item("Short", startDay: 5, endDay: 9, repositories: ["backlog"]),
                Item("After short", startDay: 12, endDay: 16, repositories: ["backlog"])
            ]),
            Configured);

        Assert.Equal(2, Assert.Single(RepositoryBands(view)).RowList.Count);
        Assert.Equal(
            view.Bars.Single(bar => bar.Title == "Short").RowId,
            view.Bars.Single(bar => bar.Title == "After short").RowId);
    }

    [Fact]
    public void AStackedRow_StillNamesItsLane()
    {
        var view = RoadmapPlanView.From(
            Plan(
            [
                Item("A", repositories: ["backlog"], lane: "platform"),
                Item("B", repositories: ["backlog"], lane: "platform")
            ]),
            Configured);

        var rows = Assert.Single(RepositoryBands(view)).RowList;
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("platform", RoadmapPlanView.LaneOf(row.Id)));
        Assert.Null(RoadmapPlanView.LaneOf("no-separator"));
    }

    [Fact]
    public void EveryDateSharesOneBandAtTheTopOfTheChart()
    {
        var view = RoadmapPlanView.From(
            Plan(
                [Item("Work", repositories: ["backlog"])],
                [Milestone("1.0", repositories: ["backlog"]), Milestone("Freeze", 20, repositories: ["fincent"])]),
            Configured);

        // First band, whatever the repositories say: it is where a reader looks for the
        // dates everything else is measured against.
        Assert.Equal(RoadmapPlanView.MilestoneGroupId, view.Groups[0].Id);
        Assert.Equal(RoadmapPlanView.MilestoneGroupTitle, view.Groups[0].Title);

        var row = Assert.Single(view.Groups[0].RowList);
        Assert.Equal(RoadmapRowKind.Milestones, row.Kind);

        // Both dates on it, not one per repository band.
        Assert.Equal(2, view.Milestones.Count);
        Assert.All(view.Milestones, marker => Assert.Equal(row.Id, marker.RowId));

        // And no milestones row inside the repository band.
        var backlog = view.Groups.Single(band => band.Id == "backlog");
        Assert.All(backlog.RowList, candidate => Assert.Equal(RoadmapRowKind.Bars, candidate.Kind));
    }

    [Fact]
    public void TheDatesBandTakesNoColour_BecauseItIsNotARepository()
    {
        var view = RoadmapPlanView.From(
            Plan([Item("Work", repositories: ["backlog"])], [Milestone("1.0")]),
            Configured);

        Assert.Null(view.Groups[0].Color);
        Assert.Equal("var(--color-band-1)", view.Groups[1].Color);
    }

    [Fact]
    public void APlanOfNothingButDatesIsStillDrawn()
    {
        var view = RoadmapPlanView.From(Plan(milestones: [Milestone("1.0")]), Configured);

        // The dates band, and the no-repository band every chart ends with.
        Assert.Equal(
            [RoadmapPlanView.MilestoneGroupId, RoadmapPlanView.NoRepositoryGroupId],
            view.Groups.Select(band => band.Id));
        Assert.Empty(view.Bars);
        Assert.Single(view.Milestones);
    }

    [Fact]
    public void ADateReadAgainstTheWholePlanAsksForALine_AndSaysSo()
    {
        var view = RoadmapPlanView.From(
            Plan(milestones: [Milestone("Freeze", kind: MilestoneKind.Freeze, planWide: true)]),
            Configured);

        var marker = Assert.Single(view.Milestones);

        Assert.True(marker.Line);
        Assert.Contains("read against the whole plan", marker.Detail);
    }

    [Fact]
    public void AnOrdinaryDateAsksForNoLine()
    {
        var view = RoadmapPlanView.From(Plan(milestones: [Milestone("1.0")]), Configured);

        Assert.False(Assert.Single(view.Milestones).Line);
    }

    // --- Band colours ---------------------------------------------------------
    //
    // Which hue a repository wears is settled before this type is called — it is a
    // fact about the repository, chosen in Settings, and three other surfaces are
    // reading the same answer. What is left here is the mapping: a number becomes a
    // token, and the two bands that are not repositories stay neutral.

    [Fact]
    public void ABandWearsTheHueItWasHandedRatherThanOneOfItsOwn()
    {
        var view = RoadmapPlanView.From(
            Plan([Item("In Backlog", repositories: ["backlog"]), Item("In Fincent", repositories: ["fincent"])]),
            [new PlannedRepository("backlog", "JSdotNet/Backlog", 4), new PlannedRepository("fincent", "JSdotNet/Fincent", 1)]);

        Assert.Equal("var(--color-band-4)", view.Groups[0].Color);
        Assert.Equal("var(--color-band-1)", view.Groups[1].Color);
    }

    [Fact]
    public void ARepositoryHandedNoHueDrawsNeutral()
    {
        var view = RoadmapPlanView.From(
            Plan([Item("In Backlog", repositories: ["backlog"])]),
            [new PlannedRepository("backlog", "JSdotNet/Backlog")]);

        // Not a hue picked here to fill the gap: this type declines to choose, so
        // "nobody said" draws as nothing rather than as one more project.
        Assert.Null(Assert.Single(RepositoryBands(view)).Color);
    }

    [Theory]
    [InlineData(PlanningPriority.Critical, 0)]
    [InlineData(PlanningPriority.High, 1)]
    [InlineData(PlanningPriority.Medium, 2)]
    [InlineData(PlanningPriority.Low, 3)]
    public void PriorityBecomesAShadeStep_StrongestFirst(PlanningPriority priority, int shade)
    {
        var view = RoadmapPlanView.From(
            Plan([Item("Work", repositories: ["backlog"], priority: priority)]),
            Configured);

        Assert.Equal(shade, Assert.Single(view.Bars).ShadeStep);
    }

    [Fact]
    public void EachRepositoryBandTakesItsOwnHue_FromTheSanctionedIdentitySet()
    {
        var view = RoadmapPlanView.From(
            Plan([Item("In Backlog", repositories: ["backlog"]), Item("In Fincent", repositories: ["fincent"])]),
            Configured);

        Assert.Equal(
            ["var(--color-band-1)", "var(--color-band-2)"],
            RepositoryBands(view).Select(band => band.Color));
    }

    [Fact]
    public void TheNoRepositoryBandTakesNoColour_BecauseItIsNotAProject()
    {
        var view = RoadmapPlanView.From(
            Plan([Item("Filed", repositories: ["backlog"]), Item("Not filed")]),
            Configured);

        var noRepository = view.Groups.Single(band => band.Id == RoadmapPlanView.NoRepositoryGroupId);

        // A neutral band reads as "nobody said" rather than as one more project.
        Assert.Null(noRepository.Color);
        Assert.Equal("var(--color-band-1)", view.Groups[0].Color);
    }

    [Fact]
    public void ARepositoryKeepsItsHueWhetherOrNotItsNeighboursHaveWork()
    {
        // Only the second repository has work in it, and it still draws in its own hue
        // rather than sliding up to the first one.
        //
        // This is a deliberate change from when the roadmap allocated its own hues by
        // counting the bands it drew. A hue now says which repository, everywhere — the
        // filter chip above the backlog says Fincent is band 2 — so a roadmap that
        // renumbered by what happened to have work in it would make the same project two
        // colours on two screens.
        var view = RoadmapPlanView.From(Plan([Item("Only Fincent", repositories: ["fincent"])]), Configured);

        Assert.Equal("var(--color-band-2)", Assert.Single(RepositoryBands(view)).Color);
    }

    [Fact]
    public void ASixthRepositoryRepeatsTheFirstHue_RatherThanGrowingTheSet()
    {
        // Six repositories as the host resolved them: the set wraps, so the sixth is
        // handed the first hue again.
        List<PlannedRepository> six =
        [
            new("one", "org/one", 1), new("two", "org/two", 2), new("three", "org/three", 3),
            new("four", "org/four", 4), new("five", "org/five", 5), new("six", "org/six", 1)
        ];

        var view = RoadmapPlanView.From(
            Plan(six.Select(repository => Item(repository.Alias, repositories: [repository.Alias]))),
            six);

        Assert.Equal(
            [
                "var(--color-band-1)", "var(--color-band-2)", "var(--color-band-3)",
                "var(--color-band-4)", "var(--color-band-5)", "var(--color-band-1)"
            ],
            RepositoryBands(view).Select(band => band.Color));
    }

    [Fact]
    public void PriorityIsAlsoWrittenInWords_SoAShadeIsNeverTheOnlyCarrier()
    {
        var view = RoadmapPlanView.From(
            Plan([Item("Work", repositories: ["backlog"], priority: PlanningPriority.Critical)]),
            Configured);

        var bar = Assert.Single(view.Bars);
        Assert.Contains("Critical priority", bar.Detail);
        Assert.Contains(new RoadmapFacet("Priority", "Critical"), bar.FacetList);
    }

    [Fact]
    public void DatesAreCarriedThroughUntouched_BothEndsInclusive()
    {
        var view = RoadmapPlanView.From(
            Plan([Item("Work", startDay: 5, endDay: 9, repositories: ["backlog"])]),
            Configured);

        var bar = Assert.Single(view.Bars);
        Assert.Equal(new DateOnly(2026, 1, 5), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 9), bar.End);
        Assert.Equal(5, bar.Days);
    }

    [Fact]
    public void AnArrowRunsFromTheThingThatHasToLandFirst()
    {
        var design = Item("Design", repositories: ["backlog"]);
        var build = Item("Build", startDay: 12, endDay: 16, repositories: ["backlog"], dependsOn: [design.Id]);

        var view = RoadmapPlanView.From(Plan([design, build]), Configured);

        var link = Assert.Single(view.Links);
        Assert.Equal(design.Id.ToString(), link.FromId);
        Assert.Equal(build.Id.ToString(), link.ToId);
    }

    [Fact]
    public void AnArrowToSomethingNotInThePlanIsNotDrawn()
    {
        var view = RoadmapPlanView.From(
            Plan([Item("Waiting", repositories: ["backlog"], dependsOn: [Guid.NewGuid()])]),
            Configured);

        Assert.Empty(view.Links);
    }

    [Fact]
    public void AContradictionIsSaidInWordsAsWellAsDrawn()
    {
        var design = Item("Design", startDay: 5, endDay: 16, repositories: ["backlog"]);
        var build = Item("Build", startDay: 12, endDay: 23, repositories: ["backlog"], dependsOn: [design.Id]);

        var view = RoadmapPlanView.From(
            Plan([design, build], contradictions: [new PlanContradictionDto(build.Id, design.Id, "overlaps")]),
            Configured);

        var bar = view.Bars.Single(candidate => candidate.Id == build.Id.ToString());
        Assert.Contains("starts before what it waits for has finished", bar.Detail);
    }

    [Fact]
    public void ALinkedBacklogEntryIsMentioned_ButNothingIsReadThroughIt()
    {
        var view = RoadmapPlanView.From(
            Plan([Item("Linked", repositories: ["backlog"], taskId: Guid.NewGuid())]),
            Configured);

        Assert.Contains("linked to a backlog entry", Assert.Single(view.Bars).Detail);
    }

    [Fact]
    public void TheTagIsOfferedAsAFacet_SoTheTimelineCanFilterByIt()
    {
        var view = RoadmapPlanView.From(
            Plan([Item("Work", repositories: ["backlog"], tag: "sync")]),
            Configured);

        var bar = Assert.Single(view.Bars);
        Assert.Contains(new RoadmapFacet("Tag", "sync"), bar.FacetList);
        Assert.Contains("tagged sync", bar.Detail);
    }

    [Fact]
    public void TheKnowledgeReferenceCountIsSaidInTheDetail()
    {
        var view = RoadmapPlanView.From(
            Plan([Item("Work", repositories: ["backlog"], tag: "sync", knowledge: ["a.md", "b.md#h"])]),
            Configured);

        Assert.Contains("references 2 knowledge chapters", Assert.Single(view.Bars).Detail);
    }

    [Fact]
    public void AReleaseAndAFreezeGetDifferentGlyphs_AndBothSayWhichTheyAre()
    {
        var view = RoadmapPlanView.From(
            Plan(milestones:
            [
                Milestone("1.0", 20, MilestoneKind.Release, ["backlog"]),
                Milestone("Freeze", 25, MilestoneKind.Freeze, ["backlog"])
            ]),
            Configured);

        var release = view.Milestones.Single(milestone => milestone.Title == "1.0");
        var freeze = view.Milestones.Single(milestone => milestone.Title == "Freeze");

        Assert.Equal(RoadmapMarker.Star, release.Marker);
        Assert.Equal(RoadmapMarker.Square, freeze.Marker);
        Assert.Equal("Release", release.Detail);
        Assert.Equal("Freeze", freeze.Detail);
    }

    [Fact]
    public void WithNoRepositoriesConfiguredAtAll_TheWholePlanLandsInTheNoRepositoryBand()
    {
        var view = RoadmapPlanView.From(Plan([Item("Work", repositories: ["backlog"])]), []);

        var band = Assert.Single(view.Groups);
        Assert.Equal(RoadmapPlanView.NoRepositoryGroupId, band.Id);
        Assert.Single(view.Bars);
    }

    [Fact]
    public void ABarIdIsThePlansOwnId_SoAGestureCanBeTracedBackToIt()
    {
        var item = Item("Work", repositories: ["backlog"]);

        var view = RoadmapPlanView.From(Plan([item]), Configured);

        Assert.Equal(item.Id, RoadmapPlanView.NodeIdOf(Assert.Single(view.Bars).Id));
        Assert.Null(RoadmapPlanView.NodeIdOf("not-an-id"));
    }

    // --- Steps inside an item (ADR 0013, ruling 6) ------------------------------

    private static RoadmapGatheredLink Task(
        string key,
        int? effort = 1,
        RoadmapProgress? progress = RoadmapProgress.Planned,
        params string[] after) =>
        new(key, key.ToUpperInvariant(), effort, RollupOrigin.Tag, progress, after);

    private static RoadmapBar Drawn(RoadmapItemDto item, RoadmapItemRollupDto rollup) =>
        Assert.Single(RoadmapPlanView.From(
            Plan([item]),
            Configured,
            new Dictionary<Guid, RoadmapItemRollupDto> { [item.Id] = rollup }).Bars);

    private static RoadmapGatheredLink Worked(
        string key,
        RoadmapProgress progress,
        DateOnly? started = null,
        DateOnly? completed = null,
        DateOnly? created = null) =>
        new(key, key.ToUpperInvariant(), 1, RollupOrigin.Tag, progress, StartedOn: started, CompletedOn: completed, CreatedOn: created);

    [Fact]
    public void AFinishedItem_IsDrawnFromItsFirstStartToItsLastCompletion_AndLocked()
    {
        // Planned 5–9 January; the work actually ran 12 December to 20 January.
        var bar = Drawn(Item("Plan", repositories: ["backlog"]), new RoadmapItemRollupDto(
        [
            Worked("a", RoadmapProgress.Done, started: new DateOnly(2025, 12, 12), completed: new DateOnly(2026, 1, 2)),
            Worked("b", RoadmapProgress.Done, started: new DateOnly(2026, 1, 3), completed: new DateOnly(2026, 1, 20))
        ], []));

        Assert.Equal(new DateOnly(2025, 12, 12), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 20), bar.End);
        Assert.True(bar.Locked);
    }

    [Fact]
    public void AFinishedItem_WhoseTasksPredateTheStartedStamp_StartsWhenTheFirstWasCreated()
    {
        var bar = Drawn(Item("Plan", repositories: ["backlog"]), new RoadmapItemRollupDto(
        [
            Worked("a", RoadmapProgress.Done, completed: new DateOnly(2026, 1, 7), created: new DateOnly(2026, 1, 1)),
            Worked("b", RoadmapProgress.Done, started: new DateOnly(2026, 1, 3), completed: new DateOnly(2026, 1, 8))
        ], []));

        Assert.Equal(new DateOnly(2026, 1, 1), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 8), bar.End);
    }

    [Fact]
    public void AFinishedItem_NobodyTicked_KeepsThePlannedEnd()
    {
        var bar = Drawn(Item("Plan", startDay: 5, endDay: 9, repositories: ["backlog"]), new RoadmapItemRollupDto(
            [Worked("a", RoadmapProgress.Done, started: new DateOnly(2026, 1, 2))], []));

        Assert.Equal(new DateOnly(2026, 1, 2), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 9), bar.End);
    }

    [Fact]
    public void AnItemWithWorkStillOpen_IsDrawnWhereItWasPlanned_AndCanBeMoved()
    {
        var bar = Drawn(Item("Plan", startDay: 5, endDay: 9, repositories: ["backlog"]), new RoadmapItemRollupDto(
        [
            Worked("a", RoadmapProgress.Done, started: new DateOnly(2025, 12, 1), completed: new DateOnly(2025, 12, 20)),
            Worked("b", RoadmapProgress.InProgress, started: new DateOnly(2026, 1, 2))
        ], []));

        Assert.Equal(new DateOnly(2026, 1, 5), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 9), bar.End);
        Assert.False(bar.Locked);
    }

    // --- Drawn from the work, forecast at the pace in use ------------------------

    // Saturday 10 January 2026, and a backlog pace of 7 points a week counted in a week
    // that works every day alike: a point a day, so the forecast's own rules read in
    // whole days. The working week's own rules are pinned below with the default week.
    private static readonly SharedKernel.WorkingHours EveryDayAlike = new()
    {
        Days = [.. SharedKernel.WorkingHours.Week.Select(day => new SharedKernel.WorkingDay(day, true, new TimeOnly(9, 0), new TimeOnly(17, 0)))]
    };

    private static readonly RoadmapForecast Forecast = new(
        new DateOnly(2026, 1, 10),
        new PacesInUseDto(14m, new Dictionary<string, decimal> { ["backlog"] = 7m }) { Week = EveryDayAlike });

    private static RoadmapBar Forecasted(RoadmapItemDto item, params RoadmapGatheredLink[] links) =>
        Forecasted(Forecast, item, links);

    private static RoadmapBar Forecasted(RoadmapForecast forecast, RoadmapItemDto item, params RoadmapGatheredLink[] links) =>
        Assert.Single(RoadmapPlanView.From(
            Plan([item]),
            Configured,
            new Dictionary<Guid, RoadmapItemRollupDto> { [item.Id] = new(links, []) },
            forecast: forecast).Bars);

    /// <summary>The same Saturday, counted in the default week.</summary>
    private static readonly RoadmapForecast OnTheDefaultWeek = Forecast with
    {
        Paces = Forecast.Paces with { Week = SharedKernel.WorkingHours.Default }
    };

    /// <summary>Local ADR 0019: work in flight on a Saturday is forecast from the
    /// Monday, counting the working hours — 3 points at 7 a week need 18.2 hours, so the
    /// Monday, the Tuesday and into the Wednesday. Drawn from the Tuesday its work
    /// began.</summary>
    [Fact]
    public void AnItemInFlight_IsForecastThroughTheWorkingWeek()
    {
        var bar = Forecasted(
            OnTheDefaultWeek,
            Item("Plan", startDay: 5, endDay: 31, repositories: ["backlog"]),
            Sized("a", 3, RoadmapProgress.InProgress, started: new DateOnly(2026, 1, 6)));

        Assert.Equal(new DateOnly(2026, 1, 6), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 14), bar.End);
    }

    /// <summary>Local ADR 0019 Verification 1, as drawn: an item nobody started, sized
    /// by its effort, laid out from today — a Saturday, so from the Monday — 10 points at
    /// 5 a week, ends on the next week's Friday.</summary>
    [Fact]
    public void AnEffortPlacedItemNobodyStarted_IsDrawnThroughTheWorkingWeek()
    {
        var forecast = OnTheDefaultWeek with
        {
            Paces = OnTheDefaultWeek.Paces with { ByRepository = new Dictionary<string, decimal> { ["backlog"] = 5m } }
        };

        var bar = Forecasted(
            forecast,
            Item("Plan", startDay: 5, endDay: 9, repositories: ["backlog"], placedBy: ImportPlacement.Effort),
            Sized("a", 10, RoadmapProgress.Planned));

        Assert.Equal(new DateOnly(2026, 1, 12), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 23), bar.End);
    }

    private static RoadmapGatheredLink Sized(
        string key,
        int? effort,
        RoadmapProgress progress,
        DateOnly? started = null,
        DateOnly? completed = null,
        DateOnly? created = null) =>
        new(key, key.ToUpperInvariant(), effort, RollupOrigin.Tag, progress, StartedOn: started, CompletedOn: completed, CreatedOn: created);

    [Fact]
    public void AnItemInFlight_StartsWhenItsWorkBegan_AndEndsWhenWhatIsLeftShouldBeDone_AndIsLocked()
    {
        // Planned 5–9 January. Work began on 20 December; 3 points and an unestimated
        // task are left, at a point a day from today, the 10th: 4 days, to the 13th.
        var bar = Forecasted(
            Item("Plan", startDay: 5, endDay: 9, repositories: ["backlog"]),
            Sized("a", 2, RoadmapProgress.Done, started: new DateOnly(2025, 12, 20), completed: new DateOnly(2025, 12, 28)),
            Sized("b", 3, RoadmapProgress.InProgress, started: new DateOnly(2026, 1, 2)),
            Sized("c", null, RoadmapProgress.Planned));

        Assert.Equal(new DateOnly(2025, 12, 20), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 13), bar.End);
        Assert.True(bar.Locked);
        Assert.Contains("in progress", bar.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AnItemInFlight_NearlyDone_IsNotStretchedToItsPlannedEnd()
    {
        // Planned to run to the 31st; only one point is left.
        var bar = Forecasted(
            Item("Plan", startDay: 5, endDay: 31, repositories: ["backlog"]),
            Sized("a", 8, RoadmapProgress.Done, started: new DateOnly(2026, 1, 5), completed: new DateOnly(2026, 1, 8)),
            Sized("b", 1, RoadmapProgress.Ready));

        Assert.Equal(new DateOnly(2026, 1, 5), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 10), bar.End);
    }

    [Fact]
    public void AnItemInFlight_IsDrawnFromWhenItsWorkBegan_NotFromAnEarlierPlannedStart()
    {
        // Planned from the 5th, and its task started on the 8th. A part is drawn from the
        // day its work began, and the 2 points left run from today, the 10th.
        var bar = Forecasted(
            Item("Plan", startDay: 5, endDay: 20, repositories: ["backlog"]),
            Sized("a", 2, RoadmapProgress.InProgress, started: new DateOnly(2026, 1, 8)));

        Assert.Equal(new DateOnly(2026, 1, 8), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 11), bar.End);
    }

    // --- A pinned end outranks the forecast for work in flight --------------------

    private static RoadmapGatheredLink[] InFlightWork() =>
    [
        Sized("a", 2, RoadmapProgress.Done, started: new DateOnly(2025, 12, 20), completed: new DateOnly(2025, 12, 28)),
        Sized("b", 3, RoadmapProgress.InProgress, started: new DateOnly(2026, 1, 2)),
        Sized("c", null, RoadmapProgress.Planned)
    ];

    [Fact]
    public void AnItemInFlight_IsLocked_ButItsEndCanBeResized()
    {
        var bar = Forecasted(Item("Plan", repositories: ["backlog"]), InFlightWork());

        Assert.True(bar.Locked);
        Assert.True(bar.EndResizable);
        Assert.DoesNotContain("Forecast:", bar.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AnItemInFlight_WithAPinnedEnd_IsDrawnToThePinnedEnd_AndStartsWhenTheWorkBegan()
    {
        // The forecast would end on the 13th; the person pinned the 31st.
        var bar = Forecasted(
            Item("Plan", startDay: 5, endDay: 31, repositories: ["backlog"]) with { EndPinned = true },
            InFlightWork());

        Assert.Equal(new DateOnly(2025, 12, 20), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 31), bar.End);
        Assert.True(bar.Locked);
        Assert.True(bar.EndResizable);
    }

    [Fact]
    public void AnItemInFlight_WithAPinnedEnd_KeepsTheForecastInItsDetail()
    {
        var bar = Forecasted(
            Item("Plan", startDay: 5, endDay: 31, repositories: ["backlog"]) with { EndPinned = true },
            InFlightWork());

        var lines = bar.Detail!.Split('\n');
        Assert.Contains("Forecast: 13 Jan 2026 at 7 pt/wk", lines);
        Assert.Contains(lines, line => line.Contains("end you pinned", StringComparison.Ordinal));
    }

    [Fact]
    public void AnItemInFlight_WhosePinnedEndHasPassed_IsDrawnFromTodayAtLeast()
    {
        // Pinned to the 7th, but today is the 10th and work is still open: the bar
        // cannot end before the first day open work can be drawn on.
        var bar = Forecasted(
            Item("Plan", startDay: 5, endDay: 7, repositories: ["backlog"]) with { EndPinned = true },
            InFlightWork());

        Assert.Equal(new DateOnly(2026, 1, 10), bar.End);
    }

    [Fact]
    public void AnItemInFlight_PinnedBeforeItsPlannedStart_IsDrawnFromWhenTheWorkBegan_ToTheLaterOfThePinAndToday()
    {
        // The plan said the 5th, the work began on 20 December, and the person pinned
        // the 1st: the store moved the window back to the pin (1st to 1st), and the bar
        // is still drawn from when the work began — never ending before open work can be.
        var bar = Forecasted(
            Item("Plan", startDay: 1, endDay: 1, repositories: ["backlog"]) with { EndPinned = true },
            InFlightWork());

        Assert.Equal(new DateOnly(2025, 12, 20), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 10), bar.End);
        Assert.True(bar.EndResizable);
    }

    [Fact]
    public void AFinishedItem_IgnoresAPinnedEnd_AndCannotBeResized()
    {
        var bar = Forecasted(
            Item("Plan", startDay: 5, endDay: 31, repositories: ["backlog"]) with { EndPinned = true },
            Sized("a", 2, RoadmapProgress.Done, started: new DateOnly(2026, 1, 2), completed: new DateOnly(2026, 1, 8)));

        Assert.Equal(new DateOnly(2026, 1, 8), bar.End);
        Assert.True(bar.Locked);
        Assert.False(bar.EndResizable);
    }

    [Fact]
    public void AnItemNobodyStarted_IsDrawnAsStored_EvenWithAPinnedEnd_AndIsNotEndOnly()
    {
        var bar = Forecasted(
            Item("Plan", startDay: 5, endDay: 20, repositories: ["backlog"]) with { EndPinned = true },
            Sized("a", 3, RoadmapProgress.Ready));

        Assert.Equal(new DateOnly(2026, 1, 20), bar.End);
        Assert.False(bar.Locked);
        Assert.False(bar.EndResizable);
    }

    [Fact]
    public void AnItemNobodyStarted_IsDrawnWherePlanned_AndCanBeMoved()
    {
        var bar = Forecasted(
            Item("Plan", startDay: 5, endDay: 9, repositories: ["backlog"]),
            Sized("a", 13, RoadmapProgress.Ready),
            Sized("b", 5, RoadmapProgress.Planned));

        Assert.Equal(new DateOnly(2026, 1, 5), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 9), bar.End);
        Assert.False(bar.Locked);
    }

    // --- Sized by effort, read at the pace in use (local ADR 0018) --------------

    [Fact]
    public void AnEffortPlacedItemNobodyStarted_IsDrawnAtThePaceInUse_FromToday_AndCanBeMoved()
    {
        // Stored 5–9 January at whatever pace was in use then, and nobody started it by
        // today, the 10th. 18 points at backlog's 7 a week is 18 days: the 10th to the
        // 27th — laid out again from today, as the keep-up projection stores it.
        var bar = Forecasted(
            Item("Plan", startDay: 5, endDay: 9, repositories: ["backlog"], placedBy: ImportPlacement.Effort),
            Sized("a", 13, RoadmapProgress.Ready),
            Sized("b", 5, RoadmapProgress.Planned));

        Assert.Equal(new DateOnly(2026, 1, 10), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 27), bar.End);
        Assert.False(bar.Locked);
    }

    [Fact]
    public void AnEffortPlacedItemNobodyStarted_InARepositoryWithNoPaceOfItsOwn_IsDrawnAtTheGlobalPace()
    {
        // Fincent has no pace in the forecast, so 14 points go at the global 14 a week:
        // a week from today, the 10th to the 16th.
        var bar = Forecasted(
            Item("Plan", startDay: 5, endDay: 31, repositories: ["fincent"], placedBy: ImportPlacement.Effort),
            Sized("a", 14, RoadmapProgress.Planned));

        Assert.Equal(new DateOnly(2026, 1, 10), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 16), bar.End);
    }

    [Fact]
    public void AnEffortPlacedItemNobodyStarted_WithNoRepository_IsDrawnAtTheGlobalPace()
    {
        // 14 points at the global 14 a week: a week from today, the 10th to the 16th.
        var bar = Forecasted(
            Item("Plan", startDay: 5, endDay: 31, placedBy: ImportPlacement.Effort),
            Sized("a", 14, RoadmapProgress.Planned));

        Assert.Equal(new DateOnly(2026, 1, 10), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 16), bar.End);
    }

    /// <summary>One rule for the drawing and the store: the bar is the window the keep-up
    /// projection would store today.</summary>
    [Fact]
    public void AnEffortPlacedItemNobodyStarted_IsDrawnAsTheKeepUpProjectionWouldStoreIt()
    {
        var item = Item("Plan", startDay: 5, endDay: 9, repositories: ["backlog"], placedBy: ImportPlacement.Effort);
        RoadmapGatheredLink[] work = [Sized("a", 3, RoadmapProgress.Ready), Sized("b", null, RoadmapProgress.Ready)];

        var bar = Forecasted(item, work);
        var stored = Assert.Single(RoadmapProjection.Project(
            [item],
            new Dictionary<Guid, RoadmapItemRollupDto> { [item.Id] = new(work, []) },
            Forecast.Paces,
            Forecast.Today));

        Assert.Equal((stored.Start, stored.End), (bar.Start, bar.End));
        Assert.Equal(EffortWindow.EndFrom(Forecast.Today, 3, 7m, Forecast.Paces.Week), bar.End);
    }

    [Fact]
    public void AnEffortPlacedItem_WithoutAForecast_IsDrawnAsStored()
    {
        var bar = Drawn(
            Item("Plan", startDay: 5, endDay: 9, repositories: ["backlog"], placedBy: ImportPlacement.Effort),
            new RoadmapItemRollupDto([Sized("a", 30, RoadmapProgress.Ready)], []));

        Assert.Equal(new DateOnly(2026, 1, 9), bar.End);
    }

    [Fact]
    public void AnEffortPlacedItemThatGathersNothing_IsDrawnAsStored()
    {
        var bar = Forecasted(Item("Plan", startDay: 5, endDay: 9, repositories: ["backlog"], placedBy: ImportPlacement.Effort));

        Assert.Equal(new DateOnly(2026, 1, 5), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 9), bar.End);
    }

    [Fact]
    public void ADueDatedItemNobodyStarted_KeepsTheEndThePersonWrote()
    {
        var bar = Forecasted(
            Item("Plan", startDay: 5, endDay: 9, repositories: ["backlog"], placedBy: ImportPlacement.DueDate),
            Sized("a", 30, RoadmapProgress.Ready));

        Assert.Equal(new DateOnly(2026, 1, 9), bar.End);
    }

    [Fact]
    public void AnEffortPlacedItemInFlight_IsStillDrawnFromItsWork()
    {
        // The same reading as a hand-placed item in flight: from when work began to
        // what is left at a point a day from today, the 10th — 3 points, to the 12th.
        var bar = Forecasted(
            Item("Plan", startDay: 5, endDay: 9, repositories: ["backlog"], placedBy: ImportPlacement.Effort),
            Sized("a", 2, RoadmapProgress.InProgress, started: new DateOnly(2026, 1, 3)),
            Sized("b", 1, RoadmapProgress.Ready));

        Assert.Equal(new DateOnly(2026, 1, 3), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 12), bar.End);
        Assert.True(bar.Locked);
    }

    [Fact]
    public void AFinishedItem_NobodyTicked_NeverEndsAfterToday()
    {
        var bar = Forecasted(
            Item("Plan", startDay: 5, endDay: 31, repositories: ["backlog"]),
            Sized("a", 2, RoadmapProgress.Done, started: new DateOnly(2026, 1, 2)));

        Assert.Equal(new DateOnly(2026, 1, 2), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 10), bar.End);
        Assert.Contains("finished", bar.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AnItemInFlight_InARepositoryWithNoPaceOfItsOwn_IsForecastAtTheGlobalPace()
    {
        // Fincent has no pace in the forecast. Two points a day globally: 4 points left
        // is 2 days, the 10th and 11th — from the 6th, when its work began.
        var bar = Forecasted(
            Item("Plan", startDay: 5, endDay: 31, repositories: ["fincent"]),
            Sized("a", 1, RoadmapProgress.Done, started: new DateOnly(2026, 1, 6), completed: new DateOnly(2026, 1, 7)),
            Sized("b", 4, RoadmapProgress.InProgress, started: new DateOnly(2026, 1, 8)));

        Assert.Equal(new DateOnly(2026, 1, 6), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 11), bar.End);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("retired")]
    public void AnItemInFlight_UnderNoConfiguredRepository_IsForecastAtTheGlobalPace(string? filedAs)
    {
        // Filed under none, or under a name no repository answers to, it is drawn under
        // no repository at the default pace. Two points a day globally: 4 points left is
        // 2 days, the 10th and 11th.
        var bar = Forecasted(
            Item("Plan", startDay: 5, endDay: 31, repositories: filedAs is null ? [] : [filedAs]),
            Sized("b", 4, RoadmapProgress.InProgress, started: new DateOnly(2026, 1, 8)));

        Assert.StartsWith($"{RoadmapPlanView.NoRepositoryGroupId}::", bar.RowId, StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2026, 1, 11), bar.End);
    }

    [Theory]
    [InlineData("JSdotNet/Backlog")]
    [InlineData("jsdotnet/backlog")]
    public void AnItemInFlight_FiledByItsRepositorysFullName_IsForecastAtThatRepositorysPace(string filedAs)
    {
        // A point a day in Backlog, against two globally: 4 points left is the 10th to
        // the 13th. Filed by full name it is still Backlog's work, at Backlog's pace.
        var bar = Forecasted(
            Item("Plan", startDay: 5, endDay: 31, repositories: [filedAs]),
            Sized("b", 4, RoadmapProgress.InProgress, started: new DateOnly(2026, 1, 8)));

        Assert.Equal(new DateOnly(2026, 1, 13), bar.End);
    }

    [Fact]
    public void AnItemsStepsAreItsGatheredTasksInDependencyOrder()
    {
        // Handed in backwards: c waits on b, b waits on a. Drawn a, b, c, with the
        // free-standing d keeping its place among the ties.
        var bar = Drawn(Item("Plan", repositories: ["backlog"]), new RoadmapItemRollupDto(
            [Task("c", after: "b"), Task("d"), Task("b", after: "a"), Task("a")],
            []));

        Assert.Equal(["d", "a", "b", "c"], bar.StepList.Select(step => step.Id));
    }

    [Fact]
    public void AStepSaysWhatItWaitsForByTitle()
    {
        var bar = Drawn(Item("Plan", repositories: ["backlog"]), new RoadmapItemRollupDto([Task("a"), Task("b", after: "a")], []));

        Assert.Equal("After A", bar.StepList[1].Detail);
        Assert.Null(bar.StepList[0].Detail);
    }

    [Fact]
    public void KnowledgeChaptersAreNotStepsAndDoNotMoveTheFill()
    {
        var bar = Drawn(Item("Plan", repositories: ["backlog"]), new RoadmapItemRollupDto(
            [Task("a", effort: 2, progress: RoadmapProgress.Done), Task("b", effort: 2)],
            [new RoadmapGatheredLink("chapter.md#x", "Chapter", null, RollupOrigin.Tag)]));

        Assert.Equal(["a", "b"], bar.StepList.Select(step => step.Id));
        Assert.Equal(0, bar.UnestimatedCount);
        Assert.Equal(0.5, bar.DoneShare);
    }

    [Theory]
    [InlineData(RoadmapProgress.Planned, RoadmapStepTone.Draft)]
    [InlineData(RoadmapProgress.Ready, RoadmapStepTone.Ready)]
    [InlineData(RoadmapProgress.InProgress, RoadmapStepTone.InProgress)]
    [InlineData(RoadmapProgress.Done, RoadmapStepTone.Done)]
    [InlineData(null, RoadmapStepTone.Unknown)]
    public void ProgressIsColouredWithTheStatusBadgesOwnTones(RoadmapProgress? progress, RoadmapStepTone tone) =>
        Assert.Equal(tone, RoadmapPlanView.Tone(progress));

    [Fact]
    public void AnItemWithNoRollupDrawsWithNoSteps()
    {
        var view = RoadmapPlanView.From(Plan([Item("Plan", repositories: ["backlog"])]), Configured);

        Assert.False(Assert.Single(view.Bars).HasSteps);
    }

    // --- Work nothing carries out yet -------------------------------------------

    [Fact]
    public void AnItemNoTaskCarriesOutIsDrawnAsIntentAndSaysSo()
    {
        var bar = Drawn(Item("Plan", repositories: ["backlog"]), RoadmapItemRollupDto.Empty);

        Assert.True(bar.Tentative);
        Assert.Contains("no task yet", bar.Detail);
    }

    [Fact]
    public void AnItemWithATaskIsDrawnAsWork()
    {
        var bar = Drawn(Item("Plan", repositories: ["backlog"]), new RoadmapItemRollupDto([Task("a")], []));

        Assert.False(bar.Tentative);
        Assert.DoesNotContain("no task yet", bar.Detail);
    }

    [Fact]
    public void AnItemThatGatheredOnlyKnowledgeStillHasNoTask()
    {
        var bar = Drawn(Item("Plan", repositories: ["backlog"]), new RoadmapItemRollupDto(
            [], [new RoadmapGatheredLink("chapter.md#x", "Chapter", null, RollupOrigin.Tag)]));

        Assert.True(bar.Tentative);
    }

    [Fact]
    public void WithoutTheGatheredWorkNothingIsClaimedAboutTasks()
    {
        var view = RoadmapPlanView.From(Plan([Item("Plan", repositories: ["backlog"])]), Configured);

        Assert.False(Assert.Single(view.Bars).Tentative);
    }
}
