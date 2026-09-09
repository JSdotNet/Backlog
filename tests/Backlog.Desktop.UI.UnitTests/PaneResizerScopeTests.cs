namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A rule about a pane resizer has to say whose resizer it is.
/// <para>
/// This app draws two of them and they are the same class. The knowledge layout
/// renders its own <c>.pane-resizer</c> between the backlog and the side stack
/// (<c>Home.razor</c>), and every <c>SplitPane</c> the shared library renders puts
/// that class on its separator too — the resizer's styling is one block in
/// <c>components.css</c> and its pointer drag is one handler keyed off
/// <c>data-pane-resizer</c>, so sharing the name is the point.
/// </para>
/// <para>
/// Which makes an unscoped selector here a rule about both. The knowledge layout's
/// stacking step at 72rem carried a bare <c>.pane-resizer { display: none }</c> —
/// correct for its own edge, which has nothing vertical left to drag once the panes
/// stack, and wrong for the backlog split, which does not stack until 60rem and is
/// still a side-by-side grid for the whole 12rem in between.
/// </para>
/// <para>
/// What that cost, measured in the harness at a 1024x900 window with an entry open:
/// the separator was <c>display: none</c>, so it stopped being a grid item, and
/// auto-placement moved the entry panel into the 8px track the separator had left
/// behind. The split resolved to <c>624px 172px 172px</c> — the list pinned at its
/// 39rem floor, the panel crushed to 172px against 224px of content, its title
/// rendering one letter per line, and the third track, the panel's own, standing
/// empty at the right edge of the window. The reader could not drag any of it back,
/// because the thing you drag was the thing that had gone.
/// </para>
/// <para>
/// So the rule is structural rather than a number: reach a resizer through the
/// layout that owns it. <c>SplitPaneColumnTests</c> holds the other half — that the
/// split's panes name their own columns, so a separator hidden by anything at all
/// cannot move them again.
/// </para>
/// </summary>
public sealed class PaneResizerScopeTests
{
    [Fact]
    public void Every_rule_about_a_resizer_names_the_layout_that_owns_it()
    {
        var unscoped = Selectors(WithoutComments(Css()))
            .Where(Opens)
            .ToList();

        Assert.True(
            unscoped.Count == 0,
            "A selector that opens on `.pane-resizer` is a rule about every resizer in the app, "
            + "including the separator of every SplitPane the library renders. Reach the intended "
            + "one through its layout instead. Unscoped: " + string.Join("; ", unscoped));
    }

    /// <summary>
    /// Whether a selector starts at the resizer itself. <c>.pane-resizer__grip</c>
    /// does not: it is a different class that happens to share a prefix, it names a
    /// part rather than the element, and a rule about it is already inside whatever
    /// rule put the resizer there.
    /// </summary>
    private static bool Opens(string selector) =>
        System.Text.RegularExpressions.Regex.IsMatch(selector, @"^\.pane-resizer(?![\w-])");

    /// <summary>
    /// Every selector in the stylesheet, one per comma, with whitespace flattened.
    /// <para>
    /// A selector list is the text between the end of the previous block or
    /// declaration and the <c>{</c> that opens this one. At-rules open a block the
    /// same way and are skipped by their leading <c>@</c>, which also skips their
    /// nesting: the rules inside a media query are found by the same scan, since
    /// what precedes them is a <c>{</c> just like anywhere else.
    /// </para>
    /// </summary>
    private static IEnumerable<string> Selectors(string css)
    {
        var cursor = 0;

        while (cursor < css.Length)
        {
            var open = css.IndexOf('{', cursor);
            if (open < 0)
            {
                yield break;
            }

            var previous = open == 0 ? -1 : css.LastIndexOfAny(['{', '}', ';'], open - 1);
            var text = Flatten(css[(previous + 1)..open]);
            cursor = open + 1;

            if (text.Length == 0 || text.StartsWith('@'))
            {
                continue;
            }

            foreach (var selector in text.Split(','))
            {
                var trimmed = selector.Trim();
                if (trimmed.Length > 0)
                {
                    yield return trimmed;
                }
            }
        }
    }

    /// <summary>
    /// The stylesheet with its comments taken out, because this file explains itself
    /// in prose that quotes CSS — braces, semicolons and selectors and all — and a
    /// scan that reads those as rules would report a rule nobody wrote.
    /// </summary>
    private static string WithoutComments(string css)
    {
        var kept = new System.Text.StringBuilder(css.Length);
        var at = 0;

        while (at < css.Length)
        {
            var start = css.IndexOf("/*", at, StringComparison.Ordinal);
            if (start < 0)
            {
                kept.Append(css[at..]);
                break;
            }

            kept.Append(css[at..start]);

            var end = css.IndexOf("*/", start + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                break;
            }

            // A newline in place of the comment, so two rules either side of one do
            // not run together into a single selector.
            kept.Append('\n');
            at = end + 2;
        }

        return kept.ToString();
    }

    /// <summary>Runs of whitespace down to one space: a selector split over three
    /// lines is one selector.</summary>
    private static string Flatten(string text) => string.Join(
        ' ',
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Css() => File.ReadAllText(
        RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css"))
        .Replace("\r\n", "\n");
}
