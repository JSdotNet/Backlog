using AngleSharp.Dom;

using Bunit;
using Backlog.Infrastructure.GitHub;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class InstructionsKnowledgePanelTests
{
    [Fact]
    public async Task Instructions_panel_renders_repository_instructions_without_throwing()
    {
        await using var harness = CreateHarness();

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog"));

        Assert.Contains("Instructions", component.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(component.FindAll("[data-testid='instructions-document']"));
    }

    /// <summary>
    /// The panel this was reported against. It drew its own header and its own
    /// body, and the relative path was on both — once beside the name and once on
    /// the editing surface's bar directly below it.
    /// </summary>
    [Fact]
    public async Task The_selected_document_is_shown_through_the_shared_file_view()
    {
        await using var harness = CreateCloneHarness();

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog")
            .Add(parameter => parameter.SelectedPath, ".github/copilot-instructions.md"));

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='instructions-document-body']")));

        // A selection means no file list, so the whole panel is the scope: the
        // path belongs on the file view's header and nowhere else on the screen.
        component.AssertTheFileIsNamedOnce("copilot-instructions.md", "[data-testid='instructions-knowledge-panel']");
    }

    /// <summary>
    /// Read is the resting state, so the editing surface is not on screen until
    /// someone asks for it — what is on screen is the file, and the button that
    /// opens it, in the file view's own header.
    /// </summary>
    [Fact]
    public async Task The_selected_document_opens_as_the_file_read_and_offers_a_way_in()
    {
        await using var harness = CreateCloneHarness();

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog")
            .Add(parameter => parameter.SelectedPath, ".github/copilot-instructions.md"));

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='instructions-document-edit']")));
        Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-surface']"));
        Assert.Contains("Repository-wide guidance.", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_puts_the_editing_surface_in_the_file_views_own_body()
    {
        await using var harness = CreateCloneHarness();

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog")
            .Add(parameter => parameter.SelectedPath, ".github/copilot-instructions.md"));

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='instructions-document-edit']")));
        component.Find("[data-testid='instructions-document-edit']").Click();

        // Inside the file view's body rather than merely somewhere on the panel.
        // The header is what keeps the identity on screen while the file scrolls,
        // and a body that landed beside the file view instead of in it would take
        // that away while still looking right in a screenshot.
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='instructions-document-body'] [data-testid='knowledge-chapter-surface']")));

        // The way out is where the way in was, and the editing surface brings no
        // second one of its own: two Edit buttons, one under the other, is what
        // Bare exists to prevent.
        Assert.Single(component.FindAll("[data-testid='instructions-document-done']"));
        Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-edit']"));
    }

    [Fact]
    public async Task The_file_view_header_carries_the_agent_the_scope_and_the_size()
    {
        await using var harness = CreateCloneHarness();

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog")
            .Add(parameter => parameter.SelectedPath, ".github/copilot-instructions.md"));

        // Every fact the panel's own header used to list, on the header that
        // replaced it. Moving onto a shared component is only an alignment if
        // nothing a reader was being told goes missing in the move.
        component.WaitForAssertion(() =>
        {
            var meta = component.Find(".file-view__meta").TextContent;
            Assert.Contains("GitHub Copilot", meta, StringComparison.Ordinal);
            Assert.Contains("Repository-wide instructions", meta, StringComparison.Ordinal);
            Assert.Contains(" B", meta, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task The_selected_document_is_edited_from_the_file_and_not_from_the_discovery_pass()
    {
        await using var harness = CreateCloneHarness();
        var claude = Path.Combine(harness.Root, "clone", "CLAUDE.md");

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog"));
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("[data-testid='instructions-document-button']").Count));

        // Discovery read every instruction file in the clone to build the list on
        // the left, and the file moved after it. Selecting it has to open what the
        // file says now: the editor writes the whole buffer back, so a buffer built
        // from the discovery pass would put the old text back over this.
        File.WriteAllText(claude, "# Claude\n\nChanged on disk after the list was built.\n");
        component.FindAll("[data-testid='instructions-document-button']")[1].Click();

        component.WaitForAssertion(
            () => Assert.Contains("Changed on disk after the list was built.", component.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("As the list found it.", component.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two facts an instruction file states above its first heading, drawn as
    /// facts. Read as markdown they were a divider and a paragraph of
    /// run-together `key: value` sitting on top of the prose, which is what the
    /// frontmatter strip replaces.
    /// </summary>
    [Fact]
    public async Task An_instruction_files_frontmatter_is_read_as_what_it_applies_to()
    {
        await using var harness = CreateCloneHarness();

        var instructions = Path.Combine(harness.Root, "clone", ".github", "instructions");
        Directory.CreateDirectory(instructions);
        File.WriteAllText(
            Path.Combine(instructions, "ui-components.instructions.md"),
            """
            ---
            applyTo: "src/App/**,src/Modules/**"
            description: An application screen renders the shared component library's components.
            ---

            # UI components

            A paragraph.
            """);

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog")
            .Add(parameter => parameter.SelectedPath, ".github/instructions/ui-components.instructions.md"));

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='instructions-document-frontmatter']")));

        var strip = component.Find("[data-testid='instructions-document-frontmatter']");

        // One badge per glob: the file writes both in one quoted scalar, and two
        // patterns are two facts.
        Assert.Equal(
            ["src/App/**", "src/Modules/**"],
            strip.QuerySelectorAll("[data-testid='instructions-document-applies-to'] .badge--glob").Select(badge => badge.TextContent));
        Assert.Equal(
            "An application screen renders the shared component library's components.",
            strip.QuerySelector("[data-testid='instructions-document-description']")!.TextContent);

        // And said once. The body under the strip is the editing surface's read
        // view, and it renders the file from the same text — so this is where the
        // frontmatter would come back as the divider and paragraph it used to be.
        var view = component.Find(".file-view");
        Assert.DoesNotContain("applyTo:", view.TextContent, StringComparison.Ordinal);
        Assert.Empty(view.QuerySelectorAll("hr.md-divider"));
        Assert.Equal("UI components", view.QuerySelector("[data-testid='instructions-document-body'] .md-heading")!.TextContent);
    }

    /// <summary>
    /// A skill file states a <c>name</c>, and nothing about the strip has a field
    /// for it. It is still shown — as its own label and its own words — because
    /// the block leaves the body once anything in it is drawn, and a line that
    /// left the file with nowhere to land is the one failure this feature must not
    /// have.
    /// </summary>
    [Fact]
    public async Task A_key_the_strip_has_no_field_for_is_still_on_the_screen()
    {
        await using var harness = CreateCloneHarness();

        var skill = Path.Combine(harness.Root, "clone", ".github", "skills", "pr-jsdotnet");
        Directory.CreateDirectory(skill);

        // The shape of this repository's own .github/skills/pr-jsdotnet/SKILL.md.
        File.WriteAllText(
            Path.Combine(skill, "SKILL.md"),
            """
            ---
            name: pr-jsdotnet
            description: 'Create a GitHub Pull Request in any JSdotNet repository through the `gh` CLI.'
            ---

            # Create PR in JSdotNet Repositories

            Use the JSdotNet account for that command only.
            """);

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog")
            .Add(parameter => parameter.SelectedPath, ".github/skills/pr-jsdotnet/SKILL.md"));

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='instructions-document-frontmatter']")));

        var strip = component.Find("[data-testid='instructions-document-frontmatter']");

        Assert.Equal("Name", strip.QuerySelector("[data-testid='instructions-document-field-name'] .file-view__frontmatter-label")!.TextContent);
        Assert.Equal("pr-jsdotnet", strip.QuerySelector("[data-testid='instructions-document-field-name'] .file-view__frontmatter-value")!.TextContent);
        Assert.StartsWith(
            "Create a GitHub Pull Request",
            strip.QuerySelector("[data-testid='instructions-document-description']")!.TextContent,
            StringComparison.Ordinal);

        // Said once, and nothing said nowhere: the read view below has given the
        // block up, and every key it held is in the strip above.
        var view = component.Find(".file-view");

        Assert.DoesNotContain("name:", view.TextContent, StringComparison.Ordinal);
        Assert.Empty(view.QuerySelectorAll("hr.md-divider"));
    }

    /// <summary>
    /// The second reading of the folder. It is a fact about the set, so it is a
    /// tab beside the files rather than a row on any one of them — the shape the
    /// arc42 panel already keeps for its C4 model.
    /// </summary>
    [Fact]
    public async Task The_folder_can_be_read_as_each_assistant_would_read_it()
    {
        await using var harness = CreateComparisonHarness();

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog"));

        // Clicked on the render it was found in. The chapter behind the opening
        // file loads asynchronously, and a click issued across that render is
        // dispatched to a handler the tab strip no longer has.
        await component.InvokeAsync(() => component.Find("[data-testid='instructions-reach-tab']").Click());

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='instructions-reach-view']")));
        Assert.Equal(4, component.FindAll("[data-testid='instructions-reach-table'] tbody tr").Count);

        // What each host carries, which is the question the sizes answer.
        Assert.Single(component.FindAll("[data-testid='instructions-reach-total-claude']"));
        Assert.Single(component.FindAll("[data-testid='instructions-reach-total-copilot']"));
    }

    /// <summary>
    /// The scoped reading. A conditional rule is context a host might spend, and
    /// picking the file it governs is how a reader turns that into context it
    /// does spend.
    /// </summary>
    [Fact]
    public async Task Picking_a_path_turns_on_the_rules_that_govern_it()
    {
        await using var harness = CreateComparisonHarness();

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog"));

        await component.InvokeAsync(() => component.Find("[data-testid='instructions-reach-tab']").Click());
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='instructions-reach-view']")));

        // Nothing picked yet, so the scoped rule is off and nothing is listed.
        Assert.Empty(component.FindAll("[data-testid='instructions-reach-picked']"));

        await component.InvokeAsync(() => component.Find("[data-testid='instructions-reach-picker-toggle']").Click());
        await ClickTreeRowAsync(component, "src");
        await ClickTreeRowAsync(component, "App");
        await ClickTreeRowAsync(component, "Home.razor");

        var picked = component.WaitForElement("[data-testid='instructions-reach-picked']");
        Assert.Contains("src/App/Home.razor", picked.TextContent, StringComparison.Ordinal);

        // The picked row has to look picked. Reported against this: aria-selected
        // was set and nothing else was, so the only row that looked chosen was
        // whichever one the pointer happened to be over.
        var row = Assert.Single(
            component.FindAll("[data-testid='instructions-reach-view'] [role='treeitem']")
                .Where(item => item.TextContent.Trim().EndsWith("Home.razor", StringComparison.Ordinal)));

        Assert.Equal("true", row.GetAttribute("aria-selected"));
        Assert.Contains("knowledge-menu__item--active", row.ClassName ?? string.Empty, StringComparison.Ordinal);

        // The rule scoped to src/App is now one Copilot loads for this change.
        var scoped = Assert.Single(
            component.FindAll("[data-testid='instructions-reach-table'] tbody tr")
                .Where(row => row.TextContent.Contains("ui-components.instructions.md", StringComparison.Ordinal)));

        Assert.Contains(
            "badge--reach-matched",
            scoped.QuerySelector("[data-testid='instructions-reach-copilot'] .badge")!.ClassName ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clearing_the_selection_puts_the_baseline_back()
    {
        await using var harness = CreateComparisonHarness();

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog"));

        await component.InvokeAsync(() => component.Find("[data-testid='instructions-reach-tab']").Click());
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='instructions-reach-view']")));

        await component.InvokeAsync(() => component.Find("[data-testid='instructions-reach-picker-toggle']").Click());
        await ClickTreeRowAsync(component, "src");
        await ClickTreeRowAsync(component, "App");
        await ClickTreeRowAsync(component, "Home.razor");

        component.WaitForElement("[data-testid='instructions-reach-picked']");

        await component.InvokeAsync(() => component.Find("[data-testid='instructions-reach-clear']").Click());

        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='instructions-reach-picked']")));
    }

    /// <summary>
    /// The band this view must never get wrong. Claude Code's own context view
    /// calls its remainder "free space"; this one cannot, because the
    /// conversation, the tool definitions and the system prompt all sit in there
    /// and this app can see none of them.
    /// </summary>
    [Fact]
    public async Task The_window_bar_never_calls_the_remainder_free()
    {
        await using var harness = CreateComparisonHarness();

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog"));

        await component.InvokeAsync(() => component.Find("[data-testid='instructions-reach-tab']").Click());
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='instructions-reach-view']")));

        // One bar per assistant, each with its own legend.
        var claude = component.Find("[data-testid='instructions-reach-window-claude']");
        Assert.Single(component.FindAll("[data-testid='instructions-reach-window-copilot']"));

        // Asserted on the bar rather than on the whole panel: the caveat below it
        // says the words "not free space" on purpose, and a check over the markup
        // cannot tell a disclaimer from the claim it disclaims.
        Assert.Contains("Repository, every session", claude.TextContent, StringComparison.Ordinal);
        Assert.Contains("Messages, tools and the rest", claude.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("free", claude.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("available", claude.TextContent, StringComparison.OrdinalIgnoreCase);

        // The caveat is where the point gets made, so it has to be there.
        Assert.Contains("not free space", component.Markup, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The matched band is absent until a path makes it real — the bar
    /// drops a part worth nothing rather than drawing an empty one.</summary>
    [Fact]
    public async Task Picking_a_path_adds_a_matched_band_to_the_window_bar()
    {
        await using var harness = CreateComparisonHarness();

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog"));

        await component.InvokeAsync(() => component.Find("[data-testid='instructions-reach-tab']").Click());
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='instructions-reach-view']")));

        Assert.DoesNotContain("Matched by the selected paths", component.Markup, StringComparison.Ordinal);

        await component.InvokeAsync(() => component.Find("[data-testid='instructions-reach-picker-toggle']").Click());
        await ClickTreeRowAsync(component, "src");
        await ClickTreeRowAsync(component, "App");
        await ClickTreeRowAsync(component, "Home.razor");

        component.WaitForAssertion(() => Assert.Contains(
            "Matched by the selected paths",
            component.Markup,
            StringComparison.Ordinal));
    }

    /// <summary>Clicks a row of the picker's tree by its label. Re-found inside
    /// the dispatch, because every click re-renders the tree under it.</summary>
    private static async Task ClickTreeRowAsync(IRenderedComponent<InstructionsKnowledgePanel> component, string label)
    {
        component.WaitForAssertion(() => Assert.Contains(
            component.FindAll("[data-testid='instructions-reach-view'] [role='treeitem']"),
            row => row.TextContent.Trim().EndsWith(label, StringComparison.Ordinal)));

        await component.InvokeAsync(() =>
            component.FindAll("[data-testid='instructions-reach-view'] [role='treeitem']")
                .First(row => row.TextContent.Trim().EndsWith(label, StringComparison.Ordinal))
                .Click());
    }

    /// <summary>
    /// The files are what the section opens with. The comparison is a step away
    /// and not the landing surface: a reader coming here from the knowledge menu
    /// asked for a file.
    /// </summary>
    [Fact]
    public async Task The_section_opens_on_the_files_with_the_comparison_beside_them()
    {
        await using var harness = CreateComparisonHarness();

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog"));

        Assert.Single(component.FindAll("[data-testid='instructions-files-tab']"));
        Assert.Single(component.FindAll("[data-testid='instructions-reach-tab']"));
        Assert.Empty(component.FindAll("[data-testid='instructions-reach-view']"));
    }

    /// <summary>
    /// The finding the view exists for: a file GitHub Copilot reads on every
    /// request that Claude Code has no route to at all, because the file Claude
    /// does read never names it.
    /// </summary>
    [Fact]
    public async Task A_file_only_one_assistant_reads_is_marked_on_its_own_row()
    {
        await using var harness = CreateComparisonHarness();

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog"));

        await component.InvokeAsync(() => component.Find("[data-testid='instructions-reach-tab']").Click());
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='instructions-reach-view']")));

        var naming = Assert.Single(
            component.FindAll("[data-testid='instructions-reach-table'] tbody tr")
                .Where(row => row.TextContent.Contains("naming.instructions.md", StringComparison.Ordinal)));

        Assert.Contains("Not read", naming.QuerySelector("[data-testid='instructions-reach-claude']")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Always", naming.QuerySelector("[data-testid='instructions-reach-copilot']")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("instruction-reach__row--one-sided", naming.ClassName ?? string.Empty, StringComparison.Ordinal);

        // The one Claude does read is not flagged, or the mark would mean nothing.
        var claudeFile = Assert.Single(
            component.FindAll("[data-testid='instructions-reach-table'] tbody tr")
                .Where(row => row.TextContent.Contains("CLAUDE.md", StringComparison.Ordinal)));

        Assert.Contains("Always", claudeFile.QuerySelector("[data-testid='instructions-reach-claude']")!.TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// A tab that opens onto nothing is worse than no tab, so it is absent rather
    /// than empty when the clone holds no instruction files at all.
    /// </summary>
    [Fact]
    public async Task The_comparison_is_absent_when_there_is_nothing_to_compare()
    {
        await using var harness = CreateComparisonHarness(withDocuments: false);

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog"));

        Assert.Empty(component.FindAll("[data-testid='instructions-reach-tab']"));
    }

    /// <summary>A clone with one file each side and one that only Copilot can
    /// reach, which is the asymmetry the comparison is for.</summary>
    private static Harness CreateComparisonHarness(bool withDocuments = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-instructions-reach-tests", Guid.NewGuid().ToString("n"));
        var clone = Path.Combine(root, "clone");
        Directory.CreateDirectory(Path.Combine(clone, ".github", "instructions"));

        if (withDocuments)
        {
            File.WriteAllText(
                Path.Combine(clone, ".github", "copilot-instructions.md"),
                "# Copilot\n\nSee `.github/instructions/naming.instructions.md`.\n");
            File.WriteAllText(
                Path.Combine(clone, ".github", "instructions", "naming.instructions.md"),
                "---\napplyTo: \"**\"\ndescription: Naming.\n---\n\n# Naming\n");

            // Scoped rather than repository-wide, so there is something for a
            // picked path to switch on.
            File.WriteAllText(
                Path.Combine(clone, ".github", "instructions", "ui-components.instructions.md"),
                "---\napplyTo: \"src/App/**\"\ndescription: UI components.\n---\n\n# UI components\n");

            // Names no instruction file of its own, which is what leaves the
            // naming rules unreachable from this side.
            File.WriteAllText(Path.Combine(clone, "CLAUDE.md"), "# Claude\n\nNothing linked from here.\n");

            // Not an instruction file — a file to ask the question about.
            Directory.CreateDirectory(Path.Combine(clone, "src", "App"));
            File.WriteAllText(Path.Combine(clone, "src", "App", "Home.razor"), "<h1>Home</h1>\n");
        }

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = clone,
            KnowledgeFolders = KnowledgeFolderSetting.Defaults()
        };
        Assert.Null(gitHubSettings.SetRepositories([repository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(store);
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(gitHubSettings, store));
        context.Services.AddSingleton(new InstructionSourceDiscovery());
        context.Services.AddSingleton<KnowledgeChapterWriter>();

        return new Harness(root, context);
    }

    private static Harness CreateCloneHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-instructions-clone-tests", Guid.NewGuid().ToString("n"));
        var clone = Path.Combine(root, "clone");
        Directory.CreateDirectory(Path.Combine(clone, ".github"));
        File.WriteAllText(Path.Combine(clone, ".github", "copilot-instructions.md"), "# Copilot\n\nRepository-wide guidance.\n");
        File.WriteAllText(Path.Combine(clone, "CLAUDE.md"), "# Claude\n\nAs the list found it.\n");

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = clone,
            KnowledgeFolders = KnowledgeFolderSetting.Defaults()
        };
        Assert.Null(gitHubSettings.SetRepositories([repository]));

        var context = new BunitContext();

        // The markdown editor watches its textarea through interop for the
        // highlight layer, which is not what this is about.
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(store);
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(gitHubSettings, store));
        context.Services.AddSingleton(new InstructionSourceDiscovery());
        context.Services.AddSingleton<KnowledgeChapterWriter>();

        return new Harness(root, context);
    }

    private static Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-instructions-tests", Guid.NewGuid().ToString("n"));
        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories);
        var configuredRepository = repository with
        {
            CloneDirectory = FindRepositoryRoot(),
            KnowledgeFolders = KnowledgeFolderSetting.Defaults()
        };

        Assert.Null(gitHubSettings.SetRepositories([configuredRepository]));

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(gitHubSettings, store));
        context.Services.AddSingleton(new InstructionSourceDiscovery());
        // The panel renders the shared editing surface, which writes.
        context.Services.AddSingleton<KnowledgeChapterWriter>();

        return new Harness(root, context);
    }

    private static string FindRepositoryRoot() => RepositoryRoot.Root.FullName;

    private sealed record Harness(string Root, BunitContext Context) : IAsyncDisposable
    {
        /// <summary>
        /// Awaited disposal, because the editing surface this harness renders
        /// writes its last pending save on the way out. A synchronous
        /// <c>Dispose</c> hands that save to the renderer's dispatcher and returns
        /// before it lands, so the folder delete that follows could arrive while
        /// the file was still being replaced — a locked temp file on a slow
        /// machine and a green suite on a fast one.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();

            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
