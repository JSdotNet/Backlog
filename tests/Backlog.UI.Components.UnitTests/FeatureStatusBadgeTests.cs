namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The maturity flag a half-finished feature wears.
///
/// <para>The rule worth protecting is the silent one: a released feature renders
/// <em>nothing</em>, not an empty badge and not a badge saying "released". Every
/// caller drops this component into a header unconditionally and relies on that,
/// so a stray wrapper element here would put an empty box beside seven buttons at
/// once.</para>
/// </summary>
public sealed class FeatureStatusBadgeTests
{
    [Theory]
    [InlineData("dev", "DEV")]
    [InlineData("beta", "BETA")]
    public void A_flagged_feature_wears_its_kind_and_its_value(string status, string expected)
    {
        using var context = new BunitContext();

        var badge = context.Render<FeatureStatusBadge>(parameters => parameters
            .Add(b => b.Status, status));

        var span = badge.Find("span");

        Assert.Equal($"badge badge--feature badge--feature-{status}", span.GetAttribute("class"));

        // The stylesheet uppercases it; the DOM keeps the word. Asserted
        // case-insensitively so this test is about the slug reaching the text,
        // not about which layer does the shouting.
        Assert.Equal(expected, span.TextContent.ToUpperInvariant());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_feature_with_no_status_renders_nothing_at_all(string? status)
    {
        using var context = new BunitContext();

        var badge = context.Render<FeatureStatusBadge>(parameters => parameters
            .Add(b => b.Status, status));

        Assert.Equal(string.Empty, badge.Markup.Trim());
    }

    /// <summary>Unlike <c>StatusBadge</c>, which falls back to <c>draft</c>. A
    /// backlog entry always has a status and an unreadable one is worth showing;
    /// a feature status is genuinely optional, so an unslugifiable one is the
    /// same as none.</summary>
    [Fact]
    public void An_unslugifiable_status_renders_nothing_rather_than_falling_back()
    {
        using var context = new BunitContext();

        var badge = context.Render<FeatureStatusBadge>(parameters => parameters
            .Add(b => b.Status, "!!!"));

        Assert.Equal(string.Empty, badge.Markup.Trim());
    }

    [Fact]
    public void The_title_carries_the_sentence_the_badge_has_no_room_for()
    {
        using var context = new BunitContext();

        var badge = context.Render<FeatureStatusBadge>(parameters => parameters
            .Add(b => b.Status, "dev")
            .Add(b => b.Title, "In development — not usable yet.")
            .Add(b => b.TestId, "inbox-feature-status"));

        var span = badge.Find("span");

        Assert.Equal("In development — not usable yet.", span.GetAttribute("title"));
        Assert.Equal("inbox-feature-status", span.GetAttribute("data-testid"));
    }

    [Fact]
    public void Text_overrides_the_label_without_touching_the_slug()
    {
        using var context = new BunitContext();

        var badge = context.Render<FeatureStatusBadge>(parameters => parameters
            .Add(b => b.Status, "beta")
            .Add(b => b.Text, "Preview"));

        var span = badge.Find("span");

        Assert.Contains("badge--feature-beta", span.GetAttribute("class"));
        Assert.Equal("Preview", span.TextContent);
    }
}
