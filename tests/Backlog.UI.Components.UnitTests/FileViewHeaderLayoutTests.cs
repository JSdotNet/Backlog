using System.Text.RegularExpressions;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What the file pane's header does with the things that fight for its width: the
/// name's line — the name, the path right-aligned in what is left of it and the
/// status closing it — and the actions stacked opposite.
///
/// <para>The layout half is asserted against the stylesheet rather than a render,
/// for the reason <c>DesignTokenTests</c> gives for the same shape of test: the
/// markup was always right — the path wore the class it should — and the defect was
/// entirely in what the stylesheet did with it. bUnit brings no layout engine, so a
/// render test can only confirm the class is present, which it already was. The
/// order the header reads in <em>is</em> markup, so that half is a render.</para>
/// </summary>
public sealed class FileViewHeaderLayoutTests
{
    /// <summary>A knowledge file as the convention writes one: the title with the
    /// file's own record under it, which is what puts a status in the header at
    /// all.</summary>
    private const string Knowledge = """
        # Shared Technologies

        ```meta
        status: adopted
        ```

        What the technologies are.
        """;

    /// <summary>
    /// The name's line reads name, path, status — and the three are siblings of one
    /// row, which is what makes that order the header's rather than a coincidence of
    /// which column each landed in.
    ///
    /// <para>The status is what fixes it. It is drawn inside the record, held to the
    /// right edge of the row the name is on, so anything that is to precede it has
    /// to be an item of that row. The path was in the aside opposite, where it could
    /// only ever follow; it then led the identity column as a line of its own above
    /// the name, which put it before the status but took it off the name's line
    /// altogether. It is now the row's flexible middle.</para>
    /// </summary>
    [Fact]
    public void The_name_then_the_path_then_the_status_are_one_row()
    {
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "shared-technologies.md")
            .Add(v => v.Path, ".tech/shared-technologies.md")
            .Add(v => v.Body, Knowledge)
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.KnowledgeFolder, KnowledgeFolder.Tech));

        var header = view.Find(".file-view__header");

        // All three have to be there, or the order asserted here is the order of
        // fewer elements and passes while proving nothing.
        Assert.Equal(["name", "path", "status"], header.QuerySelectorAll(Parts).Select(Role).ToList());

        // Siblings of the record's headline, and not three things in three places
        // that happen to read left to right. The row is what holds the alignment:
        // the status takes its right edge, and the path is the item that grows into
        // whatever the name leaves.
        var row = view.Find(".file-view__record .knowledge-record__headline");

        Assert.Equal(["name", "path", "status"], row.Children.Select(Role).ToList());
    }

    /// <summary>
    /// The same line with no record at all, which is the case a plain file's header
    /// is in.
    ///
    /// <para>There is no headline row then — <c>MetadataFileView</c> draws the
    /// heading and nothing around it — so the identity column holds those same items
    /// directly, and the stylesheet makes the column the row. The path still ends up
    /// on the name's line, right-aligned, rather than reverting to a line of its own
    /// above it.</para>
    /// </summary>
    [Fact]
    public void The_path_is_still_on_the_names_line_with_no_status_at_all()
    {
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "notes.md")
            .Add(v => v.Path, "docs/notes.md")
            .Add(v => v.Body, "# Notes\n\nNothing says what state this is in."));

        var identity = view.Find(".file-view__identity");

        Assert.Empty(view.FindAll(".badge--status"));
        Assert.Empty(identity.QuerySelectorAll(".knowledge-record"));

        // The column is the row: the name's wrapper and the path, in that order,
        // with nothing between them and nothing wrapping either.
        Assert.Equal(["name", "path"], identity.Children.Select(Role).ToList());
        Assert.Equal("docs/notes.md", identity.QuerySelector(".file-view__path")!.TextContent);
        Assert.Equal("notes.md", identity.QuerySelector(".file-view__headline .file-view__name")!.TextContent);

        // And the stylesheet turns exactly that column into a row. Scoped to the
        // column without a record: the one with a record has the record's own
        // headline for the job, and has to stay a block around it.
        var row = Rule(".file-view__identity:not(.file-view__identity--record)");

        Assert.Contains("display: flex", row, StringComparison.Ordinal);
        Assert.Contains("align-items: baseline", row, StringComparison.Ordinal);
    }

    /// <summary>
    /// The regression the cap in the aside was written for, and why it is still
    /// needed now the path has left.
    ///
    /// <para><c>.file-view__aside</c> stacks its children, so a child's
    /// <c>flex-shrink</c> and its <c>min-width: 0</c> govern the main axis — which
    /// is vertical here — and neither of them touches its width. Width comes off
    /// the cross axis instead, as fit-content, and fit-content is never smaller
    /// than min-content. The path was what found that: for a
    /// <c>white-space: nowrap</c> string min-content is the whole string, so it
    /// kept its full width inside a column a third of it, overflowed leftwards out
    /// of the card, and painted over the file's own name while the ellipsis it
    /// declares never engaged.</para>
    ///
    /// <para>The path is in the identity column now, where a block fills its
    /// containing block and the cap is not needed. The action group and the
    /// baseline picker still stack in the aside, and both are rows of controls that
    /// would reach past the column that holds them, so the rule stays — for the
    /// things still in it rather than for the one that left.</para>
    /// </summary>
    [Fact]
    public void Nothing_stacked_in_the_aside_may_grow_wider_than_the_column_it_was_given()
    {
        var rule = Rule(".file-view__aside > *");

        Assert.True(
            Regex.IsMatch(rule, @"max-width\s*:\s*100%"),
            "The actions and the baseline picker are stacked children of .file-view__aside, so "
            + "flex-shrink and min-width: 0 govern their height and their width is fit-content — never "
            + "below their own min-content, which for a row of buttons is the whole row. Without a "
            + "max-width the box keeps that width, spills out of the column and reaches back across the "
            + $"header. Rule found:\n{rule}");
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

    /// <summary>
    /// What gives the ellipsis something to engage against, and what holds the path
    /// against the status rather than against the name.
    ///
    /// <para>The path is the one item of the line that grows, so it takes whatever
    /// the name leaves and its <c>text-align: right</c> — the shared declaration,
    /// which the lead-line arrangement had to override and no longer does — puts the
    /// text at the far end of that space, next to the status. Growing is also what
    /// spends the free space the status's own <c>margin-inline-start: auto</c> would
    /// otherwise have taken, which is why the two end up adjacent.</para>
    ///
    /// <para>Truncation then needs the item to be allowed below its content.
    /// Without <c>min-width: 0</c> a flex item refuses to go under its own
    /// min-content, which for a nowrap path is the whole string, and the header
    /// stops truncating and starts growing instead — so the declaration is asserted
    /// on the identity column and on the path itself. The lead-line rule that
    /// left-aligned it is gone rather than overridden: two rules arguing about the
    /// same property is how the arrangement stopped being legible.</para>
    /// </summary>
    [Fact]
    public void The_path_grows_into_the_space_the_name_leaves_and_truncates_there()
    {
        Assert.Contains("min-width: 0", Rule(".file-view__identity,"), StringComparison.Ordinal);

        var shared = Rule(".file-view__path,");

        Assert.Contains("min-width: 0", shared, StringComparison.Ordinal);
        Assert.Contains("text-align: right", shared, StringComparison.Ordinal);

        // Both rows the file pane's path can be an item of: the record's headline,
        // and the identity column itself when there is no record to draw one.
        var grows = Rule(".file-view__identity > .file-view__path,");

        Assert.Contains(".knowledge-record__headline > .file-view__path", grows, StringComparison.Ordinal);
        Assert.True(
            Regex.IsMatch(grows, @"flex\s*:\s*1\s+1\s"),
            "The path is the line's flexible middle: it has to grow into what the name leaves, or "
            + "right-aligning it lands it against the name and the status keeps the free space "
            + $"instead. Rule found:\n{grows}");

        // And nothing left behind to argue with it.
        var css = Stylesheet();

        Assert.DoesNotContain(".file-view__identity > .file-view__path {", css, StringComparison.Ordinal);
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

    /// <summary>
    /// The header is two lines, and the second one is shared.
    ///
    /// <para>It read as two for as long as the identity column held one line. The
    /// detail line — source, kind, size — is drawn under the name inside that
    /// column, so a file that states any of the three had a third line, and the acts
    /// were pushed onto a fourth. The stylesheet said two the whole time.</para>
    ///
    /// <para>So the details leave the identity column and become the left of a line
    /// they share with the aside. Both belong there for the same reason: neither is
    /// what the file <em>is</em>, which is the first line's business, and the eye
    /// that has finished the name lands on the two of them at once rather than on
    /// two lines of trailing matter.</para>
    /// </summary>
    [Fact]
    public void The_details_and_the_acts_share_the_headers_second_line()
    {
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "shared-technologies.md")
            .Add(v => v.Path, ".tech/shared-technologies.md")
            .Add(v => v.Kind, "Technology stack")
            .Add(v => v.Body, Knowledge)
            .Add(v => v.AllowCopy, true)
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.KnowledgeFolder, KnowledgeFolder.Tech));

        var header = view.Find(".file-view__header");

        // Two children and no third: the identity line, then the line the details
        // and the acts share. A header that grew a line would show up here as a
        // list of three.
        Assert.Equal(
            ["file-view__identity", "file-view__summary"],
            header.Children.Select(child => child.ClassList.First(name => name.StartsWith("file-view__", StringComparison.Ordinal))).ToList());

        // And the two are siblings of that line, in reading order, rather than one
        // of them still being a line of its own.
        var summary = view.Find(".file-view__summary");

        Assert.Equal(["file-view__meta", "file-view__aside"], summary.Children.Select(child => child.ClassName).ToList());

        // The details are out of the identity column, which is what made the third
        // line. Left behind, they would still draw one.
        Assert.Empty(view.Find(".file-view__identity").QuerySelectorAll(".file-view__meta"));
    }

    /// <summary>
    /// The line holds its two ends apart, and the acts keep the right one even when
    /// there are no details to hold the left.
    ///
    /// <para><c>space-between</c> alone would put a lone aside at the start of the
    /// line, under the name, which is the one thing the second line is not for. The
    /// auto margin is what makes the right edge the aside's regardless — the same
    /// device the status badge uses to close the line above it.</para>
    /// </summary>
    [Fact]
    public void The_acts_hold_the_lines_right_edge_with_or_without_details()
    {
        var line = Rule(".file-view__summary");

        Assert.Contains("display: flex", line, StringComparison.Ordinal);
        Assert.Contains("align-items: baseline", line, StringComparison.Ordinal);
        Assert.Contains("min-width: 0", line, StringComparison.Ordinal);

        var aside = Rule(".file-view__aside");

        Assert.True(
            Regex.IsMatch(aside, @"margin-inline-start\s*:\s*auto"),
            "With no details the aside is the line's only item, and a line that only spreads its "
            + "items apart has nothing to spread — the acts would sit at the start of it, under the "
            + $"name. Rule found:\n{aside}");

        // A row now, not a column: the picker beside the acts is what keeps the
        // comparison case on two lines instead of a third.
        Assert.True(
            Regex.IsMatch(aside, @"flex-direction\s*:\s*row"),
            $"The aside stacked its children, which is a third line the moment both show. Rule found:\n{aside}");
    }

    /// <summary>
    /// The picker joins the acts on that line rather than dropping under them.
    ///
    /// <para>The aside held two stacked children — the acts, and the baseline picker
    /// under the toggle that summons it — so a file being compared against more than
    /// one version drew four lines of header. Both are things you may do to the file
    /// and both are right-aligned, so they read as one trailing group on one
    /// line.</para>
    /// </summary>
    [Fact]
    public void Comparing_against_several_baselines_does_not_buy_a_third_line()
    {
        using var context = new BunitContext();

        var view = context.Render<FileHeader>(parameters => parameters
            .Add(v => v.Name, "context-map.md")
            .Add(v => v.Path, ".domain/context-map.md")
            .Add(v => v.Kind, "Domain context map")
            .Add(v => v.OffersCompare, true)
            .Add(v => v.Comparing, true)
            .Add(v => v.CompareBaselines, new List<FileCompareBaseline>
            {
                new("head", "Last commit"),
                new("disk", "On disk")
            })
            .Add(v => v.SelectedBaselineId, "head"));

        var header = view.Find(".file-view__header");

        Assert.Equal(2, header.Children.Length);

        // Both groups, and both inside the one line.
        var aside = view.Find(".file-view__summary > .file-view__aside");

        Assert.NotNull(aside.QuerySelector(".file-view__actions"));
        Assert.NotNull(aside.QuerySelector(".file-view__baselines"));
    }

    /// <summary>
    /// Neither group of controls may wrap, because wrapping is the other way a line
    /// becomes two.
    ///
    /// <para>They wrapped by choice, back when the aside was a column and a group
    /// reaching past it was the worse outcome. On one shared line the cap the aside
    /// already puts on its children plus this is what holds the count at two: the
    /// controls are a handful of 2rem marks, so what a narrow pane takes off the
    /// line is the detail text beside them.</para>
    /// </summary>
    [Fact]
    public void A_narrow_header_shortens_the_details_rather_than_wrapping_the_controls()
    {
        var groups = Rule(".file-view__actions,");

        Assert.Contains(".file-view__baselines", groups, StringComparison.Ordinal);
        Assert.True(
            Regex.IsMatch(groups, @"flex-wrap\s*:\s*nowrap"),
            $"A group that wraps is a second line of aside, which is a third line of header. Rule found:\n{groups}");

        // What gives instead. The details are the line's flexible item and the one
        // with text to lose, so they truncate the way the path above them does.
        var details = Rule(".file-view__meta,");

        Assert.Contains("min-width: 0", details, StringComparison.Ordinal);

        var truncates = Rule(".file-view__summary .file-view__meta");

        Assert.Contains("white-space: nowrap", truncates, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden", truncates, StringComparison.Ordinal);
        Assert.Contains("text-overflow: ellipsis", truncates, StringComparison.Ordinal);
    }

    /// <summary>
    /// The name is the last thing that could still buy a line, and the one nobody
    /// thinks to check: the shared heading rule breaks it anywhere it has to, so a
    /// long file name in a narrow pane wrapped to two, three, as many lines as it
    /// took, and the cap the rest of this fixes went with it.
    ///
    /// <para>Truncated visually and whole in the DOM, which is the bargain the path
    /// beside it already struck — see
    /// <see cref="The_whole_path_is_in_the_DOM_however_little_of_it_is_drawn"/>. The
    /// pane's own heading is the one place that is safe: <c>FolderView</c> and
    /// <c>MarkdownCompare</c> share the rule this overrides and neither promises a
    /// line count, so the override is scoped to this header rather than taken out of
    /// the rule all three read.</para>
    /// </summary>
    [Fact]
    public void A_long_name_truncates_instead_of_wrapping_the_first_line()
    {
        const string name = "0002-backlog-module-owns-the-entry-text-language.md";

        var rule = Rule(".file-view__name {");

        Assert.Contains("white-space: nowrap", rule, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden", rule, StringComparison.Ordinal);
        Assert.Contains("text-overflow: ellipsis", rule, StringComparison.Ordinal);

        // The shared rule still breaks a folder's or a comparison's name where it
        // has to. Only the file pane caps its header.
        var shared = Rule(".file-view__name,");

        Assert.Contains(".folder-view__name", shared, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere", shared, StringComparison.Ordinal);

        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, name));

        var rendered = view.Find(".file-view__name");

        Assert.Equal(name, rendered.TextContent);
        Assert.False(rendered.HasAttribute("title"));
    }

    /// <summary>
    /// The file's status and the chapters' statuses are one column.
    ///
    /// <para>The file's is drawn at the end of the header's first line and a
    /// chapter's at the end of its own heading in the body, one under the other,
    /// and a reader takes them as one column of the same answer. They were not one:
    /// a block that offers a remark holds its trailing corner clear for the
    /// affordance — <c>2.5rem</c> a slot plus <c>--spacing-sm</c> — so every
    /// chapter's status stopped 48px short of where the file's did. Measured on the
    /// storybook: the header's at 1244, the body's at 1196.</para>
    ///
    /// <para>Two terms and not one number, because they move for different reasons.
    /// The body's own trailing padding is given away entirely when a gutter of
    /// remarks takes that edge, and the affordance reserve depends on how many
    /// controls a block offers — two for a view that also rewrites, none at all for
    /// one nobody may annotate.</para>
    /// </summary>
    [Fact]
    public void The_files_status_and_the_chapters_statuses_are_one_column()
    {
        var header = Rule(".file-view > .file-view__header");

        Assert.True(
            Regex.IsMatch(header, @"padding-inline-end\s*:\s*calc\(\s*var\(--file-view-content-end\)\s*\+\s*var\(--file-view-affordance-end\)\s*\)"),
            "The header has to reserve what the body reserves, or the file's status and the chapters' "
            + $"stop at different places. Rule found:\n{header}");

        // Both terms have a default, so a pane whose body reserves nothing gets an
        // ordinary header rather than one holding space for a control that is not
        // drawn.
        var pane = Rule(".file-view {");

        Assert.Contains(
            "--file-view-content-end: calc(var(--spacing-md) + var(--file-view-scrollbar))",
            pane,
            StringComparison.Ordinal);
        Assert.Contains("--file-view-affordance-end: 0px", pane, StringComparison.Ordinal);

        // The scrollbar term is the width the pane's own scrollbar is given, read
        // from the same property rather than restated — being out by exactly that
        // track is what left every unannotated file's status a step off its
        // chapters'.
        Assert.Contains(
            "width: var(--file-view-scrollbar",
            Rule(".file-view__body::-webkit-scrollbar,"),
            StringComparison.Ordinal);

        // And the affordance term is the block's own reserve, restated in the same
        // units the block uses — one slot, or two when a view offers a rewrite too.
        Assert.Contains(
            "calc(2.5rem + var(--spacing-sm))",
            Rule(".file-view:has(.md-block--affordance)"),
            StringComparison.Ordinal);
        Assert.Contains(
            "calc(2 * 2.5rem + var(--spacing-sm))",
            Rule(".file-view:has(.md-block--affordance-pair)"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The three acts are dressed alike.
    ///
    /// <para>Comparing is a toggle, and a toggle's own base is the full button —
    /// the wider padding, and a tinted, bordered box once it is pressed. Between
    /// two plain glyphs that drew a raised control, and the row read as one act
    /// that mattered more than the others rather than as three things you may do to
    /// a file. It takes <c>IconButton</c>'s base instead, which is what Copy and
    /// Edit already are.</para>
    ///
    /// <para>Only the paint changes: <c>aria-pressed</c> is on the control either
    /// way, so what a screen reader is told about the state is untouched — see
    /// <c>FileViewActionsTests</c>, which is where that is asserted.</para>
    /// </summary>
    [Fact]
    public void Copy_edit_and_compare_are_dressed_alike()
    {
        using var context = new BunitContext();

        var view = context.Render<FileHeader>(parameters => parameters
            .Add(v => v.Name, "domain.md")
            .Add(v => v.Body, "# Domain\n\nText.")
            .Add(v => v.AllowCopy, true)
            .Add(v => v.OffersEdit, true)
            .Add(v => v.OffersCompare, true)
            .Add(v => v.Comparing, true)
            .Add(v => v.TestId, "file"));

        var controls = view.FindAll(".file-view__actions .btn");

        Assert.Equal(3, controls.Count);

        // Every one of them an icon button, and none of them wearing the toggle's
        // own base — which is the class the pressed box hangs off.
        foreach (var control in controls)
        {
            Assert.Contains("btn--icon", control.ClassList);
            Assert.DoesNotContain("btn--toggle", control.ClassList);
        }

        // The state is still drawn, in the register the rest of the row uses.
        var compare = view.Find("[data-testid='file-compare']");

        Assert.Contains("file-view__action--on", compare.ClassList);
        Assert.Equal("true", compare.GetAttribute("aria-pressed"));

        // And nothing wears it while the comparison is off.
        var resting = context.Render<FileHeader>(parameters => parameters
            .Add(v => v.Name, "domain.md")
            .Add(v => v.OffersCompare, true)
            .Add(v => v.TestId, "file"));

        Assert.DoesNotContain("file-view__action--on", resting.Find("[data-testid='file-compare']").ClassList);

        // The on state is a colour and not a box, or it is the raised control
        // again under another name.
        var on = Rule(".file-view__action--on");

        Assert.Contains("color: var(--color-primary)", on, StringComparison.Ordinal);
        Assert.DoesNotContain("background", on, StringComparison.Ordinal);
        Assert.DoesNotContain("border", on, StringComparison.Ordinal);
    }

    /// <summary>Every part of the name's line, in one selector, so a missing one
    /// shortens the list a test compares rather than passing unnoticed. The status
    /// has two forms — a badge when nothing offers a vocabulary, a select when
    /// something does — and both are the status.</summary>
    private const string Parts =
        ".file-view__headline, .file-view__path, .badge--status, .status-editor";

    /// <summary>Which part of the line an element is, named as the reading order
    /// names it. Asserting on roles rather than on class names keeps the expectation
    /// legible as a sentence: name, path, status.</summary>
    private static string Role(AngleSharp.Dom.IElement element) =>
        element.ClassList.Contains("file-view__headline") ? "name"
        : element.ClassList.Contains("file-view__path") ? "path"
        : "status";

    /// <summary>The rule that opens with <paramref name="selector"/>, braces matched
    /// so a nested block cannot end it early. Asserted rather than returned empty:
    /// a selector that no longer exists has to fail here as a rule this test cannot
    /// find, not further down as a green run against nothing.
    ///
    /// <para>Comments are stripped first. The rules in this stylesheet argue for
    /// themselves at length and name each other while doing it, so a selector
    /// quoted in prose above a rule would otherwise be found instead of the rule —
    /// which is exactly what happened when the path's own move was written up in a
    /// comment mentioning <c>.file-view__aside &gt; *</c>.</para></summary>
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
    /// endings normalised — see <see cref="Rule"/> for why the comments go.</summary>
    private static string Stylesheet() =>
        Regex.Replace(
            File.ReadAllText(RepositoryRoot.File(
                "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css")).Replace("\r\n", "\n"),
            @"/\*.*?\*/",
            string.Empty,
            RegexOptions.Singleline);
}
