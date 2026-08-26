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
