using AngleSharp.Dom;

namespace Backlog.Tests;

/// <summary>
/// What every pane header that adopted the library's <c>SectionHeader</c> still
/// renders in the pane's own names. Shared because the panes live in different
/// test projects — the shell's in <c>Backlog.Desktop.UI.UnitTests</c>, the Sessions
/// pane's in <c>Backlog.Modules.Sessions.UnitTests</c> — and it is linked by the
/// projects that render a pane rather than from <c>tests/Directory.Build.props</c>,
/// because it needs bUnit's DOM.
/// </summary>
internal static class PaneHeaderAssertions
{
    /// <summary>
    /// The full pane-header shape, in the pane's own names: a bare text block
    /// holding an eyebrow, the heading its landmark points at, and a subtitle.
    /// Every one of those classes is styled in app.css, which the adoption was
    /// not allowed to touch.
    /// </summary>
    internal static void AssertPaneHeader(IElement header, string block, string headingId)
    {
        Assert.Equal("HEADER", header.TagName);
        Assert.Equal($"{block}__header", header.GetAttribute("class"));

        var text = header.Children[0];
        Assert.Equal("DIV", text.TagName);
        Assert.Null(text.GetAttribute("class"));

        Assert.Equal(["P", "H2", "P"], text.Children.Select(child => child.TagName));
        Assert.Equal(
            [$"{block}__eyebrow", $"{block}__title", $"{block}__subtitle"],
            text.Children.Select(child => child.GetAttribute("class")));

        Assert.Equal(headingId, text.Children[1].GetAttribute("id"));
    }

    /// <summary>The actions wrapper, which the panes with controls in the header
    /// keep under their own name.</summary>
    internal static void AssertPaneHeaderActions(IElement header, string block)
    {
        Assert.Equal(2, header.Children.Length);
        Assert.Equal("DIV", header.Children[1].TagName);
        Assert.Equal($"{block}__header-actions", header.Children[1].GetAttribute("class"));
    }
}
