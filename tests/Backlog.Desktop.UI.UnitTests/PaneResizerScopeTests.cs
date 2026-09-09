namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// <c>pane-resizer</c> is worn by two separators, and a rule that names it without
/// saying which one is a rule about both.
/// <para>
/// The shell's side pane has its own separator, and it wears the library's class on
/// purpose — <c>SharedControlAdoptionTests</c> records that as a documented
/// duplication, because the shell drives it through its own interop and
/// <c>SplitPane</c> cannot take that over yet. Every <c>SplitPane</c> separator wears
/// the same class, which is what makes the borrowed name dangerous rather than merely
/// untidy: a rule written for one of them silently reaches the other.
/// </para>
/// <para>
/// It did. The 72rem step stacks the knowledge layout to one column and hid
/// <c>.pane-resizer</c> unscoped, on the reasoning that a stacked layout has no
/// vertical edge left to drag. True of that layout; false of the split inside its
/// pane, which is still side by side at 72rem and stacks at 60rem. Between the two
/// steps the split kept <c>.split-pane--end</c>'s three-track template while having
/// only two grid items, so the trailing pane auto-placed into the middle — the
/// separator's own track — and the third track sat empty. Measured in the harness
/// with an entry open, <c>.backlog-split</c> resolved to <c>624px 236px 236px</c> at
/// 1152 and <c>624px 160px 160px</c> at 1000, where the third track gives the entry
/// 456px and 312px. The entry was drawn at about half its width, and the handle that
/// could have corrected it was the thing that had been removed.
/// </para>
/// <para>
/// So the fix is to say which separator, and these tests hold the two halves of that:
/// the step names the layout it is about, and no step anywhere in the sheet goes back
/// to naming the class alone.
/// </para>
/// </summary>
public sealed class PaneResizerScopeTests
{
    /// <summary>Where the knowledge layout stops being two columns.</summary>
    private const string NarrowStep = "@media (max-width: 72rem) {";

    /// <summary>And where the split inside its pane stops being two, which is a
    /// different width because it is a different layout.</summary>
    private const string StackStep = "@media (max-width: 60rem) {";

    /// <summary>
    /// The narrow step hides the shell's own separator, named by the layout it
    /// belongs to.
    /// <para>
    /// A child combinator rather than a descendant one, because the split's separator
    /// is also inside <c>.knowledge-layout</c> — two levels down, through the tasks
    /// workspace — so a descendant selector would hide exactly what this is scoped to
    /// spare. The sheet already writes the layout's own children this way; see
    /// <c>.knowledge-layout--inbox-before-backlog &gt; .inbox-pane</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void The_narrow_step_hides_the_shells_own_separator()
    {
        var narrow = Block(Css(), NarrowStep);

        Assert.Contains(".knowledge-layout > .pane-resizer {", narrow, StringComparison.Ordinal);
    }

    /// <summary>
    /// And styles nothing about the split, which at this width is still a split.
    /// <para>
    /// Comments stripped first, because the rule above this one names both classes in
    /// prose to explain why it is scoped. Reading them as declarations would make the
    /// explanation the failure.
    /// </para>
    /// </summary>
    [Fact]
    public void The_narrow_step_leaves_the_backlog_split_alone()
    {
        var narrow = StripComments(Block(Css(), NarrowStep));

        Assert.DoesNotContain(".split-pane__separator", narrow, StringComparison.Ordinal);
        Assert.DoesNotContain(".backlog-split", narrow, StringComparison.Ordinal);
    }

    /// <summary>
    /// The split loses its separator at its own step, and only there — the width at
    /// which it really has stacked and really has no vertical edge left.
    /// </summary>
    [Fact]
    public void The_split_keeps_its_separator_until_it_stops_being_a_split()
    {
        var stack = Block(Css(), StackStep);

        Assert.Contains(".backlog-split .split-pane__separator", stack, StringComparison.Ordinal);
        Assert.Contains("flex-direction: column;", stack, StringComparison.Ordinal);
    }

    /// <summary>
    /// The general rule, and the one that would have caught this: no viewport step
    /// hides the class on its own.
    /// <para>
    /// The defect was not that the wrong number was chosen — 72rem is right for the
    /// layout it was written for. It was that a shared class was addressed as though
    /// it named one element. Any future step that does the same will be wrong in the
    /// same way and for the same reason, whatever width it picks, so this is pinned
    /// as a property of the sheet rather than as a second copy of the case above.
    /// </para>
    /// </summary>
    [Fact]
    public void No_viewport_step_hides_every_separator_at_once()
    {
        var css = StripComments(Css());

        var offenders = MediaBlocks(css)
            .SelectMany(SelectorLists)
            .Where(selector => selector == ".pane-resizer" || selector == ".split-pane__separator")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A viewport step is addressing the separator class on its own, which reaches both the "
            + "shell's side pane and every SplitPane in the app. Name the layout the step is about:\n"
            + string.Join("\n", offenders));
    }

    private static string Css() => File.ReadAllText(
        RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css"))
        .Replace("\r\n", "\n");

    /// <summary>Every <c>@media</c> block in the sheet, braces matched so a nested
    /// rule cannot end the block early.</summary>
    private static IEnumerable<string> MediaBlocks(string css)
    {
        for (var at = css.IndexOf("@media", StringComparison.Ordinal); at >= 0;
             at = css.IndexOf("@media", at + 1, StringComparison.Ordinal))
        {
            yield return BlockAt(css, at);
        }
    }

    /// <summary>Each selector in the block, one per comma, trimmed — so that
    /// <c>.knowledge-layout &gt; .pane-resizer</c> and <c>.pane-resizer</c> are told
    /// apart rather than both matching a substring search.</summary>
    private static IEnumerable<string> SelectorLists(string block)
    {
        var from = block.IndexOf('{') + 1;

        for (var at = block.IndexOf('{', from); at > 0; at = block.IndexOf('{', from))
        {
            var previous = block.LastIndexOfAny(['}', '{'], at - 1);
            var list = block[(previous < 0 ? from : previous + 1)..at];

            foreach (var selector in list.Split(','))
            {
                yield return selector.Trim();
            }

            from = at + 1;
        }
    }

    private static string StripComments(string css)
    {
        var stripped = new System.Text.StringBuilder(css.Length);

        for (var at = 0; at < css.Length;)
        {
            var open = css.IndexOf("/*", at, StringComparison.Ordinal);

            if (open < 0)
            {
                stripped.Append(css[at..]);
                break;
            }

            stripped.Append(css[at..open]);

            var close = css.IndexOf("*/", open + 2, StringComparison.Ordinal);

            at = close < 0 ? css.Length : close + 2;
        }

        return stripped.ToString();
    }

    private static string Block(string css, string opening)
    {
        var start = css.IndexOf(opening, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{opening} should exist.");

        return BlockAt(css, start);
    }

    private static string BlockAt(string css, int start)
    {
        var depth = 0;

        for (var index = css.IndexOf('{', start); index < css.Length && index >= 0; index++)
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

        return css[start..];
    }
}
