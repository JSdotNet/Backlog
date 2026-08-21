using System.Text.RegularExpressions;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The header's bargain with a path longer than the header: it gives up room
/// first, it truncates inside the column it was given, and the whole string
/// stays in the DOM.
///
/// <para>The first half of that is asserted against the stylesheet rather than a
/// render, for the reason <c>DesignTokenTests</c> gives for the same shape of
/// test: the markup was always right — the path is in the aside, wearing the
/// class it should — and the defect was entirely in what the stylesheet did with
/// it. bUnit brings no layout engine, so a render test can only confirm the
/// class is present, which it already was.</para>
/// </summary>
public sealed class FileViewHeaderLayoutTests
{
    /// <summary>
    /// The regression. <c>.file-view__aside</c> stacks its children, so a child's
    /// <c>flex-shrink</c> and its <c>min-width: 0</c> govern the main axis — which
    /// is vertical here — and neither of them touches its width. Width comes off
    /// the cross axis instead, as fit-content, and fit-content is never smaller
    /// than min-content: for a <c>white-space: nowrap</c> string that is the whole
    /// string. So the path kept its full width inside a column a third of it,
    /// overflowed leftwards out of the card, and painted over the file's own name
    /// while the ellipsis it declares never engaged.
    ///
    /// <para>A cap is the only constraint the cross axis honours here, and it is
    /// what hands <c>overflow: hidden</c> something to truncate against.</para>
    /// </summary>
    [Fact]
    public void Nothing_stacked_in_the_aside_may_grow_wider_than_the_column_it_was_given()
    {
        var rule = Rule(".file-view__aside > *");

        Assert.True(
            Regex.IsMatch(rule, @"max-width\s*:\s*100%"),
            "The path is a stacked child of .file-view__aside, so flex-shrink and min-width: 0 govern its "
            + "height and its width is fit-content — never below the min-content of a nowrap string, which "
            + "is the whole path. Without a max-width the box keeps its full width, spills out of the "
            + "column and draws over .file-view__name, and the declared ellipsis never appears. Rule "
            + $"found:\n{rule}");
    }

    /// <summary>The cap and the ellipsis are one fix, not two: a cap on its own
    /// cuts the path off mid-character with nothing to say it was cut, and an
    /// ellipsis on its own — which is what shipped — never engages, because the box
    /// grows to the text instead of the text being clipped to the box.</summary>
    [Fact]
    public void What_the_cap_cuts_off_is_still_marked_with_an_ellipsis()
    {
        var rule = Rule(".file-view__path,");

        Assert.Contains("white-space: nowrap", rule, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden", rule, StringComparison.Ordinal);
        Assert.Contains("text-overflow: ellipsis", rule, StringComparison.Ordinal);
    }

    /// <summary>The other half of the bargain, and the reason the fix had to be a
    /// visual one: shortening the string on the way in would have made the header
    /// look right and told a screen reader a path that is not the file's.</summary>
    [Fact]
    public void The_whole_path_is_in_the_DOM_however_little_of_it_is_drawn()
    {
        const string path = @".arc42\adr\0002-backlog-module-owns-the-entry-text-language.md";

        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "0002-backlog-module-owns-the-entry-text-language.md")
            .Add(v => v.Path, path));

        var rendered = view.Find(".file-view__path");

        Assert.Equal(path, rendered.TextContent);
        Assert.DoesNotContain('…', rendered.TextContent);

        // No title attribute either: a native tooltip is not keyboard-reachable,
        // and the string it would repeat is already the element's own text.
        Assert.False(rendered.HasAttribute("title"));
    }

    /// <summary>The rule that opens with <paramref name="selector"/>, braces matched
    /// so a nested block cannot end it early. Asserted rather than returned empty:
    /// a selector that no longer exists has to fail here as a rule this test cannot
    /// find, not further down as a green run against nothing.</summary>
    private static string Rule(string selector)
    {
        var css = File.ReadAllText(RepositoryRoot.File(
            "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css")).Replace("\r\n", "\n");

        var start = css.IndexOf(selector, StringComparison.Ordinal);
        Assert.True(start >= 0, $"components.css has no rule for {selector}.");

        var depth = 0;

        for (var index = css.IndexOf('{', start); index >= 0 && index < css.Length; index++)
        {
            if (css[index] == '{')
            {
                depth++;
            }
            else if (css[index] == '}' && --depth == 0)
            {
                return css[start..(index + 1)];
            }
        }

        Assert.Fail($"The rule for {selector} in components.css is never closed.");
        return string.Empty;
    }
}
