using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.DomainModels;

namespace Backlog.Modules.Roadmap.UnitTests;

public class PlannedWindowTests
{
    [Fact]
    public void BothEndsAreInclusive_SoASingleDayIsOneDayLong()
    {
        var window = PlannedWindow.Create(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 5)).Value;

        Assert.Equal(1, window.Days);
        Assert.True(window.Contains(new DateOnly(2026, 1, 5)));
    }

    [Fact]
    public void ThroughTheLastDayMeansTheLastDay()
    {
        var window = PlannedWindow.Create(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)).Value;

        Assert.Equal(31, window.Days);
        Assert.True(window.Contains(new DateOnly(2026, 1, 31)));
        Assert.False(window.Contains(new DateOnly(2026, 2, 1)));
    }

    [Fact]
    public void AWindowThatEndsBeforeItStarts_IsRefusedRatherThanThrown()
    {
        var created = PlannedWindow.Create(new DateOnly(2026, 1, 9), new DateOnly(2026, 1, 5));

        Assert.True(created.IsFailure);
        Assert.Equal("roadmap.invalid_window", created.Error.Code);
    }

    [Fact]
    public void EqualityIsByValue()
    {
        var first = PlannedWindow.Create(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9)).Value;
        var second = PlannedWindow.Create(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9)).Value;

        Assert.Equal(first, second);
    }
}

public class RepositoryScopeTests
{
    [Fact]
    public void AnEmptyScopeIsUnfiled_NotAnError()
    {
        Assert.True(RepositoryScope.Of(null).IsUnfiled);
        Assert.True(RepositoryScope.Of([]).IsUnfiled);
        Assert.True(RepositoryScope.Of(["", "   "]).IsUnfiled);
    }

    [Fact]
    public void AliasesAreNormalizedTheWayTheSettingsStoreWritesThem()
    {
        var scope = RepositoryScope.Of([" Backlog ", "FINCENT"]);

        Assert.Equal(["backlog", "fincent"], scope.Aliases);
        Assert.True(scope.Includes("BACKLOG"));
    }

    [Fact]
    public void DuplicatesCollapse_AndTypingOrderDoesNotAffectEquality()
    {
        var first = RepositoryScope.Of(["backlog", "fincent", "backlog"]);
        var second = RepositoryScope.Of(["fincent", "backlog"]);

        Assert.Equal(2, first.Aliases.Count);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }
}

public class PlanningLaneTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankIsTheDefaultLane(string? name)
    {
        var lane = PlanningLane.Of(name);

        Assert.True(lane.IsDefault);
        Assert.Equal(PlanningLane.Default, lane);
    }

    [Fact]
    public void ALaneIsWhateverThePersonCalledIt()
    {
        var lane = PlanningLane.Of("  migration  ");

        Assert.Equal("migration", lane.Name);
        Assert.False(lane.IsDefault);
    }
}

public class RehydrationTests
{
    private static RoadmapItem Item(Guid id, string title, IEnumerable<Guid>? dependsOn = null) =>
        new(
            id,
            title,
            PlannedWindow.Of(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9)),
            PlanningPriority.Medium,
            RepositoryScope.Unfiled,
            PlanningLane.Default,
            Dependencies.Of(dependsOn),
            null,
            null);

    [Fact]
    public void ADanglingDependencyIsDroppedRatherThanFailingTheLoad()
    {
        var known = Guid.NewGuid();
        var missing = Guid.NewGuid();

        var plan = RoadmapPlan.Rehydrate(
            [Item(known, "Known", [missing])],
            []);

        Assert.Single(plan.Items);
        Assert.Empty(plan.Items[0].Dependencies.All);
    }

    [Fact]
    public void TwoBlocksSharingAnId_CollapseToTheFirst()
    {
        var id = Guid.NewGuid();

        var plan = RoadmapPlan.Rehydrate(
            [Item(id, "First copy"), Item(id, "Second copy")],
            []);

        Assert.Single(plan.Items);
        Assert.Equal("First copy", plan.Items[0].Title);
    }

    [Fact]
    public void AnAliasThatNoLongerResolves_IsKeptRatherThanDeleted()
    {
        // Resolution happens on the read path; the plan holds what the person wrote.
        // Nothing here knows or cares whether "retired-repo" is still configured.
        var item = new RoadmapItem(
            Guid.NewGuid(),
            "Old work",
            PlannedWindow.Of(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9)),
            PlanningPriority.Medium,
            RepositoryScope.Of(["retired-repo"]),
            PlanningLane.Default,
            Dependencies.None(),
            null,
            null);

        var plan = RoadmapPlan.Rehydrate([item], []);

        Assert.Equal(["retired-repo"], plan.Items[0].Scope.Aliases);
    }

    [Fact]
    public void ACycleAlreadyInAHandEditedFile_DoesNotHangTheLoad()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var plan = RoadmapPlan.Rehydrate(
            [Item(first, "First", [second]), Item(second, "Second", [first])],
            []);

        // Both edges survive — the plan reports what the file said rather than
        // silently repairing it — and asking the graph questions terminates.
        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(2, plan.Contradictions().Count);
    }
}
