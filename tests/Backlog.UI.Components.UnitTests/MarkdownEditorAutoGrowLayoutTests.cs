using System.Text.RegularExpressions;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The markdown editor's bargain with auto-grow: the two stacked layers keep the
/// same box however the textarea is sized, and the replica that does the sizing
/// measures the text in the box the textarea lays it out in.
///
/// <para>Asserted against the stylesheet rather than a render, for the reason
/// <c>FileViewHeaderLayoutTests</c> gives for the same shape of test: the markup
/// was always right — the highlight layer, the textarea and the grow wrapper all
/// wear the classes they should — and the defect was entirely in what the
/// cascade did with them. bUnit brings no layout engine, so a render test can
/// only confirm the classes are present, which they already were. What the
/// render here does prove is the premise the selectors below are written
/// against: that an auto-growing editor really does put both class names on one
/// wrapper.</para>
/// </summary>
public sealed class MarkdownEditorAutoGrowLayoutTests
{
    /// <summary>
    /// The regression. <c>MarkdownEditor</c> stacks a coloured
    /// <c>pre.markdown-editor__highlight</c> behind a transparent
    /// <c>textarea.markdown-editor__input</c>, and they only line up while every
    /// box metric matches — which is why the metrics are stated once, for both,
    /// in a single rule. When a host asks for auto-grow, <c>TextArea</c> adds the
    /// shared <c>grow-wrap</c> class, and <c>.grow-wrap &gt; textarea</c> — a
    /// class and an element against that rule's single class — zeroed the
    /// textarea's padding while the layer behind it kept
    /// <c>var(--spacing-sm)</c>. Every coloured character then sat one padding
    /// down and to the right of the caret that typed it.
    /// </summary>
    [Fact]
    public void The_auto_grow_textarea_is_padded_the_way_the_layer_behind_it_is()
    {
        var shared = Rule(".markdown-editor__input,");
        var autoGrow = Rule(".markdown-editor__grow.grow-wrap > .markdown-editor__input");

        Assert.True(
            IsPaddedBySpacingSm(shared),
            "The highlight layer and the textarea are one box drawn twice, and this is the rule that says "
            + $"so. Stop stating the padding here and the two layers diverge everywhere. Rule found:\n{shared}");

        Assert.True(
            IsPaddedBySpacingSm(autoGrow),
            "An auto-growing editor's wrapper also carries .grow-wrap, whose `padding: 0` outranks the "
            + "shared rule above. Without this override the textarea is unpadded, the highlight layer is "
            + $"not, and every colour lands var(--spacing-sm) away from its word. Rule found:\n{autoGrow}");
    }

    /// <summary>
    /// The replica is not decoration: <c>.grow-wrap::after</c> carries the same
    /// text and sizes the grid track the textarea stretches into. Measured in a
    /// box a padding wider than the one the textarea wraps in, it fits more text
    /// per line and hands back a track a line short — so the textarea scrolls
    /// inside itself and clips its last line, which is what looked like task
    /// items overlapping. The tab size and the word breaking are here for the
    /// same reason: they decide where a line ends as surely as the padding does,
    /// and the replica inherits neither from the rule that states them for the
    /// textarea.
    /// </summary>
    [Fact]
    public void The_replica_that_sizes_the_track_measures_the_text_in_the_same_box()
    {
        var replica = Rule(".markdown-editor__grow.grow-wrap::after");

        Assert.True(
            IsPaddedBySpacingSm(replica),
            "The replica is the box that decides how tall the textarea gets. Measure the text without the "
            + "padding the textarea wraps it in and the track comes back short, so the textarea scrolls "
            + $"inside itself and the last line is never drawn. Rule found:\n{replica}");

        Assert.Contains("tab-size: 4", replica, StringComparison.Ordinal);
        Assert.Contains("word-break: normal", replica, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same bargain <c>.entry-doc__editor</c> already makes for the other
    /// auto-growing textarea in this stylesheet. Once the replica owns the
    /// height, a textarea that still scrolls hides text the box was grown to
    /// show, its scrollbar narrows it below the width the replica measured at —
    /// reintroducing the wrapping difference by another route — and a drag handle
    /// sets a height the next keystroke overwrites. The editor that does not
    /// grow keeps both, because there the textarea is the only thing that can
    /// reach the rest of the text.
    /// </summary>
    [Fact]
    public void The_auto_grow_textarea_neither_scrolls_nor_resizes_itself()
    {
        var autoGrow = Rule(".markdown-editor__grow.grow-wrap > .markdown-editor__input");

        Assert.Contains("overflow: hidden", autoGrow, StringComparison.Ordinal);
        Assert.Contains("resize: none", autoGrow, StringComparison.Ordinal);

        // And only there: the non-growing editor is scrolled and resized by hand,
        // because nothing else is sizing it.
        var plain = Rule(".markdown-editor__input {");

        Assert.Contains("overflow: auto", plain, StringComparison.Ordinal);
        Assert.Contains("resize: vertical", plain, StringComparison.Ordinal);
    }

    /// <summary>
    /// The fix is a cascade argument, so the cascade is what has to be pinned.
    /// The override ties <c>.grow-wrap::after</c> on weight, so only its position
    /// in the file decides which one wins, and a reshuffle that moved the
    /// markdown-editor section above the shared grow-wrap rules would put the bug
    /// back with nothing else changing.
    /// </summary>
    [Fact]
    public void The_overrides_still_come_after_the_rule_that_zeroes_the_padding()
    {
        var css = Css();

        var zeroing = css.IndexOf(".grow-wrap > textarea,", StringComparison.Ordinal);
        Assert.True(zeroing >= 0, "components.css no longer has the shared .grow-wrap > textarea rule.");

        foreach (var selector in new[]
                 {
                     ".markdown-editor__grow.grow-wrap > .markdown-editor__input",
                     ".markdown-editor__grow.grow-wrap::after"
                 })
        {
            Assert.True(
                css.IndexOf(selector, StringComparison.Ordinal) > zeroing,
                $"`{selector}` has to stay below the .grow-wrap rule that sets `padding: 0`. The one on the "
                + "replica only ties that rule on weight, so for it order is the whole of the argument: "
                + "move the pair up and the padding goes back to zero, silently, with every declaration "
                + "still where it is.");
        }
    }

    /// <summary>
    /// What ties the stylesheet to the component. Every selector above is written
    /// against one wrapper carrying both class names — the shared one that brings
    /// the grid and the zeroed padding, and the editor's own that scopes the
    /// override to this editor. <c>TextArea</c> composes that pair itself, from
    /// two parameters, only when auto-grow is on; if it ever stopped, the rules
    /// would match nothing and go on being green.
    /// </summary>
    [Fact]
    public void An_auto_growing_editor_renders_the_wrapper_those_rules_select()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<MarkdownEditor>(parameters => parameters
            .Add(e => e.Value, "# Heading")
            .Add(e => e.AutoGrow, true));

        var textarea = view.Find("textarea.markdown-editor__input");
        var wrapper = textarea.ParentElement;

        Assert.NotNull(wrapper);
        Assert.Contains("grow-wrap", wrapper.ClassList);
        Assert.Contains("markdown-editor__grow", wrapper.ClassList);

        // The replica reads its text from here, which is why it is the box that
        // has to be measured like the textarea rather than beside it.
        Assert.Equal("# Heading", wrapper.GetAttribute("data-replicated-value"));
    }

    private static bool IsPaddedBySpacingSm(string rule) =>
        Regex.IsMatch(rule, @"padding\s*:\s*var\(--spacing-sm\)");

    /// <summary>The rule that opens with <paramref name="selector"/>, braces matched
    /// so a nested block cannot end it early. Asserted rather than returned empty:
    /// a selector that no longer exists has to fail here as a rule this test cannot
    /// find, not further down as a green run against nothing.</summary>
    private static string Rule(string selector)
    {
        var css = Css();

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

    private static string Css() => File.ReadAllText(RepositoryRoot.File(
        "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css")).Replace("\r\n", "\n");
}
