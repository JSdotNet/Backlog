using System.Text.RegularExpressions;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A fenced <c>meta</c> block is a record in a knowledge document and an
/// ordinary code fence everywhere else. The read view only knows which when a
/// host tells it, so the default has to stay what it has always been.
/// </summary>
public sealed class MarkdownViewMetaFenceTests
{
    private const string Document = """
        # Shared Technologies

        ```meta
        status: adopted
        related: [".tech/technology-graph.md"]
        ```

        Prose after the block.
        """;

    [Fact]
    public void By_default_a_meta_fence_is_still_a_code_block()
    {
        using var context = new BunitContext();

        var view = Render(context, Document);

        Assert.NotNull(view.Find("pre.md-code"));
        Assert.Contains("status: adopted", view.Find("pre.md-code code").TextContent, StringComparison.Ordinal);
        Assert.Empty(view.FindAll("dl.knowledge-fields"));
    }

    [Fact]
    public void Asked_for_it_the_fence_becomes_the_metadata_it_describes()
    {
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        Assert.NotNull(view.Find("dl.knowledge-fields"));
        Assert.Equal("adopted", view.Find(".badge--status").TextContent);
        Assert.Equal(".tech/technology-graph.md", view.Find("code.knowledge-ref--inert").TextContent);
        Assert.Empty(view.FindAll("pre.md-code"));

        // The rest of the document is untouched.
        Assert.Contains("Prose after the block.", view.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_folder_reaches_the_status()
    {
        // Naming the folder is what turns the status from a word into a value out
        // of a list, so the record offers that list. The plumbing being checked
        // here is that MarkdownView passes the folder down at all.
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.KnowledgeFolder, KnowledgeFolder.Tech));

        var select = view.Find(".knowledge-record__headline .status-editor select");
        Assert.Equal("adopted", select.GetAttribute("value"));
        Assert.Equal(
            ["candidate", "trial", "adopted", "hold", "retired"],
            select.QuerySelectorAll("option").Select(option => option.GetAttribute("value")));
    }

    [Fact]
    public void Without_a_folder_the_fence_still_reads_back_as_a_plain_pill()
    {
        // The read view's default is folder-blind, and most markdown in this
        // product is an entry body rather than a knowledge chapter.
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        Assert.Empty(view.FindAll("select"));

        // No state modifier: the tone is what maps a folder's word onto one, and
        // a folder-blind view has no tone to map from.
        Assert.Equal("badge badge--status", view.Find(".badge--status").GetAttribute("class"));
    }

    [Fact]
    public void A_reference_reports_itself_to_the_host_that_asked_to_hear_about_it()
    {
        using var context = new BunitContext();
        var followed = new List<KnowledgeReference>();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.OnKnowledgeNavigate, EventCallback.Factory.Create<KnowledgeReference>(this, followed.Add)));

        view.Find("button.knowledge-ref--action").Click();

        Assert.Equal(".tech/technology-graph.md", Assert.Single(followed).Raw);
    }

    [Fact]
    public void A_href_resolver_reaches_the_reference_too()
    {
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.KnowledgeHrefFor, reference => $"/knowledge/{reference.Path}"));

        Assert.Equal("/knowledge/.tech/technology-graph.md", view.Find("a.knowledge-ref--link").GetAttribute("href"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_diagram_fence_is_a_diagram_either_way(bool renderKnowledgeMetadata)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = Render(context, "```mermaid\ngraph TD;\n  A-->B;\n```\n", parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, renderKnowledgeMetadata));

        Assert.NotEmpty(view.FindAll("[data-testid='diagram-view']"));
        Assert.Empty(view.FindAll("dl.knowledge-fields"));
    }

    [Fact]
    public void A_code_fence_that_only_looks_like_metadata_stays_a_code_block()
    {
        using var context = new BunitContext();

        var view = Render(context, "```yaml\nstatus: adopted\n```\n", parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        Assert.NotNull(view.Find("pre.md-code"));
        Assert.Empty(view.FindAll("dl.knowledge-fields"));
    }

    [Fact]
    public void A_heading_and_the_fence_under_it_are_drawn_as_one_record()
    {
        // The convention puts the meta fence directly under the heading it
        // describes, so that is what the two are: one record, one line, the
        // status beside the heading rather than in a block below it.
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        var headline = view.Find(".knowledge-record__headline");
        Assert.Equal("Shared Technologies", headline.QuerySelector("p.md-heading")?.TextContent);
        Assert.Equal("adopted", headline.QuerySelector(".badge--status")?.TextContent);

        // Drawn once each: the heading is not also emitted as its own block, and
        // the fence is not also emitted as a second record.
        Assert.Single(view.FindAll("p.md-heading"));
        Assert.Single(view.FindAll(".knowledge-record"));
    }

    [Fact]
    public void The_heading_keeps_the_markup_navigation_matches_on()
    {
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        var heading = view.Find(".knowledge-record__headline p.md-heading");
        Assert.Equal("md-heading md-heading--1", heading.GetAttribute("class"));
        Assert.Equal("heading", heading.GetAttribute("role"));
        Assert.Equal("1", heading.GetAttribute("aria-level"));
    }

    [Fact]
    public void Annotated_the_heading_and_the_fence_are_still_drawn_as_one_record()
    {
        // The annotated view used to keep the two stacked, on the grounds that
        // folding two blocks into one row would leave the fence's comments
        // pointing at a row that no longer exists. True, and answered by the row
        // taking both indices' notes instead — see the anchoring test below. The
        // cost of giving the pairing up was that a reviewed chapter, the one
        // document whose currency a reader most needs to see, was the only one
        // showing its status as a raw code fence.
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.OnAddComment, EventCallback.Factory.Create<int>(this, _ => { })));

        Assert.NotNull(view.Find("[data-block='0'] .knowledge-record__headline p.md-heading"));
        Assert.Empty(view.FindAll("pre.md-code"));

        // The fence's own turn in the loop is a no-op rather than a second row.
        Assert.Empty(view.FindAll("[data-block='1']"));

        // Still exactly one of each — the heading is not dropped for want of a
        // record to sit in.
        Assert.Single(view.FindAll("p.md-heading"));
        Assert.Single(view.FindAll(".knowledge-record"));
    }

    [Fact]
    public void A_comment_on_the_paired_fence_is_drawn_in_the_row_it_was_folded_into()
    {
        // The anchor is untouched: a comment on the fence still says the fence's
        // index and the host that wrote it has nothing to fix up. Only where it is
        // drawn moves, into the row of the heading the fence became part of, which
        // is the row level with it on the screen. The alternative was a row
        // carrying notes for a block the reader can no longer see.
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.Comments, new MarkdownComment[]
            {
                new("c0", 0, "About the heading."),
                new("c1", 1, "About the fence.")
            }));

        var notes = view.FindAll("[data-block='0'] .md-block-row__notes .md-comment__body")
            .Select(body => body.TextContent);

        Assert.Equal(["About the heading.", "About the fence."], notes);
        Assert.Empty(view.FindAll("[data-testid='markdown-orphaned-comments']"));
    }

    [Fact]
    public void A_fence_with_no_heading_before_it_is_a_record_on_its_own()
    {
        using var context = new BunitContext();

        var view = Render(context, """
            ```meta
            status: adopted
            ```
            """, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        var headline = view.Find(".knowledge-record__headline");
        Assert.Single(headline.Children);
        Assert.Equal("adopted", headline.Children[0].TextContent);
    }

    [Fact]
    public void A_fence_that_does_not_follow_its_heading_is_not_pulled_up_to_it()
    {
        // Only *immediately* under. A fence with prose between it and a heading
        // is describing something else, and hoisting it would put a status next
        // to a title it was never written against.
        using var context = new BunitContext();

        var view = Render(context, """
            # Shared Technologies

            Prose in between.

            ```meta
            status: adopted
            ```
            """, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        Assert.Empty(view.FindAll(".knowledge-record__headline p.md-heading"));
        Assert.Single(view.FindAll("p.md-heading"));
    }

    [Fact]
    public void A_host_can_take_the_field_list_off_without_losing_the_status()
    {
        using var context = new BunitContext();

        var view = Render(context, Document, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.RenderKnowledgeMetadataFields, false));

        Assert.Equal("adopted", view.Find(".knowledge-record__headline .badge--status").TextContent);
        Assert.Equal("Shared Technologies", view.Find(".knowledge-record__headline p.md-heading").TextContent);
        Assert.Empty(view.FindAll("dl.knowledge-fields"));

        // And the fence is still not a code block: suppressing the rows is not the
        // same as declining to read the block.
        Assert.Empty(view.FindAll("pre.md-code"));
    }

    /// <summary>
    /// A fence in a body becomes a picture and nothing else. It used to become a
    /// picture with the same fence folded underneath it, which a reading surface
    /// had to switch off by hand; there is no fold to switch off any more, and no
    /// parameter left to do it with.
    /// </summary>
    [Fact]
    public void A_diagram_in_a_body_is_a_picture_with_no_fold_of_text_under_it()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = Render(context, "```mermaid\ngraph TD;\n  A-->B;\n```\n", parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        Assert.NotNull(view.Find("[data-testid='diagram-view']"));
        Assert.Empty(view.FindAll(".diagram-view__details"));
        Assert.Empty(view.FindAll("details"));
    }

    [Fact]
    public void A_document_that_draws_status_reserves_the_column_it_is_read_in()
    {
        // Every line stops at the same rule, which is the only way the pills down
        // the right of a file read as a column rather than as text with a chip in
        // it. The prose used to run the length of the status beside it.
        using var context = new BunitContext();

        var view = Render(context, Document, p => p.Add(v => v.RenderKnowledgeMetadata, true));

        Assert.Contains("md-view--status-column", view.Find(".md-view").ClassList);
    }

    [Fact]
    public void A_document_with_no_status_in_it_reserves_nothing()
    {
        // Knowledgeable and still statusless: a chapter with no meta fence draws
        // no pill, and a column held open for one would be a margin of nothing.
        using var context = new BunitContext();

        var view = Render(
            context,
            "# A heading\n\nProse with no record in it.",
            p => p.Add(v => v.RenderKnowledgeMetadata, true));

        Assert.DoesNotContain("md-view--status-column", view.Find(".md-view").ClassList);
    }

    /// <summary>
    /// A reading surface too narrow to afford the column takes it back.
    ///
    /// <para>The reservation is a fixed <c>7.5rem</c> and the pane it is paid out
    /// of is not fixed at all. Measured in the desktop knowledge side pane the
    /// reading column is about 240px, so 120px of it was gutter and the first
    /// paragraph rendered at about 95px — two words a line, nowhere near the
    /// 72–90 characters <c>.design/typography-and-layout.md</c> asks for. A column
    /// that costs half the measure is not a column; it is a document with a margin
    /// where the words were.</para>
    ///
    /// <para>So the reservation is conditional on there being a measure left to
    /// take it out of. <c>48rem</c> is the <c>md</c> breakpoint that chapter
    /// lists, and it is the width at which paying the column still leaves
    /// <c>40.5rem</c> — about eighty characters, inside the measure rather than
    /// under it.</para>
    /// </summary>
    [Fact]
    public void Too_narrow_to_afford_the_column_the_reading_surface_takes_it_back()
    {
        var css = Stylesheet();

        // The reading body is the container, and the container is the whole of the
        // fix: the pane is a track a reader drags, so the window is 1920px wide
        // while the column being measured is 240. A media query would have answered
        // about the wrong box.
        // Named with its combinator, the way .file-view > .file-view__header is
        // asked for in FileViewHeaderLayoutTests. ".file-view__body {" on its own is
        // a substring of ".file-view--editing .file-view__body {", which is stated
        // earlier and is a different rule about a different state — the lookup would
        // read that one and report the container as missing.
        var body = Rule(".file-view > .file-view__body {");

        Assert.Contains("container-type: inline-size", body, StringComparison.Ordinal);
        Assert.Contains("container-name: file-view-body", body, StringComparison.Ordinal);

        var guard = Rule(Guard);

        Assert.True(
            Regex.IsMatch(guard, @"\.md-view--status-column\s*\{[^}]*--md-status-column\s*:\s*0"),
            "Below the breakpoint the reservation has to resolve to nothing, or the pane is still "
            + $"spending half its measure on a gutter. Rule found:\n{guard}");

        // Stated after the reservation, because a container query carries no
        // specificity of its own — first one loses, and the first one is 7.5rem.
        Assert.True(
            css.IndexOf(".md-view--status-column {", StringComparison.Ordinal)
            < css.IndexOf(Guard, StringComparison.Ordinal),
            "The withdrawal is stated before the reservation it withdraws, so the reservation wins and "
            + "nothing is withdrawn at all.");
    }

    /// <summary>
    /// One variable is withdrawn and everything the column was costing goes with
    /// it.
    ///
    /// <para>Three rules spend the reservation — the padding every block stops at,
    /// the negative margin that reaches the status back into it, and the corner an
    /// annotated block holds clear — and each reads it from
    /// <c>--md-status-column</c> rather than restating <c>7.5rem</c>. That is what
    /// makes the guard above one rule and not four, and it is what keeps a status
    /// clear of the prose at every width: a padding and a margin that cancel each
    /// other cannot cancel unequally.</para>
    ///
    /// <para>Above the breakpoint none of it moves. The column is still
    /// <c>7.5rem</c>, every block still stops at the same rule, and the statuses
    /// down the right of a file still align on one edge — see
    /// <c>FileViewHeaderLayoutTests.The_files_status_and_the_chapters_statuses_are_one_column</c>,
    /// which reads that from the header's side.</para>
    /// </summary>
    [Fact]
    public void Withdrawing_the_column_withdraws_everything_it_was_costing()
    {
        Assert.Contains(
            "--md-status-column: 7.5rem",
            Rule(".md-view--status-column {"),
            StringComparison.Ordinal);

        Assert.Contains(
            "padding-inline-end: var(--md-status-column)",
            Rule(".md-view--status-column > :not(.md-block-row)"),
            StringComparison.Ordinal);

        Assert.Contains(
            "margin-inline-end: calc(-1 * var(--md-status-column))",
            Rule(".md-view--status-column .knowledge-record__headline"),
            StringComparison.Ordinal);

        Assert.Contains(
            "var(--md-status-column, 0rem)",
            Rule(".md-block--affordance {"),
            StringComparison.Ordinal);

        // And the column is set in exactly two places: the reservation, and the
        // guard that takes it back. A third would be a width nobody could find
        // from either of the first two.
        var settings = Regex.Matches(Stylesheet(), @"--md-status-column\s*:\s*([^;]+);")
            .Select(match => match.Groups[1].Value.Trim())
            .ToArray();

        Assert.Equal(["7.5rem", "0rem"], settings);
    }

    /// <summary>
    /// The three knowledge panes get this from the one rule, because none of them
    /// has a rule of its own.
    ///
    /// <para><c>Arc42KnowledgePanel</c>, <c>DomainKnowledgePanel</c> and
    /// <c>DesignKnowledgeView</c> all read a file through the same
    /// <c>FileView</c> into the same <c>.md-view</c>. A host reserving the column
    /// itself would be a fourth width to find, and the library would no longer own
    /// the one the component draws
    /// (<c>.arc42/adr/guidelines/0011-centralized-frontend-styling-variables.md</c>).</para>
    /// </summary>
    [Fact]
    public void No_host_reserves_the_column_for_itself()
    {
        var app = File.ReadAllText(RepositoryRoot.File(
            "src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css"));

        Assert.DoesNotContain("--md-status-column", app, StringComparison.Ordinal);
        Assert.DoesNotContain("md-view--status-column", app, StringComparison.Ordinal);
    }

    /// <summary>The container query the reservation is guarded by, as the
    /// stylesheet states it — one string, so the breakpoint and the container it
    /// answers to are read from the same place.</summary>
    private const string Guard = "@container file-view-body (max-width: 48rem)";

    /// <summary>The rule that opens with <paramref name="selector"/>, braces matched
    /// so a nested block cannot end it early — which is what lets the guard above be
    /// asked for by its own at-rule. Duplicated per file for the reason the other
    /// stylesheet tests in this project duplicate it: each asserts on a different
    /// section, and a shared helper would be a third place to look.</summary>
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

    private static IRenderedComponent<MarkdownView> Render(
        BunitContext context,
        string source,
        Action<ComponentParameterCollectionBuilder<MarkdownView>>? extra = null) =>
        context.Render<MarkdownView>(parameters =>
        {
            parameters.Add(v => v.Blocks, MarkdownPreview.Parse(source));
            extra?.Invoke(parameters);
        });
}
