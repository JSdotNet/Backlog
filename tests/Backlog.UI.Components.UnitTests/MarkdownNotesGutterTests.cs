using System.Text.RegularExpressions;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Where the margin layout's remarks sit relative to the file they are about.
///
/// <para>The column was there from the start and in the right place — second track
/// of the block's row, level with its block. What was wrong was everything around
/// it: it sat inside the reading body's own padding and behind its scrollbar, with
/// nothing drawn between it and the prose, so a remark read as a box someone had
/// put in the middle of the document rather than as a note in its margin. Measured
/// on the storybook at 1600px the body's content box ran 314→1506 with the track at
/// 1250→1506 and then 16px of padding and 10px of scrollbar outside it — the
/// remarks were the only thing in the pane that stopped short of the edge.</para>
///
/// <para>Asserted against the stylesheet for the reason
/// <c>FileViewHeaderLayoutTests</c> gives: the markup was right, the defect was
/// entirely in what the stylesheet did with it, and bUnit brings no layout engine.
/// Which class the body wears is a render, because that part <em>is</em> markup.</para>
/// </summary>
public sealed class MarkdownNotesGutterTests
{
    private const string Body = """
        # Storage

        Everything is a file on disk.

        The database is a cache.
        """;

    /// <summary>
    /// The reading body says when it is carrying a gutter, so the stylesheet can
    /// hand that edge over to it.
    ///
    /// <para>A class and not <c>:has()</c>. The margin layout is already a decision
    /// this component makes and records — see
    /// <c>FileContent.BodyCommentLayout</c>, which falls back to inline when there
    /// is nothing to put in the margin — so the fact is known here, and saying it
    /// in the class list keeps the stylesheet reading forwards instead of
    /// interrogating its own descendants.</para>
    /// </summary>
    [Fact]
    public void The_reading_body_says_when_it_is_carrying_a_gutter()
    {
        using var context = new BunitContext();

        var annotated = context.Render<FileContent>(parameters => parameters
            .Add(v => v.Body, Body)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "Which cache?") }));

        Assert.Contains("file-view__body--notes", annotated.Find(".file-view__body").ClassList);

        // And says nothing when there is nothing in the margin: the fallback to
        // inline is what decides, so the two cannot disagree about whether a gutter
        // is being drawn.
        var bare = context.Render<FileContent>(parameters => parameters
            .Add(v => v.Body, Body));

        Assert.DoesNotContain("file-view__body--notes", bare.Find(".file-view__body").ClassList);

        // Nor when a host asked for the remarks under the prose instead.
        var inline = context.Render<FileContent>(parameters => parameters
            .Add(v => v.Body, Body)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "Which cache?") })
            .Add(v => v.CommentLayout, MarkdownCommentLayout.Inline));

        Assert.DoesNotContain("file-view__body--notes", inline.Find(".file-view__body").ClassList);
    }

    /// <summary>
    /// The class order the edit body's test pins holds for this one too: the base
    /// class first, the modifier after. Asserted because the two variants are
    /// mutually exclusive and a body that wore both would be a body that is
    /// somehow editing a margin.
    /// </summary>
    [Fact]
    public void A_body_being_edited_draws_no_gutter()
    {
        using var context = new BunitContext();

        var view = context.Render<FileContent>(parameters => parameters
            .Add(v => v.Body, Body)
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "Which cache?") })
            .Add(v => v.ShowsEditBody, true)
            .Add(v => v.SuppliedBody, (RenderFragment)(builder => builder.AddMarkupContent(0, "<textarea></textarea>"))));

        var classes = view.Find(".file-view__body").ClassName!;

        Assert.StartsWith("file-view__body", classes, StringComparison.Ordinal);
        Assert.Contains("file-view__body--edit", classes, StringComparison.Ordinal);
        Assert.DoesNotContain("file-view__body--notes", classes, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pane gives the gutter its trailing edge rather than keeping it as padding
    /// the remarks then sit inside of.
    ///
    /// <para>This is the whole of what "to the right of the file content" asked for.
    /// The prose keeps its own padding on the side it has always had it; the
    /// trailing padding becomes the gutter's, spent inside the column where it
    /// separates the remark from the rule on one side and the scrollbar on the
    /// other.</para>
    /// </summary>
    [Fact]
    public void The_gutter_reaches_the_panes_trailing_edge()
    {
        // The pane gives that edge away, and the body spends what the pane says —
        // one value, because the header measures its own trailing edge from it too.
        Assert.Contains(
            "--file-view-content-end: 0px",
            Rule(".file-view:has(> .file-view__body--notes) {"),
            StringComparison.Ordinal);

        var body = Rule(".file-view__body--notes");

        Assert.True(
            Regex.IsMatch(body, @"padding-inline-end\s*:\s*0"),
            "The remarks stopped short of the pane by the body's own trailing padding, which is why "
            + "they read as content set into the text rather than as a margin. The padding moves into "
            + $"the column. Rule found:\n{body}");

        var notes = Rule(".md-view--margin .md-block-row__notes");

        Assert.Contains("padding-inline", notes, StringComparison.Ordinal);
    }

    /// <summary>
    /// Something is drawn between the prose and the remarks.
    ///
    /// <para>The column was the only thing saying these were asides, which is what
    /// the inline layout's rule down the left of a remark says explicitly. Between
    /// two columns of a grid with a gap and no rule there was nothing to say it at
    /// all — the remark's own border was the only edge in the pane, and an edge
    /// around a box in the middle of a document reads as a callout rather than as a
    /// margin note.</para>
    ///
    /// <para>The rule is the cell's, drawn once per block row, so it needs the rows
    /// to meet: a gap between them, or a cell sized to its content rather than to
    /// its row, and the line arrives in pieces with a gap against every short
    /// remark. Hence no gap and a stretched cell, with the separation spent as
    /// padding inside the column instead.</para>
    /// </summary>
    [Fact]
    public void A_rule_separates_the_gutter_from_the_prose()
    {
        var notes = Rule(".md-view--margin .md-block-row__notes");

        Assert.True(
            Regex.IsMatch(notes, @"border-inline-start\s*:"),
            $"Nothing was drawn between the prose and the remarks beside it. Rule found:\n{notes}");

        var row = Rule(".md-view--margin .md-block-row");

        Assert.True(
            Regex.IsMatch(row, @"align-items\s*:\s*stretch"),
            "A cell sized to its own content leaves the rule short of the next row, so the line "
            + $"between the columns arrives in dashes. Rule found:\n{row}");
        Assert.True(
            Regex.IsMatch(row, @"gap\s*:\s*0"),
            "A gap between the two tracks is a gap in the rule as well — the cell's border is at the "
            + $"cell's edge, and the gap is outside it. Rule found:\n{row}");
    }

    /// <summary>
    /// The prose keeps the measure it had before the gutter was given the edge, and
    /// the remark keeps its width.
    ///
    /// <para>The column grew by exactly what it now spends on the rule and the
    /// padding either side of it — 2rem, 16rem to 18rem — and in the file pane that
    /// 2rem is paid for: 1rem is the trailing padding the body hands over, 1rem is
    /// the leading padding the prose was already keeping off it. Measured on the
    /// storybook at 1600px the prose is 920px on both sides of the change. A gutter
    /// that read better at the cost of a narrower document would have traded one
    /// complaint for another.</para>
    ///
    /// <para>One length and not a property a caller tunes. Both surfaces that draw
    /// this call it a margin, and a margin that is one width in a file pane and
    /// another on a document page is the same thing drawn two sizes — a remark
    /// would change width as a reader moved between them.</para>
    /// </summary>
    [Fact]
    public void Making_the_gutter_legible_costs_the_prose_nothing()
    {
        Assert.True(
            Regex.IsMatch(Rule(".file-view:has(> .file-view__body--notes) {"), @"--file-view-content-end\s*:\s*0px"),
            "The trailing padding is what the widened track is spending, so the pane has to give it "
            + "away.");

        var row = Rule(".md-view--margin .md-block-row");

        Assert.True(
            Regex.IsMatch(row, @"grid-template-columns\s*:\s*minmax\(0,\s*1fr\)\s*18rem"),
            "The track carries the rule and the padding either side of it now, so it is 2rem wider "
            + $"than the 16rem it was — or the remark inside it is narrower than it was. Rule found:\n{row}");
    }

    /// <summary>
    /// The narrow fallback takes the gutter's furniture with it.
    ///
    /// <para>Below the breakpoint the row stops being a grid and the notes cell
    /// becomes a block under its block, where the inline layout's own rule down the
    /// left is restored. A leftover border and padding from the column would draw a
    /// second rule beside that one, on the wrong side of it.</para>
    /// </summary>
    [Fact]
    public void The_narrow_fallback_puts_the_gutters_furniture_away()
    {
        var css = Stylesheet();
        var breakpoint = css.IndexOf("@media (max-width: 60rem)", StringComparison.Ordinal);

        Assert.True(breakpoint >= 0, "components.css no longer has the margin layout's narrow fallback.");

        var fallback = css[breakpoint..];
        var end = fallback.IndexOf("\n}", StringComparison.Ordinal);
        fallback = end >= 0 ? fallback[..end] : fallback;

        Assert.Contains(".md-block-row__notes", fallback, StringComparison.Ordinal);
        Assert.True(
            Regex.IsMatch(fallback, @"border-inline-start\s*:\s*none"),
            $"The column's rule survives into the stacked layout, beside the inline one. Found:\n{fallback}");
    }

    /// <summary>
    /// A pane carrying a gutter does not scroll inside itself.
    ///
    /// <para>A scroll box draws its bar at its own trailing edge and the gutter is
    /// inside it, so the bar landed beyond the remarks — which reads as the
    /// annotations being the thing that scrolls. There is no arrangement of one
    /// scroll box that puts the bar between the two columns: to be beside the bar
    /// the notes cell would have to be outside the box, and everything outside a
    /// scroll box is clipped by it.</para>
    ///
    /// <para>So the pane hands the scrolling up to whatever it sits in. Both
    /// surfaces in this product that draw remarks already relied on that —
    /// <c>Arc42KnowledgePanel</c> and <c>DomainKnowledgePanel</c> pass no
    /// <c>MaxHeight</c> and say so — and this makes it true wherever a margin is
    /// drawn rather than wherever the caller remembered.</para>
    /// </summary>
    [Fact]
    public void A_pane_carrying_a_gutter_hands_its_scrolling_up()
    {
        using var context = new BunitContext();

        var annotated = context.Render<FileContent>(parameters => parameters
            .Add(v => v.Body, Body)
            .Add(v => v.MaxHeight, "24rem")
            .Add(v => v.Comments, new MarkdownComment[] { new("c1", 1, "Which cache?") }));

        // The cap is never written, rather than withdrawn by a rule an inline
        // style would beat anyway.
        Assert.Null(annotated.Find(".file-view__body").GetAttribute("style"));

        // The same host without remarks keeps the cap it asked for.
        var plain = context.Render<FileContent>(parameters => parameters
            .Add(v => v.Body, Body)
            .Add(v => v.MaxHeight, "24rem"));

        Assert.Equal("max-height: 24rem", plain.Find(".file-view__body").GetAttribute("style"));

        var rule = Rule(".file-view__body--notes");

        Assert.Contains("overflow: visible", rule, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pane stops painting itself where the gutter starts, so the remarks read
    /// as outside the file rather than as a column of it.
    ///
    /// <para>The card is repainted, not rebuilt: its border, ground and corners
    /// come off the element and onto a layer inset by the track's own width, so
    /// the trailing border lands exactly where the notes cell begins. The remarks
    /// stay the second cell of their block's row, which is what keeps each level
    /// with its block for nothing — genuinely moving them out would leave their
    /// offsets and their scrolling to be measured in script on every resize and
    /// every edit.</para>
    /// </summary>
    [Fact]
    public void The_pane_stops_painting_itself_where_the_gutter_starts()
    {
        var pane = Rule(".file-view:has(> .file-view__body--notes) {");

        Assert.Contains("border-color: transparent", pane, StringComparison.Ordinal);
        Assert.Contains("background: transparent", pane, StringComparison.Ordinal);

        var card = Rule(".file-view:has(> .file-view__body--notes)::before");

        Assert.True(
            Regex.IsMatch(card, @"inset\s*:\s*0\s+18rem\s+0\s+0"),
            "The card has to stop at the track the remarks occupy, or it is painted under them and "
            + $"they are inside the pane again. Rule found:\n{card}");
        Assert.Contains("border: var(--border-width) solid var(--color-border)", card, StringComparison.Ordinal);
        Assert.Contains("background: var(--color-background)", card, StringComparison.Ordinal);

        // And the gutter's own rule goes, or the card's trailing border and it are
        // two lines a pixel apart.
        Assert.Contains(
            "border-inline-start: none",
            Rule(".file-view__body--notes .md-view--margin .md-block-row__notes"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The header's own rule ends at the card's edge too, and stops reserving the
    /// track the moment there is no gutter to reserve it for.
    ///
    /// <para>A margin and not padding: padding would keep the header's box — and
    /// the border under it — running the full width of the pane and straight across
    /// the gutter, which is the line the repaint above exists not to draw.</para>
    ///
    /// <para>Below the breakpoint there is no second column, and a header still
    /// holding 18rem clear is most of the header given to nothing: at 560px it left
    /// the name and the status fighting over what was left while half the line
    /// stood empty.</para>
    /// </summary>
    [Fact]
    public void The_header_reserves_the_gutter_only_while_there_is_one()
    {
        const string Selector = ".file-view:has(> .file-view__body--notes) > .file-view__header";

        // Two rules wear this selector — the reservation and its withdrawal — so
        // both are read rather than whichever the stylesheet happens to state
        // first.
        var rules = Rules(Selector);

        Assert.Equal(2, rules.Count);
        Assert.True(
            Regex.IsMatch(rules[0], @"margin-inline-end\s*:\s*18rem"),
            "The header ran on over the gutter, so its own bottom rule crossed the remarks. A margin "
            + $"and not padding, or the box still spans the pane. Rule found:\n{rules[0]}");

        // And the withdrawal is inside the breakpoint the layout itself falls back
        // at, not merely somewhere later in the file.
        var css = Stylesheet();
        var withdrawal = css.LastIndexOf(Selector, StringComparison.Ordinal);
        var breakpoint = css.LastIndexOf("@media (max-width: 60rem)", withdrawal, StringComparison.Ordinal);

        // The withdrawal opens that media block: nothing but whitespace stands
        // between the block's brace and this selector. Which is what says the rule
        // is inside the breakpoint rather than merely somewhere after one.
        Assert.True(
            breakpoint >= 0
            && string.IsNullOrWhiteSpace(css[(css.IndexOf('{', breakpoint) + 1)..withdrawal]),
            "The reservation has to be withdrawn at the same width the gutter collapses at, or the "
            + "header holds 18rem clear for a column that is not drawn.");
        Assert.True(
            Regex.IsMatch(rules[1], @"margin-inline-end\s*:\s*0"),
            $"The withdrawal has to take the reservation back. Rule found:\n{rules[1]}");

        // And the pane takes back the trailing edge it gave the gutter, which the
        // header's own padding is measured from.
        // Matched with the brace, so the ::before that paints the card is not
        // mistaken for the pane's own rule.
        Assert.Contains(
            "--file-view-content-end: var(--spacing-md)",
            Rules(".file-view:has(> .file-view__body--notes) {")[1],
            StringComparison.Ordinal);
    }

    /// <summary>Every rule that opens with <paramref name="selector"/>, in the
    /// order the stylesheet states them. For a selector a stylesheet uses twice —
    /// a declaration and its withdrawal under a media query — where
    /// <see cref="Rule"/> would silently answer with the first.</summary>
    private static List<string> Rules(string selector)
    {
        var css = Stylesheet();
        var found = new List<string>();

        for (var at = css.IndexOf(selector, StringComparison.Ordinal); at >= 0;
             at = css.IndexOf(selector, at + selector.Length, StringComparison.Ordinal))
        {
            var depth = 0;

            for (var index = css.IndexOf('{', at); index >= 0 && index < css.Length; index++)
            {
                if (css[index] == '{')
                {
                    depth++;
                }
                else if (css[index] == '}' && --depth == 0)
                {
                    found.Add(css[at..(index + 1)]);
                    break;
                }
            }
        }

        Assert.NotEmpty(found);
        return found;
    }

    /// <summary>The rule that opens with <paramref name="selector"/>, braces matched
    /// so a nested block cannot end it early — the same helper
    /// <c>FileViewHeaderLayoutTests</c> uses, and duplicated for the reason the
    /// other stylesheet tests in this project duplicate it: each one asserts on a
    /// different section and a shared helper would be a third place to look.</summary>
    private static string Rule(string selector)
    {
        var css = Stylesheet();
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

    /// <summary>The library's stylesheet with its comments stripped and its line
    /// endings normalised. The comments go because the rules here argue for
    /// themselves at length and name each other while doing it, so a selector quoted
    /// in prose would be found instead of the rule.</summary>
    private static string Stylesheet() =>
        Regex.Replace(
            File.ReadAllText(RepositoryRoot.File(
                "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css")).Replace("\r\n", "\n"),
            @"/\*.*?\*/",
            string.Empty,
            RegexOptions.Singleline);
}
