using System.Text.RegularExpressions;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What a rename field in a list looks like: the title, with a caret in it, and
/// nothing drawn under it.
///
/// <para>The rule that draws the field was written when the list had one kind of
/// it — the one the pencil opens — and there the line under the box was doing two
/// jobs: saying the edit had arrived, and standing in for the focus ring, which
/// is why it came paired with <c>outline: none</c>. Two later kinds of field, the
/// direct-rename row and the add row, are fields from the start; on those the same
/// line is permanent chrome rather than a signal, so each was given an override
/// that took it away and paid for a real outline. The base case was never revisited,
/// and the pencil's field kept a line the two newer ones had already rejected.</para>
///
/// <para>Asserted against the stylesheet rather than a render, for the reason
/// <c>MarkdownEditorAutoGrowLayoutTests</c> gives for the same shape of test: the
/// markup is not in question — <c>TaskItem</c>, <c>TaskListView</c> and
/// <c>TaskPanel</c> all put the classes where they belong — and the whole of the
/// defect is in what the cascade does with them, which bUnit brings no layout
/// engine to see.</para>
/// </summary>
public sealed class TaskRenameFieldUnderlineTests
{
    /// <summary>
    /// The regression. Every rename field in a list is the title itself taking a
    /// caret, so none of them is underlined — not the pencil's, not a direct-rename
    /// row's, not the add row's. One rule states it for all three; the moment the
    /// base rule draws a border again, the two overrides below are back to arguing
    /// with it and the pencil's field is the one that loses.
    /// </summary>
    [Fact]
    public void A_rename_field_in_the_list_has_no_line_under_it()
    {
        var field = Rule(".task-item__rename-input");

        Assert.False(
            DrawsABottomBorder(field),
            "A field that replaces the title is the title with a caret in it, and a line under it is the one "
            + "thing that makes it look like something else. It is also not needed to say where the focus is — "
            + $"the outline below does that, and says it the way .design/accessibility.md#focus-visibility asks. Rule found:\n{field}");
    }

    /// <summary>
    /// What the border was quietly paying for. <c>outline: none</c> without a
    /// replacement is prohibited by <c>.design/accessibility.md#focus-visibility</c>,
    /// and the border was the replacement — so taking it away without putting the
    /// outline back would have traded a cosmetic defect for an accessibility one.
    /// </summary>
    [Fact]
    public void A_rename_field_in_the_list_still_shows_where_the_focus_is()
    {
        var focus = Rule(".task-item__rename-input:focus-visible");

        Assert.Matches(@"outline:\s*var\(--border-width-2\)\s+solid\s+var\(--color-border-focus\)", focus);
        Assert.Matches(@"outline-offset:\s*2px", focus);
    }

    /// <summary>
    /// The panel is not the list. Its title is a heading rather than a row, it is
    /// edited by clicking the heading rather than a pencil, and its line under the
    /// field is wanted — so the fix above stops at the list. Named here so that
    /// folding the two rules together later has to be a decision rather than a
    /// tidy-up.
    /// </summary>
    [Fact]
    public void The_panel_title_field_keeps_the_line_under_it()
    {
        var field = Rule(".task-panel__rename-input");

        Assert.True(
            DrawsABottomBorder(field),
            "The side panel's title field is deliberately underlined and is not part of this fix. Rule "
            + $"found:\n{field}");
    }

    private static bool DrawsABottomBorder(string rule) =>
        Regex.IsMatch(rule, @"border(-bottom)?\s*:\s*(?!none|0)[^;]*(?<!none)\bsolid\b");

    /// <summary>The rule whose selector list starts a line with <paramref name="selector"/>
    /// and ends there, braces matched so a nested block cannot close it early.
    /// Anchored to the start of a line because every selector here is also the tail
    /// of a longer one — <c>.task-item__rename &gt; .task-item__rename-input</c> —
    /// and a plain substring search would return that rule's body instead.</summary>
    private static string Rule(string selector)
    {
        var css = Css();

        var start = css.IndexOf("\n" + selector + " {", StringComparison.Ordinal) + 1;
        Assert.True(start > 0, $"components.css has no rule for {selector}.");

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
