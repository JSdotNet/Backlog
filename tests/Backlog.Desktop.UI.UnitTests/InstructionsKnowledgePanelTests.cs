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

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-chapter-surface']")));

        // Inside the file view's body rather than merely somewhere on the panel.
        // The header is what keeps the identity on screen while the file scrolls,
        // and a body that landed beside the file view instead of in it would take
        // that away while still looking right in a screenshot.
        Assert.Single(component.FindAll("[data-testid='instructions-document-body'] [data-testid='knowledge-chapter-surface']"));

        // A selection means no file list, so the whole panel is the scope: the
        // path belongs on the file view's header and nowhere else on the screen.
        component.AssertTheFileIsNamedOnce("copilot-instructions.md", "[data-testid='instructions-knowledge-panel']");
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
