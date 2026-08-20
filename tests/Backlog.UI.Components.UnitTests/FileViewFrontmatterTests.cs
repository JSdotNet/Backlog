namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A file's YAML frontmatter, read as the facts it is.
///
/// <para>What this exists to stop is the reading a markdown parser gives those
/// four lines: a divider, and a paragraph of run-together <c>key: value</c>
/// pairs above the file's first heading. The strip says the same thing as facts —
/// and then the body is not asked to say it a second time.</para>
///
/// <para>Opt-in throughout, like every other reading of the file's own text in
/// this component. The tests that pin "nothing changed for a caller that asked
/// for nothing" live in <see cref="FileViewKnowledgeTests"/> and
/// <see cref="FileViewBodyTests"/>; the first two here are this feature's own
/// half of that bargain.</para>
/// </summary>
public sealed class FileViewFrontmatterTests
{
    /// <summary>Verbatim from .github/instructions/ui-components.instructions.md:
    /// a comma-separated glob list in one double-quoted scalar, and a description
    /// carrying a semicolon, a comma and a colon of its own.</summary>
    private const string UiComponents = """
        ---
        applyTo: "src/App/**,src/Modules/**,src/Core/Backlog.UI.Components/**"
        description: An application screen renders the shared component library's components rather than growing its own copies; how to make a component fit, and what to do when it cannot.
        ---

        # UI components

        `src/Core/Backlog.UI.Components` is the product's own component library.
        """;

    private static IRenderedComponent<FileView> Render(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<FileView>> extra,
        string? body = UiComponents) =>
        context.Render<FileView>(parameters =>
        {
            parameters
                .Add(v => v.Name, "ui-components.instructions.md")
                .Add(v => v.Body, body)
                .Add(v => v.TestId, "file");
            extra(parameters);
        });

    [Fact]
    public void Nothing_reads_the_frontmatter_until_a_host_asks_it_to()
    {
        using var context = new BunitContext();

        var view = Render(context, _ => { });

        Assert.Empty(view.FindAll(".file-view__frontmatter"));

        // And the body is the body it always was, block for block: a divider for
        // each fence line and the paragraph the fields ran together into.
        Assert.Equal(2, view.FindAll(".file-view__body hr.md-divider").Count);
        Assert.Contains("applyTo:", view.Find(".file-view__body .md-p").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_strip_says_what_the_file_is_about_and_where_it_applies()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.ShowFrontmatter, true));

        var strip = view.Find("[data-testid='file-frontmatter']");

        Assert.StartsWith(
            "An application screen renders the shared component library's components",
            strip.QuerySelector("[data-testid='file-description']")!.TextContent,
            StringComparison.Ordinal);

        // One badge per glob. The file writes three of them in one quoted scalar,
        // and three patterns are three facts.
        Assert.Equal(
            ["src/App/**", "src/Modules/**", "src/Core/Backlog.UI.Components/**"],
            strip.QuerySelectorAll("[data-testid='file-applies-to'] .badge--glob").Select(badge => badge.TextContent));

        // Named, because a row of paths with nothing in front of it reads as a
        // breadcrumb.
        Assert.Equal("Applies to", strip.QuerySelector(".file-view__frontmatter-label")!.TextContent);

        // Between the header and the body, not inside either.
        Assert.Empty(view.FindAll(".file-view__header .file-view__frontmatter"));
        Assert.Empty(view.FindAll(".file-view__body .file-view__frontmatter"));
    }

    [Fact]
    public void The_body_is_not_asked_to_say_the_same_thing_a_second_time()
    {
        // The whole point of the strip: the fields are drawn once, as facts. Left
        // in the body they would come back as the divider and the paragraph the
        // first test pins, directly under the strip that had just said them.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.ShowFrontmatter, true));

        var body = view.Find(".file-view__body");

        Assert.DoesNotContain("applyTo:", body.TextContent, StringComparison.Ordinal);
        Assert.Empty(body.QuerySelectorAll("hr.md-divider"));
        Assert.Equal("UI components", body.QuerySelector(".md-heading")!.TextContent);
    }

    [Fact]
    public void What_the_copy_button_hands_over_is_still_the_whole_file()
    {
        // The body loses the block; the file does not. What round-trips through
        // the clipboard is the source as it is on disk, frontmatter and all.
        using var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var view = Render(context, parameters => parameters
            .Add(v => v.ShowFrontmatter, true)
            .Add(v => v.AllowCopy, true));

        view.Find("[data-testid='file-copy']").Click();

        Assert.Equal(
            UiComponents,
            Assert.Single(context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]);
    }

    [Fact]
    public void A_file_with_nothing_in_its_first_lines_gets_no_strip_at_all()
    {
        // Asked for and not there: an empty container would be a row of padding
        // and a rule under the header saying nothing.
        using var context = new BunitContext();

        var view = Render(
            context,
            parameters => parameters.Add(v => v.ShowFrontmatter, true),
            "# UI components\n\nA file that opens with its heading.\n");

        Assert.Empty(view.FindAll(".file-view__frontmatter"));
        Assert.Equal("UI components", view.Find(".file-view__body .md-heading").TextContent);
        Assert.Equal("A file that opens with its heading.", view.Find(".file-view__body .md-p").TextContent);
    }

    [Fact]
    public void A_file_that_states_one_of_the_three_shows_that_one()
    {
        using var context = new BunitContext();

        var view = Render(
            context,
            parameters => parameters.Add(v => v.ShowFrontmatter, true),
            "---\napplyTo: \"**\"\n---\n\n# Everywhere\n");

        Assert.Empty(view.FindAll("[data-testid='file-description']"));
        Assert.Empty(view.FindAll("[data-testid='file-tools']"));
        Assert.Equal("**", view.Find("[data-testid='file-applies-to'] .badge--glob").TextContent);
    }

    [Fact]
    public void The_tools_a_file_declares_are_badges_of_their_own()
    {
        using var context = new BunitContext();

        var view = Render(
            context,
            parameters => parameters.Add(v => v.ShowFrontmatter, true),
            "---\ndescription: A prompt.\ntools: ['codebase', 'search/codebase']\n---\n\n# Prompt\n");

        var tools = view.Find("[data-testid='file-tools']");

        Assert.Equal("Tools", tools.QuerySelector(".file-view__frontmatter-label")!.TextContent);
        Assert.Equal(
            ["codebase", "search/codebase"],
            tools.QuerySelectorAll(".badge--tool").Select(badge => badge.TextContent));
        Assert.Empty(view.FindAll("[data-testid='file-applies-to']"));
    }

    [Fact]
    public void A_host_that_renders_the_body_says_what_the_file_is_and_keeps_its_own_text()
    {
        // The reason FrontmatterSource exists. The body here is an editable buffer
        // and it is written back over the file, so it holds the whole file — four
        // lines short would save the file without them. The strip is drawn from
        // the text the host names instead, and touches nothing.
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "ui-components.instructions.md")
            .Add(v => v.TestId, "file")
            .Add(v => v.ShowFrontmatter, true)
            .Add(v => v.FrontmatterSource, UiComponents)
            .Add(v => v.BodyContent, Buffer(UiComponents)));

        Assert.Equal(
            ["src/App/**", "src/Modules/**", "src/Core/Backlog.UI.Components/**"],
            view.FindAll("[data-testid='file-applies-to'] .badge--glob").Select(badge => badge.TextContent));

        // Against the DOM's own line endings rather than the file's: what is
        // being pinned is that the buffer still holds every line the file has.
        Assert.Equal(UiComponents.ReplaceLineEndings("\n"), view.Find(".file-view__body textarea").TextContent);
    }

    [Fact]
    public void A_key_the_strip_has_no_field_for_is_a_row_of_its_own()
    {
        // The strip never hides what it does not show. Before this, a block of
        // keys with no field here drew nothing and left the raw block in the body;
        // drawing the three it knows and taking the rest away with them would have
        // been worse — the line would have left the file and landed nowhere.
        using var context = new BunitContext();

        var view = Render(
            context,
            parameters => parameters.Add(v => v.ShowFrontmatter, true),
            "---\nmode: agent\nmodel: Claude Opus 5\n---\n\n# Prompt\n");

        var strip = view.Find("[data-testid='file-frontmatter']");

        Assert.Equal("Mode", strip.QuerySelector("[data-testid='file-field-mode'] .file-view__frontmatter-label")!.TextContent);
        Assert.Equal("agent", strip.QuerySelector("[data-testid='file-field-mode'] .file-view__frontmatter-value")!.TextContent);
        Assert.Equal("Claude Opus 5", strip.QuerySelector("[data-testid='file-field-model'] .file-view__frontmatter-value")!.TextContent);

        // Text, not a badge: a badge says "one of a set", and these are not known
        // to be one of anything.
        Assert.Empty(strip.QuerySelectorAll(".badge"));

        // And the block is out of the body, which is only safe because both keys
        // are on the screen above it.
        var body = view.Find(".file-view__body");

        Assert.DoesNotContain("mode: agent", body.TextContent, StringComparison.Ordinal);
        Assert.Empty(body.QuerySelectorAll("hr.md-divider"));
        Assert.Equal("Prompt", body.QuerySelector(".md-heading")!.TextContent);
    }

    [Fact]
    public void A_skill_file_shows_its_description_and_then_its_name()
    {
        // The order is the strip's: description, applies to, tools, then whatever
        // else the file said, in the order it said it.
        using var context = new BunitContext();

        var view = Render(
            context,
            parameters => parameters.Add(v => v.ShowFrontmatter, true),
            "---\nname: pr-jsdotnet\ndescription: Create a pull request as JSdotNet.\n---\n\n# Create PR\n");

        var strip = view.Find("[data-testid='file-frontmatter']");

        // The description leads, whatever order the file put it in.
        Assert.Equal(
            ["file-description", "file-field-name"],
            strip.Children.Select(row => row.GetAttribute("data-testid")));
        Assert.Equal("Name", strip.QuerySelector("[data-testid='file-field-name'] .file-view__frontmatter-label")!.TextContent);
        Assert.Equal("pr-jsdotnet", strip.QuerySelector("[data-testid='file-field-name'] .file-view__frontmatter-value")!.TextContent);
        Assert.DoesNotContain("name:", view.Find(".file-view__body").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_block_that_states_nothing_keeps_its_place_in_the_file()
    {
        // Nothing to draw means nothing is drawn — and then nothing may be taken
        // away either, so the body is the body it always was.
        using var context = new BunitContext();

        var view = Render(
            context,
            parameters => parameters.Add(v => v.ShowFrontmatter, true),
            "---\nmode:\nmodel: null\n---\n\n# Prompt\n");

        Assert.Empty(view.FindAll(".file-view__frontmatter"));
        Assert.Equal(2, view.FindAll(".file-view__body hr.md-divider").Count);
        Assert.Contains("model: null", view.Find(".file-view__body").TextContent, StringComparison.Ordinal);
    }

    /// <summary>Stands in for the host's editable buffer: the whole file, as it is
    /// on disk, because that is what a save writes back.</summary>
    private static RenderFragment Buffer(string text) => builder =>
    {
        builder.OpenElement(0, "textarea");
        builder.AddAttribute(1, "data-testid", "supplied-editor");
        builder.AddAttribute(2, "aria-label", "File source");
        builder.AddContent(3, text);
        builder.CloseElement();
    };
}
