using Backlog.UI.Components.Roadmap;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// An item whose tasks hand over between repositories is drawn as that sequence — a
/// segment per repository per phase — rather than one bar per repository all running
/// the same window. What decides a phase, how the window is shared out, and which
/// arrows the hand-overs draw.
/// </summary>
public class RoadmapPlanViewHandOverTests
{
    private static readonly List<PlannedRepository> Configured =
    [
        new("backlog", "JSdotNet/Backlog", 1),
        new("fincent", "JSdotNet/Fincent", 2)
    ];

    private static RoadmapItemDto Item(Guid? id = null, Guid[]? dependsOn = null, int startDay = 1, int endDay = 30) =>
        new(
            id ?? Guid.NewGuid(),
            "Spans both",
            new DateOnly(2026, 1, startDay),
            new DateOnly(2026, 1, endDay),
            PlanningPriority.Medium,
            ["backlog", "fincent"],
            null,
            null,
            dependsOn ?? [],
            null,
            "",
            null);

    private static RoadmapGatheredLink Task(string key, int effort, string repository, params string[] waits) =>
        new(key, key.ToUpperInvariant(), effort, RollupOrigin.Tag, RoadmapProgress.Planned, waits, [repository]);

    private static RoadmapTimelineModel Draw(RoadmapItemDto item, params RoadmapGatheredLink[] tasks) =>
        Draw([item], new Dictionary<Guid, RoadmapItemRollupDto> { [item.Id] = new(tasks, []) });

    private static RoadmapTimelineModel Draw(
        IEnumerable<RoadmapItemDto> items,
        IReadOnlyDictionary<Guid, RoadmapItemRollupDto> rollups) =>
        RoadmapPlanView.From(new RoadmapPlanDto([.. items], [], [], null), Configured, rollups);

    [Fact]
    public void TasksHandingOverBetweenRepositories_AreDrawnAsConsecutiveSegments()
    {
        var item = Item();

        // backlog, then fincent waiting on it, then backlog again waiting on fincent.
        var view = Draw(
            item,
            Task("a", 3, "JSdotNet/Backlog"),
            Task("b", 5, "JSdotNet/Fincent", "a"),
            Task("c", 2, "JSdotNet/Backlog", "b"));

        var bars = view.Bars.OrderBy(bar => bar.Start).ToList();
        Assert.Equal(
            [$"{item.Id}@backlog#1", $"{item.Id}@fincent#2", $"{item.Id}@backlog#3"],
            bars.Select(bar => bar.Id));

        // Every segment is the one stored item, so opening or moving any opens or moves it.
        Assert.All(bars, bar => Assert.Equal(item.Id, RoadmapPlanView.NodeIdOf(bar.Id)));

        // The window shared out by effort, 3 : 5 : 2 of 30 days, end to end.
        Assert.Equal(
            [(1, 9), (10, 24), (25, 30)],
            bars.Select(bar => (bar.Start.Day, bar.End.Day)));

        Assert.Equal(["a"], bars[0].StepList.Select(step => step.Id));
        Assert.Equal(["b"], bars[1].StepList.Select(step => step.Id));
        Assert.Equal(["c"], bars[2].StepList.Select(step => step.Id));

        // Both backlog segments share a row: they follow on, they do not overlap.
        Assert.Equal(bars[0].RowId, bars[2].RowId);
        Assert.Contains("handed over between repositories", bars[1].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheWaitsThatCrossARepository_BecomeArrows()
    {
        var item = Item();

        var view = Draw(
            item,
            Task("a", 2, "JSdotNet/Backlog"),
            // Waits inside its own repository: same phase, no arrow.
            Task("a2", 2, "JSdotNet/Backlog", "a"),
            Task("b", 2, "JSdotNet/Fincent", "a2"));

        Assert.Equal(2, view.Bars.Count);
        var link = Assert.Single(view.Links);
        Assert.Equal(new RoadmapLink($"{item.Id}@backlog#1", $"{item.Id}@fincent#2"), link);
    }

    [Fact]
    public void RepositoriesThatNeverHandOver_KeepOnePartEach_OverTheWholeWindow()
    {
        var item = Item();

        var view = Draw(
            item,
            Task("a", 2, "JSdotNet/Backlog"),
            Task("a2", 2, "JSdotNet/Backlog", "a"),
            Task("b", 2, "JSdotNet/Fincent"));

        Assert.Equal(
            [$"{item.Id}@backlog", $"{item.Id}@fincent"],
            view.Bars.Select(bar => bar.Id).Order(StringComparer.Ordinal));
        Assert.All(view.Bars, bar => Assert.Equal((item.Start, item.End), (bar.Start, bar.End)));
        Assert.Empty(view.Links);
    }

    [Fact]
    public void APlanDependencyOnASequencedItem_IsOneArrow_FromItsLastSegment()
    {
        var first = Item();
        var then = Item(dependsOn: [first.Id], startDay: 31, endDay: 31);

        var view = Draw(
            [first, then],
            new Dictionary<Guid, RoadmapItemRollupDto>
            {
                [first.Id] = new([Task("a", 1, "JSdotNet/Backlog"), Task("b", 1, "JSdotNet/Fincent", "a")], [])
            });

        // The hand-over inside first, and one arrow from where first finishes to then —
        // not one into every part of it.
        Assert.Contains(new RoadmapLink($"{first.Id}@backlog#1", $"{first.Id}@fincent#2"), view.Links);
        var planArrow = Assert.Single(view.Links, link => RoadmapPlanView.NodeIdOf(link.ToId) == then.Id);
        Assert.Equal($"{first.Id}@fincent#2", planArrow.FromId);
    }

    [Fact]
    public void DraggingOneSegment_MovesTheWholeItem_AndPullingItsEnd_LengthensIt()
    {
        var item = Item();
        var view = Draw(
            item,
            Task("a", 1, "JSdotNet/Backlog"),
            Task("b", 1, "JSdotNet/Fincent", "a"));
        var second = view.Bars.Single(bar => bar.Id.EndsWith("#2", StringComparison.Ordinal));

        var moved = RoadmapPlanView.ItemWindowFor(item, second,
            new RoadmapChange(second.Id, second.RowId, second.Start.AddDays(7), second.End.AddDays(7), RoadmapDrag.Move));
        Assert.Equal((new DateOnly(2026, 1, 8), new DateOnly(2026, 2, 6)), moved);

        var longer = RoadmapPlanView.ItemWindowFor(item, second,
            new RoadmapChange(second.Id, second.RowId, second.Start, second.End.AddDays(7), RoadmapDrag.ResizeEnd));
        Assert.Equal((item.Start, new DateOnly(2026, 2, 6)), longer);
    }
}
