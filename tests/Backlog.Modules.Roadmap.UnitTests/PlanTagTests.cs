using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.DomainModels;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// The tag every roadmap item carries: derived from the title, editable, and held
/// apart from the title so a rename cannot silently move a tag other parts of the
/// product already point at.
/// </summary>
public class PlanningTagTests
{
    [Theory]
    [InlineData("Hello World", "hello-world")]
    [InlineData("MiXeD CaSe", "mixed-case")]
    [InlineData("Feature X!", "feature-x")]
    [InlineData("  padded  ", "padded")]
    [InlineData("a---b__c  d", "a-b-c-d")]
    [InlineData("Extract the sync service", "extract-the-sync-service")]
    [InlineData("1.0 release", "1-0-release")]
    public void ATitleSlugifiesToKebabCase(string title, string expected)
    {
        Assert.Equal(expected, PlanningTag.From(title).Value);
    }

    [Fact]
    public void DiacriticsAreFoldedRatherThanDropped()
    {
        Assert.Equal("cafe-plan", PlanningTag.From("Café Plan").Value);
        Assert.Equal("aeoue", PlanningTag.From("äëöüé").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("---")]
    [InlineData("…")]
    public void ATitleThatSlugifiesToNothing_FallsBackToAStableValue(string title)
    {
        // Deterministic so two such titles do not collide by accident and a reader
        // always has something to point at.
        Assert.Equal(PlanningTag.Fallback, PlanningTag.From(title).Value);
        Assert.Equal("item", PlanningTag.From(title).Value);
    }

    [Fact]
    public void AnExplicitValueIsNormalizedThroughTheSameRules()
    {
        Assert.Equal("feature-x", PlanningTag.Of("Feature X!").Value);
        Assert.Equal("already-a-slug", PlanningTag.Of("already-a-slug").Value);
    }

    [Fact]
    public void EqualityIsByValue()
    {
        Assert.Equal(PlanningTag.From("Hello World"), PlanningTag.Of("hello-world"));
    }

    [Theory]
    [InlineData("hello-world")]
    [InlineData("feature-x")]
    [InlineData("1-0-release")]
    [InlineData("item")]
    public void EveryProducedTagMatchesTheAgreedShape(string _)
    {
        // The one guarantee every consumer relies on: whatever went in, what comes out
        // matches ^[a-z0-9]+(-[a-z0-9]+)*$.
        var pattern = new System.Text.RegularExpressions.Regex("^[a-z0-9]+(-[a-z0-9]+)*$");

        Assert.Matches(pattern, PlanningTag.From("A Wild — Title!! (draft)").Value);
        Assert.Matches(pattern, PlanningTag.Of("  Spaced Out  ").Value);
        Assert.Matches(pattern, PlanningTag.From("###").Value);
    }
}

/// <summary>
/// The knowledge chapters an item points at: opaque references, trimmed and
/// de-duplicated, in the order they were given.
/// </summary>
public class KnowledgeReferencesTests
{
    [Fact]
    public void AnEmptySetIsValid_NotAnError()
    {
        Assert.True(KnowledgeReferences.Of(null).IsEmpty);
        Assert.True(KnowledgeReferences.Of([]).IsEmpty);
        Assert.True(KnowledgeReferences.Of(["", "   "]).IsEmpty);
    }

    [Fact]
    public void ReferencesAreTrimmed()
    {
        var refs = KnowledgeReferences.Of(["  .domain/backlog/domain.md  ", " a.md#b "]);

        Assert.Equal([".domain/backlog/domain.md", "a.md#b"], refs.Refs);
    }

    [Fact]
    public void DuplicatesCollapse_KeepingTheFirst()
    {
        var refs = KnowledgeReferences.Of(["a.md", "b.md", "a.md", " b.md "]);

        Assert.Equal(["a.md", "b.md"], refs.Refs);
    }

    [Fact]
    public void OrderIsPreserved_BecauseTheReadingOrderIsThePersons()
    {
        var refs = KnowledgeReferences.Of(["c.md", "a.md", "b.md"]);

        Assert.Equal(["c.md", "a.md", "b.md"], refs.Refs);
    }

    [Fact]
    public void EqualityIsByValue_AndOrderSensitive()
    {
        Assert.Equal(KnowledgeReferences.Of(["a.md", "b.md"]), KnowledgeReferences.Of(["a.md", "b.md"]));
        Assert.NotEqual(KnowledgeReferences.Of(["a.md", "b.md"]), KnowledgeReferences.Of(["b.md", "a.md"]));
    }
}

/// <summary>
/// How the plan threads tags and knowledge references through its items, and how it
/// surfaces where a tag is shared.
/// </summary>
public class PlanTaggingTests
{
    private static PlannedWindow Window(int startDay, int endDay) =>
        PlannedWindow.Create(new DateOnly(2026, 1, startDay), new DateOnly(2026, 1, endDay)).Value;

    [Fact]
    public void AnItemAddedWithoutATag_GetsOneDerivedFromItsTitle()
    {
        var plan = RoadmapPlan.Empty();

        var item = plan.AddItem("Extract the sync service", Window(5, 9)).Value;

        Assert.Equal("extract-the-sync-service", item.Tag.Value);
    }

    [Fact]
    public void AnExplicitTagWins_OverTheOneTheTitleWouldDerive()
    {
        var plan = RoadmapPlan.Empty();

        var item = plan.AddItem(
            "Extract the sync service",
            Window(5, 9),
            tag: PlanningTag.Of("sync")).Value;

        Assert.Equal("sync", item.Tag.Value);
    }

    [Fact]
    public void RenamingTheTitle_LeavesTheTagAlone()
    {
        var plan = RoadmapPlan.Empty();
        var item = plan.AddItem("Original title", Window(5, 9)).Value;
        var tagBefore = item.Tag;

        plan.Rename(item.Id, "A completely different title");

        // The tag did not follow the title: something elsewhere may already point at it.
        Assert.Equal("original-title", tagBefore.Value);
        Assert.Equal(tagBefore, plan.Items.Single().Tag);
    }

    [Fact]
    public void UpdatingWithAnExplicitTag_MovesItOnlyThen()
    {
        var plan = RoadmapPlan.Empty();
        var item = plan.AddItem("Original title", Window(5, 9)).Value;

        plan.UpdateItem(
            item.Id,
            "Renamed, but the tag is set on purpose",
            Window(5, 9),
            PlanningPriority.Medium,
            tag: PlanningTag.Of("chosen-tag"));

        Assert.Equal("chosen-tag", item.Tag.Value);
    }

    [Fact]
    public void UpdatingWithNoTag_RederivesFromTheTitle_BecauseTheFieldWasCleared()
    {
        var plan = RoadmapPlan.Empty();
        var item = plan.AddItem("Original title", Window(5, 9), tag: PlanningTag.Of("old")).Value;

        plan.UpdateItem(item.Id, "Fresh title", Window(5, 9), PlanningPriority.Medium, tag: null);

        Assert.Equal("fresh-title", item.Tag.Value);
    }

    [Fact]
    public void KnowledgeReferencesAreTrimmedAndDeduplicated_OnAdd()
    {
        var plan = RoadmapPlan.Empty();

        var item = plan.AddItem(
            "Work",
            Window(5, 9),
            knowledgeRefs: KnowledgeReferences.Of([" a.md ", "a.md", "b.md#x"])).Value;

        Assert.Equal(["a.md", "b.md#x"], item.KnowledgeRefs.Refs);
    }

    [Fact]
    public void KnowledgeReferencesAreWrittenBack_OnUpdate()
    {
        var plan = RoadmapPlan.Empty();
        var item = plan.AddItem("Work", Window(5, 9)).Value;

        plan.UpdateItem(
            item.Id,
            "Work",
            Window(5, 9),
            PlanningPriority.Medium,
            knowledgeRefs: KnowledgeReferences.Of(["chapter.md#h"]));

        Assert.Equal(["chapter.md#h"], item.KnowledgeRefs.Refs);
    }

    [Fact]
    public void TheDistinctTagsInUseAreReported_OnceEach()
    {
        var plan = RoadmapPlan.Empty();
        plan.AddItem("Alpha", Window(5, 9), tag: PlanningTag.Of("shared"));
        plan.AddItem("Beta", Window(5, 9), tag: PlanningTag.Of("solo"));
        plan.AddItem("Gamma", Window(5, 9), tag: PlanningTag.Of("shared"));

        Assert.Equal(["shared", "solo"], plan.TagsInUse().Select(tag => tag.Value));
    }

    [Fact]
    public void TwoItemsMayShareATag_AndBothAreFound()
    {
        var plan = RoadmapPlan.Empty();
        var first = plan.AddItem("Alpha", Window(5, 9), tag: PlanningTag.Of("shared")).Value;
        plan.AddItem("Beta", Window(5, 9), tag: PlanningTag.Of("solo"));
        var second = plan.AddItem("Gamma", Window(5, 9), tag: PlanningTag.Of("shared")).Value;

        var shared = plan.ItemsTagged(PlanningTag.Of("shared"));

        // A shared tag is a deliberate grouping, not a fault: both items come back.
        Assert.Equal([first.Id, second.Id], shared.Select(item => item.Id));
    }

    [Fact]
    public void AskingForATagNothingCarries_ReturnsNothing()
    {
        var plan = RoadmapPlan.Empty();
        plan.AddItem("Alpha", Window(5, 9), tag: PlanningTag.Of("here"));

        Assert.Empty(plan.ItemsTagged(PlanningTag.Of("absent")));
    }
}
