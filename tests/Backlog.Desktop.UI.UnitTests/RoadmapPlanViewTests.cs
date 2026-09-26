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
        string[]? knowledge = null) =>
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
            knowledge);

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

        Assert.Equal(["backlog", "fincent"], view.Groups.Select(group => group.Id));
        // Labelled by alias, not full name: the label is written down the side of the
        // band, so its length is a floor on how short the band can be. The full name
        // is still what the Repository filter offers.
        Assert.Equal(["backlog", "fincent"], view.Groups.Select(group => group.Title));
    }

    [Fact]
    public void ABandWithNothingInItIsNotDrawn()
    {
        var view = RoadmapPlanView.From(Plan([Item("Only in Backlog", repositories: ["backlog"])]), Configured);

        Assert.Single(view.Groups);
        Assert.Equal("backlog", view.Groups[0].Id);
    }

    [Fact]
    public void WorkWithNoRepositoryLandsInTheUnfiledBand_Last()
    {
        var view = RoadmapPlanView.From(
            Plan([Item("Filed", repositories: ["backlog"]), Item("Not filed")]),
            Configured);

        Assert.Equal(["backlog", RoadmapPlanView.UnfiledGroupId], view.Groups.Select(group => group.Id));
    }

    [Fact]
    public void AnAliasThatIsNoLongerConfigured_ReadsAsUnfiledRatherThanMakingItsOwnBand()
    {
        var view = RoadmapPlanView.From(Plan([Item("Old work", repositories: ["retired"])]), Configured);

        var band = Assert.Single(view.Groups);
        Assert.Equal(RoadmapPlanView.UnfiledGroupId, band.Id);

        // The alias itself is not lost: it is still offered as a filter, so
        // configuring that repository again puts the work back where it was.
        var bar = Assert.Single(view.Bars);
        Assert.Contains(new RoadmapFacet("Repository", "retired"), bar.FacetList);
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

        var band = Assert.Single(view.Groups);
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

        var rows = Assert.Single(view.Groups).RowList;
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

        var row = Assert.Single(Assert.Single(view.Groups).RowList);
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

        Assert.Equal(2, Assert.Single(view.Groups).RowList.Count);
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

        var rows = Assert.Single(view.Groups).RowList;
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

        var band = Assert.Single(view.Groups);
        Assert.Equal(RoadmapPlanView.MilestoneGroupId, band.Id);
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
        Assert.Null(Assert.Single(view.Groups).Color);
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
            view.Groups.Select(band => band.Color));
    }

    [Fact]
    public void TheUnfiledBandTakesNoColour_BecauseItIsNotAProject()
    {
        var view = RoadmapPlanView.From(
            Plan([Item("Filed", repositories: ["backlog"]), Item("Not filed")]),
            Configured);

        var unfiled = view.Groups.Single(band => band.Id == RoadmapPlanView.UnfiledGroupId);

        // A neutral band reads as "nobody said" rather than as one more project.
        Assert.Null(unfiled.Color);
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

        Assert.Equal("var(--color-band-2)", Assert.Single(view.Groups).Color);
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
            view.Groups.Select(band => band.Color));
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
    public void WithNoRepositoriesConfiguredAtAll_TheWholePlanReadsAsUnfiled()
    {
        var view = RoadmapPlanView.From(Plan([Item("Work", repositories: ["backlog"])]), []);

        var band = Assert.Single(view.Groups);
        Assert.Equal(RoadmapPlanView.UnfiledGroupId, band.Id);
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
        var bar = Drawn(Item("Plan"), new RoadmapItemRollupDto(
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
        var bar = Drawn(Item("Plan"), new RoadmapItemRollupDto(
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
        var bar = Drawn(Item("Plan", startDay: 5, endDay: 9), new RoadmapItemRollupDto(
            [Worked("a", RoadmapProgress.Done, started: new DateOnly(2026, 1, 2))], []));

        Assert.Equal(new DateOnly(2026, 1, 2), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 9), bar.End);
    }

    [Fact]
    public void AnItemWithWorkStillOpen_IsDrawnWhereItWasPlanned_AndCanBeMoved()
    {
        var bar = Drawn(Item("Plan", startDay: 5, endDay: 9), new RoadmapItemRollupDto(
        [
            Worked("a", RoadmapProgress.Done, started: new DateOnly(2025, 12, 1), completed: new DateOnly(2025, 12, 20)),
            Worked("b", RoadmapProgress.InProgress, started: new DateOnly(2026, 1, 2))
        ], []));

        Assert.Equal(new DateOnly(2026, 1, 5), bar.Start);
        Assert.Equal(new DateOnly(2026, 1, 9), bar.End);
        Assert.False(bar.Locked);
    }

    [Fact]
    public void AnItemsStepsAreItsGatheredTasksInDependencyOrder()
    {
        // Handed in backwards: c waits on b, b waits on a. Drawn a, b, c, with the
        // free-standing d keeping its place among the ties.
        var bar = Drawn(Item("Plan"), new RoadmapItemRollupDto(
            [Task("c", after: "b"), Task("d"), Task("b", after: "a"), Task("a")],
            []));

        Assert.Equal(["d", "a", "b", "c"], bar.StepList.Select(step => step.Id));
    }

    [Fact]
    public void AStepSaysWhatItWaitsForByTitle()
    {
        var bar = Drawn(Item("Plan"), new RoadmapItemRollupDto([Task("a"), Task("b", after: "a")], []));

        Assert.Equal("After A", bar.StepList[1].Detail);
        Assert.Null(bar.StepList[0].Detail);
    }

    [Fact]
    public void KnowledgeChaptersAreNotStepsAndDoNotMoveTheFill()
    {
        var bar = Drawn(Item("Plan"), new RoadmapItemRollupDto(
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
        var view = RoadmapPlanView.From(Plan([Item("Plan")]), Configured);

        Assert.False(Assert.Single(view.Bars).HasSteps);
    }
}
