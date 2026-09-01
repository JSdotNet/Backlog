using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// One button whose label is the whole affordance: it says "Check now" until a
/// check has found the clone behind, and "Pull latest" after. Nothing else on the
/// strip tells the reader which of the two pressing it will do, so the label, the
/// count beside it, and the sentence under it are asserted on the rendered markup
/// rather than on the state behind it.
/// <para>
/// The clone these tests point at is a folder they build, not this repository's
/// own checkout, because the point of a pull is that the folder changes: the fake
/// git writes a file the way a real pull would, and what is asserted is that the
/// pane shows it.
/// </para>
/// </summary>
public sealed class KnowledgePaneUpdateTests
{
    private const string PulledFileName = "pulled-after-the-check.instructions.md";

    /// <summary>How the menu labels <see cref="PulledFileName"/>: the tree titles
    /// its nodes rather than printing file names, so this is what the assertion
    /// has to look for.</summary>
    private const string PulledMenuLabel = "Pulled After The Check";

    [Fact]
    public async Task Knowledge_read_from_a_clone_offers_to_check_the_version_before_anybody_has_asked()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".knowledge-menu__item")));
        Assert.Equal("Check now", ActionButton(component).TextContent.Trim());
        Assert.Empty(component.FindAll("[data-testid='knowledge-update-behind']"));

        // Alert renders nothing at all for an empty message, so an unasked question
        // costs the pane no line of its own.
        Assert.Empty(component.FindAll("[data-testid='knowledge-update-status']"));
    }

    [Fact]
    public async Task Knowledge_kept_in_the_storage_folder_is_offered_no_version_control_at_all()
    {
        await using var harness = CreateHarness();

        var component = harness.RenderWithoutRepository();

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".knowledge-stack__nav-item")));
        Assert.Empty(component.FindAll("[data-testid='knowledge-update-action']"));
    }

    [Fact]
    public async Task A_repository_nobody_has_cloned_yet_is_offered_no_version_control_either()
    {
        await using var harness = CreateHarness(cloned: false);

        var component = harness.Render();

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".knowledge-stack__nav-item")));
        Assert.Empty(component.FindAll("[data-testid='knowledge-update-action']"));
    }

    [Fact]
    public async Task A_clone_on_the_latest_version_says_so_and_keeps_offering_the_check()
    {
        await using var harness = CreateHarness();
        harness.Git.NextCheck = Check(LocalGitRepositoryCurrency.UpToDate, behind: 0, "On the latest version of origin/main.");

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".knowledge-menu__item")));
        ActionButton(component).Click();

        component.WaitForAssertion(() =>
        {
            var status = component.Find("[data-testid='knowledge-update-status']");
            Assert.Equal("On the latest version of origin/main.", status.TextContent.Trim());
            Assert.Contains("knowledge-stack__update-status--ok", status.GetAttribute("class"));
            Assert.Equal("Check now", ActionButton(component).TextContent.Trim());
        });

        Assert.Empty(component.FindAll("[data-testid='knowledge-update-behind']"));
    }

    [Fact]
    public async Task A_clone_the_remote_has_moved_past_shows_how_far_behind_and_turns_the_button_into_a_pull()
    {
        await using var harness = CreateHarness();
        harness.Git.NextCheck = Check(LocalGitRepositoryCurrency.Behind, behind: 3, "3 commits behind origin/main.");

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".knowledge-menu__item")));
        ActionButton(component).Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal("3 behind", component.Find("[data-testid='knowledge-update-behind']").TextContent.Trim());
            Assert.Equal("Pull latest", ActionButton(component).TextContent.Trim());

            var status = component.Find("[data-testid='knowledge-update-status']");
            Assert.Equal("3 commits behind origin/main.", status.TextContent.Trim());
            Assert.Contains("knowledge-stack__update-status--available", status.GetAttribute("class"));
        });
    }

    [Fact]
    public async Task Pressing_the_button_a_second_time_pulls_and_the_pane_shows_what_arrived()
    {
        await using var harness = CreateHarness();
        harness.Git.NextCheck = Check(LocalGitRepositoryCurrency.Behind, behind: 1, "1 commit behind origin/main.");
        harness.Git.NextPull = LocalGitRepositoryPullResult.Succeeded(
            "Pulled the latest JSdotNet/Backlog into the clone.",
            Check(LocalGitRepositoryCurrency.UpToDate, behind: 0, "On the latest version of origin/main."));
        harness.Git.OnPull = harness.WritePulledInstruction;

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".knowledge-menu__item")));
        Assert.DoesNotContain(PulledMenuLabel, MenuText(component), StringComparison.Ordinal);

        ActionButton(component).Click();
        component.WaitForAssertion(() => Assert.Equal("Pull latest", ActionButton(component).TextContent.Trim()));

        ActionButton(component).Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(1, harness.Git.PullsRequested);
            Assert.Equal(
                "Pulled the latest JSdotNet/Backlog into the clone.",
                component.Find("[data-testid='knowledge-update-status']").TextContent.Trim());

            // Back to offering a check, and the count gone: what was behind is not
            // behind any more, so a badge saying otherwise would outlive its fact.
            Assert.Equal("Check now", ActionButton(component).TextContent.Trim());
            Assert.Empty(component.FindAll("[data-testid='knowledge-update-behind']"));

            // The point of the whole feature: the menu is built from the folder the
            // pull replaced, so what the pull brought in is on screen. The guard
            // that makes the menu cheap on every parameter set is the same guard
            // that would have left this stale.
            Assert.Contains(PulledMenuLabel, MenuText(component), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_refused_pull_says_why_and_leaves_the_button_offering_a_check()
    {
        await using var harness = CreateHarness();
        harness.Git.NextCheck = Check(LocalGitRepositoryCurrency.Behind, behind: 2, "2 commits behind origin/main.");
        harness.Git.NextPull = LocalGitRepositoryPullResult.Failed(
            "There are local changes in the clone. Commit or discard them before pulling the latest version.");

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".knowledge-menu__item")));

        ActionButton(component).Click();
        component.WaitForAssertion(() => Assert.Equal("Pull latest", ActionButton(component).TextContent.Trim()));

        ActionButton(component).Click();

        component.WaitForAssertion(() =>
        {
            var status = component.Find("[data-testid='knowledge-update-status']");
            Assert.Contains("Commit or discard them", status.TextContent, StringComparison.Ordinal);
            Assert.Contains("knowledge-stack__update-status--blocked", status.GetAttribute("class"));
            Assert.Equal("Check now", ActionButton(component).TextContent.Trim());
        });

        Assert.Empty(component.FindAll("[data-testid='knowledge-update-behind']"));
    }

    [Fact]
    public async Task A_clone_that_cannot_be_pulled_says_why_and_never_offers_to_try()
    {
        await using var harness = CreateHarness();
        harness.Git.NextCheck = Check(
            LocalGitRepositoryCurrency.Diverged,
            behind: 1,
            "This clone has diverged from origin/main: 2 commits here that the remote does not have, and 1 the other way. Resolve it in git.");

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".knowledge-menu__item")));
        ActionButton(component).Click();

        component.WaitForAssertion(() =>
        {
            var status = component.Find("[data-testid='knowledge-update-status']");
            Assert.Contains("diverged", status.TextContent, StringComparison.Ordinal);
            Assert.Contains("knowledge-stack__update-status--blocked", status.GetAttribute("class"));
            Assert.Equal("Check now", ActionButton(component).TextContent.Trim());
        });

        Assert.Empty(component.FindAll("[data-testid='knowledge-update-behind']"));
        Assert.Equal(0, harness.Git.PullsRequested);
    }

    [Fact]
    public async Task The_version_control_renders_the_librarys_button_rather_than_a_bare_one()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".knowledge-menu__item")));

        // AppButton with Variant.None wearing the pane's own class: a real button
        // element with a type, so the strip keeps the library's keyboard and focus
        // behaviour instead of a hand-rolled copy of it.
        var button = ActionButton(component);
        Assert.Equal("BUTTON", button.TagName);
        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Equal("knowledge-stack__update-toggle", button.GetAttribute("class"));
    }

    private static LocalGitRepositoryUpdateCheck Check(LocalGitRepositoryCurrency currency, int behind, string summary) =>
        new(currency, currency is LocalGitRepositoryCurrency.Diverged ? 2 : 0, behind, false, "origin/main", summary);

    private static AngleSharp.Dom.IElement ActionButton(IRenderedComponent<KnowledgePane> component) =>
        component.Find("[data-testid='knowledge-update-action']");

    private static string MenuText(IRenderedComponent<KnowledgePane> component) =>
        string.Join(" ", component.FindAll(".knowledge-menu__item").Select(item => item.TextContent));

    private static Harness CreateHarness(bool cloned = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-knowledge-pane-update-tests", Guid.NewGuid().ToString("n"));
        var clone = Path.Combine(root, "clone");
        WriteKnowledgeFolders(clone);

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));

        _ = featureSettings.SetEnabled(KnowledgeFeatures.KnowledgeSections, true);
        _ = featureSettings.SetEnabled(KnowledgeFeatures.RepositoryKnowledge, true);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var configuredRepository = Assert.Single(repositories) with
        {
            CloneDirectory = cloned ? clone : null,
            KnowledgeFolders = KnowledgeFolderSetting.Defaults()
        };
        Assert.Null(gitHubSettings.SetRepositories([configuredRepository]));

        var git = new ScriptedGitRepositoryService();

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(featureSettings);
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(gitHubSettings, store));
        context.Services.AddSingleton<ILocalGitRepositoryService>(git);
        context.Services.AddSingleton<InstructionSourceDiscovery>();
        context.Services.AddSingleton<KnowledgeMenu>();
        context.Services.AddSingleton<KnowledgeScope>();
        context.Services.AddSingleton<KnowledgeUpdateService>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<KnowledgeFolderOpenService>();
        // The pane renders a panel, and a panel renders the shared editing
        // surface, which writes. Composing the pane means composing the writer.
        context.Services.AddSingleton<KnowledgeChapterWriter>();

        return new Harness(root, clone, context, configuredRepository.Alias, git);
    }

    /// <summary>
    /// Enough of a knowledge base for every section to resolve and for the
    /// instructions tree — the section the pane opens on — to have something in it.
    /// </summary>
    private static void WriteKnowledgeFolders(string clone)
    {
        Write(clone, ".github/instructions/already-here.instructions.md", "---\napplyTo: \"**\"\n---\n\n# Already here\n");
        Write(clone, ".domain/context-map.md", "# Context map\n");
        Write(clone, ".arc42/01-introduction-and-goals.md", "# Introduction\n");
        Write(clone, ".tech/technology-graph.md", "# Technology graph\n");
        Write(clone, ".design/README.md", "# Design\n");
    }

    private static void Write(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed record Harness(
        string Root,
        string CloneDirectory,
        BunitContext Context,
        string RepositoryAlias,
        ScriptedGitRepositoryService Git) : IAsyncDisposable
    {
        public IRenderedComponent<KnowledgePane> Render() =>
            Context.Render<KnowledgePane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

        public IRenderedComponent<KnowledgePane> RenderWithoutRepository() =>
            Context.Render<KnowledgePane>(parameters => parameters.Add(pane => pane.RepositoryAlias, null));

        /// <summary>What the fake pull leaves behind, standing in for a chapter
        /// somebody else wrote and pushed.</summary>
        public void WritePulledInstruction() =>
            Write(CloneDirectory, $".github/instructions/{PulledFileName}", "---\napplyTo: \"**\"\n---\n\n# Pulled\n");

        /// <summary>Awaited for the reason <c>KnowledgePaneOpenErrorTests</c>
        /// spells out: the editing surface the pane renders writes its last pending
        /// save on the way out, and the folder delete must not race it.</summary>
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
