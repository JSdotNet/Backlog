namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The marks. Drawn in the file rather than fetched, filled rather than stroked,
/// and hidden from assistive technology unless somebody asks for the opposite.
/// </summary>
public sealed class ProviderMarkTests
{
    [Theory]
    [InlineData(IntegrationProvider.GitHub, "github")]
    [InlineData(IntegrationProvider.Copilot, "copilot")]
    [InlineData(IntegrationProvider.Claude, "claude")]
    [InlineData(IntegrationProvider.VsCode, "vscode")]
    public void Every_provider_has_a_mark_of_its_own(IntegrationProvider provider, string slug)
    {
        // At Compact density the mark is the only thing separating a Claude
        // session row from a Copilot session row from a GitHub issue row, so
        // four distinct paths is a legibility requirement rather than a polish.
        using var context = new BunitContext();

        var mark = context.Render<ProviderMark>(parameters => parameters
            .Add(m => m.Provider, provider)
            .Add(m => m.TestId, "mark"));

        var svg = mark.Find("[data-testid='mark']");

        Assert.Contains($"provider-mark--{slug}", svg.ClassList);
        Assert.False(string.IsNullOrWhiteSpace(mark.Find("path").GetAttribute("d")));
    }

    [Fact]
    public void The_four_marks_are_four_different_drawings()
    {
        using var context = new BunitContext();

        var paths = new[]
        {
            IntegrationProvider.GitHub,
            IntegrationProvider.Copilot,
            IntegrationProvider.Claude,
            IntegrationProvider.VsCode
        }
        .Select(provider => context
            .Render<ProviderMark>(parameters => parameters.Add(m => m.Provider, provider))
            .Find("path")
            .GetAttribute("d"))
        .ToList();

        Assert.Equal(4, paths.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void No_provider_draws_nothing_at_all()
    {
        // Not an empty svg: a bar of provider-less acts must have no phantom gap
        // where a glyph would have been.
        using var context = new BunitContext();

        var mark = context.Render<ProviderMark>(parameters => parameters
            .Add(m => m.Provider, IntegrationProvider.None));

        Assert.Equal(string.Empty, mark.Markup.Trim());
    }

    [Fact]
    public void A_mark_is_hidden_until_it_is_the_only_thing_saying_which()
    {
        // Beside a text label it would be announced twice; where the label is
        // gone it is the name.
        using var context = new BunitContext();

        var quiet = context.Render<ProviderMark>(parameters => parameters
            .Add(m => m.Provider, IntegrationProvider.GitHub)
            .Add(m => m.TestId, "mark"));

        var quietSvg = quiet.Find("[data-testid='mark']");

        Assert.Equal("true", quietSvg.GetAttribute("aria-hidden"));
        Assert.Equal("false", quietSvg.GetAttribute("focusable"));
        Assert.Null(quietSvg.GetAttribute("role"));

        var named = context.Render<ProviderMark>(parameters => parameters
            .Add(m => m.Provider, IntegrationProvider.GitHub)
            .Add(m => m.Title, "GitHub")
            .Add(m => m.TestId, "mark"));

        var namedSvg = named.Find("[data-testid='mark']");

        Assert.Equal("img", namedSvg.GetAttribute("role"));
        Assert.Null(namedSvg.GetAttribute("aria-hidden"));
        Assert.Equal("GitHub", named.Find("title").TextContent);
    }

    [Fact]
    public void Marks_are_filled_and_inherit_their_ink()
    {
        // Marks are filled and state icons are stroked — the rule that keeps a
        // provider and a state from reading as the same class of thing at 16 px.
        // And because everything is currentColor, the same path is the live ink
        // on an open issue and the disabled ink on a Connect button.
        using var context = new BunitContext();

        var mark = context.Render<ProviderMark>(parameters => parameters
            .Add(m => m.Provider, IntegrationProvider.Claude)
            .Add(m => m.TestId, "mark"));

        var svg = mark.Find("[data-testid='mark']");

        Assert.Equal("currentColor", svg.GetAttribute("fill"));
        Assert.Null(svg.GetAttribute("stroke"));
    }

    [Fact]
    public void The_size_is_in_pixels_and_not_in_ems()
    {
        // Sized from the row rather than from the font, so a mark and the state
        // icon beside it do not come apart when a reader zooms the text.
        using var context = new BunitContext();

        var mark = context.Render<ProviderMark>(parameters => parameters
            .Add(m => m.Provider, IntegrationProvider.VsCode)
            .Add(m => m.Size, 24)
            .Add(m => m.TestId, "mark"));

        var svg = mark.Find("[data-testid='mark']");

        Assert.Equal("24", svg.GetAttribute("width"));
        Assert.Equal("24", svg.GetAttribute("height"));
    }
}
