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
