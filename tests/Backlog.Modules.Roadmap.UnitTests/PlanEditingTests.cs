using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.DomainModels;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// Editing an item that already exists. The interesting cases are the ones where a
/// form submitting every field could quietly undo something.
/// </summary>
public class PlanEditingTests
{
    private static PlannedWindow Window(int startDay, int endDay) =>
        PlannedWindow.Create(new DateOnly(2026, 1, startDay), new DateOnly(2026, 1, endDay)).Value;

    private static (RoadmapPlan Plan, RoadmapItem Item) Planned()
    {
        var plan = RoadmapPlan.Empty();
        var item = plan.AddItem(
            "Original title",
            Window(5, 9),
            PlanningPriority.Low,
            RepositoryScope.Of(["backlog"]),
            PlanningLane.Of("platform"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Original notes").Value;

        return (plan, item);
    }

    [Fact]
    public void EveryFieldIsWrittenBack()
    {
        var (plan, item) = Planned();
        var entry = Guid.NewGuid();

        var updated = plan.UpdateItem(
            item.Id,
            "  Edited title  ",
            Window(12, 23),
            PlanningPriority.Critical,
            RepositoryScope.Of(["fincent", "backlog"]),
            PlanningLane.Of("migration"),
            entry,
            "Edited notes");

        Assert.True(updated.IsSuccess);
        Assert.Equal("Edited title", item.Title);
        Assert.Equal(new DateOnly(2026, 1, 12), item.Window.Start);
        Assert.Equal(new DateOnly(2026, 1, 23), item.Window.End);
        Assert.Equal(PlanningPriority.Critical, item.Priority);
        Assert.Equal(["backlog", "fincent"], item.Scope.Aliases);
        Assert.Equal("migration", item.Lane.Name);
        Assert.Equal(entry, item.BacklogEntryId);
        Assert.Equal("Edited notes", item.Notes);
    }

    [Fact]
    public void FieldsCanBeCleared_BecauseEverySubmissionIsComplete()
    {
        var (plan, item) = Planned();

        plan.UpdateItem(item.Id, "Edited", Window(5, 9), PlanningPriority.Low);

        // Nothing was passed for scope, lane, link or notes, and that means "empty"
        // rather than "leave it": a form that submits every field cannot say the
        // second, and the plan must not guess it.
        Assert.True(item.Scope.IsUnfiled);
        Assert.True(item.Lane.IsDefault);
        Assert.Null(item.BacklogEntryId);
        Assert.Null(item.Notes);
    }

    [Fact]
    public void TheIdSurvivesAnEdit_SoDependenciesOnItDoToo()
    {
        var plan = RoadmapPlan.Empty();
        var design = plan.AddItem("Design", Window(5, 9)).Value;
        var build = plan.AddItem("Build", Window(12, 16)).Value;
        plan.AddDependency(build.Id, design.Id);

        plan.UpdateItem(design.Id, "Design, renamed", Window(2, 6), PlanningPriority.High);

        Assert.Equal([design.Id], build.Dependencies.All);
        Assert.Equal("Design, renamed", plan.Items.Single(item => item.Id == design.Id).Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEditWithoutATitleIsRefused_AndChangesNothing(string title)
    {
        var (plan, item) = Planned();

        var updated = plan.UpdateItem(item.Id, title, Window(12, 23), PlanningPriority.Critical);

        Assert.True(updated.IsFailure);
        Assert.Equal("roadmap.title_required", updated.Error.Code);
        Assert.Equal("Original title", item.Title);
        Assert.Equal(new DateOnly(2026, 1, 5), item.Window.Start);
        Assert.Equal(PlanningPriority.Low, item.Priority);
    }

    [Fact]
    public void EditingSomethingThatIsNotThereIsRefused()
    {
        var (plan, _) = Planned();

        var updated = plan.UpdateItem(Guid.NewGuid(), "Anything", Window(5, 9), PlanningPriority.Low);

        Assert.True(updated.IsFailure);
        Assert.Equal("roadmap.item_not_found", updated.Error.Code);
    }

    [Fact]
    public void EditingDatesIntoAContradiction_IsAllowedAndReported()
    {
        var plan = RoadmapPlan.Empty();
        var design = plan.AddItem("Design", Window(5, 9)).Value;
        var build = plan.AddItem("Build", Window(12, 16)).Value;
        plan.AddDependency(build.Id, design.Id);

        // Pulling the predecessor's end past the successor's start. The plan takes it
        // and says so, the same as a drag would: discovering the date does not fit is
        // the point.
        var updated = plan.UpdateItem(design.Id, "Design", Window(5, 20), PlanningPriority.Low);

        Assert.True(updated.IsSuccess);
        Assert.Single(plan.Contradictions());
    }

    [Fact]
    public void EditingDoesNotTouchDependencies()
    {
        var plan = RoadmapPlan.Empty();
        var design = plan.AddItem("Design", Window(5, 9)).Value;
        var build = plan.AddItem("Build", Window(12, 16)).Value;
        plan.AddDependency(build.Id, design.Id);

        plan.UpdateItem(build.Id, "Build, edited", Window(19, 23), PlanningPriority.High);

        Assert.Equal([design.Id], build.Dependencies.All);
    }
}
