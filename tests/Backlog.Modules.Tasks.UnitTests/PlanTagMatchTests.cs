using Backlog.Modules.Tasks.Abstractions;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// Whether a stored tag carries a roadmap item's slug — the tag half of Roadmap Item
/// Gathering (.devbook/domain/roadmap/domain.md#roadmap-item-gathering). The roadmap's
/// rollup and the Tasks list's "Open in Tasks" both ask it, so the two cannot count a
/// different set of entries for one item.
/// </summary>
public sealed class PlanTagMatchTests
{
    [Theory]
    [InlineData("+release-q4")]
    [InlineData("release-q4")]
    [InlineData("+RELEASE-Q4")]
    [InlineData("Release-Q4")]
    public void A_tag_spelling_the_slug_with_or_without_its_sigil_carries_it(string tag)
    {
        Assert.True(PlanTagMatch.Carries(tag, "release-q4"));
    }

    [Theory]
    [InlineData("@release-q4")]
    [InlineData("other")]
    [InlineData("+other")]
    [InlineData("release-q4-x")]
    [InlineData("++release-q4")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_tag_naming_something_else_does_not_carry_it(string? tag)
    {
        Assert.False(PlanTagMatch.Carries(tag, "release-q4"));
    }

    /// <summary>A goal's attached plan counts what the roadmap item gathers: the slugs a
    /// task carries are the plan tag less its sigil and the bare tag an entry filed before
    /// the sigil carries — the one a person types as <c>#slug</c> — and never a person.</summary>
    [Fact]
    public void The_slugs_a_task_carries_are_its_plan_tags_and_its_bare_tags_but_no_person()
    {
        Assert.Equal(
            ["release-q4", "qa-plan-completion-tasked"],
            PlanTagMatch.SlugsCarried(["+release-q4", "qa-plan-completion-tasked", "@release-q4", "+", " ", "Release-Q4"]));
    }

    [Theory]
    [InlineData("+release-q4")]
    [InlineData("release-q4")]
    [InlineData("RELEASE-Q4")]
    [InlineData("@release-q4")]
    [InlineData("other")]
    [InlineData("++release-q4")]
    public void A_slug_is_carried_exactly_when_the_roadmap_rollup_says_it_is(string tag)
    {
        Assert.Equal(
            PlanTagMatch.Carries(tag, "release-q4"),
            PlanTagMatch.SlugsCarried([tag]).Contains("release-q4", StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void No_slug_is_carried_by_anything(string? slug)
    {
        Assert.False(PlanTagMatch.Carries("+", slug));
        Assert.False(PlanTagMatch.Carries("", slug));
    }
}
