using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Switching the source has to be visible even when it changes nothing on
/// screen. A clone and a branch at the same commit hold the same files, so the
/// panels re-read and draw the same chapters; the one trace of the switch was
/// the status control giving way to a badge, and a reader who did not know to
/// look for that saw a select that did nothing. The sentence under the strip is
/// the confirmation, and these assert it on the rendered markup — including that
/// it makes way for the version answer rather than stacking under it.
/// </summary>
public sealed class DevbookPaneSourceStatusTests
{
    [Fact]
    public async Task Nothing_is_said_about_the_source_until_somebody_switches_it()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));
        Assert.Empty(component.FindAll("[data-testid='devbook-source-status']"));
    }

    [Fact]
    public async Task Switching_to_a_branch_says_the_branch_is_being_read_as_a_snapshot()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        SourceSelect(component).Change(string.Empty);

        component.WaitForAssertion(() =>
        {
            var status = component.Find("[data-testid='devbook-source-status']");
            Assert.Equal("status", status.GetAttribute("role"));
            Assert.StartsWith("Reading the default branch as a read-only snapshot.", status.TextContent.Trim(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Switching_back_to_the_clone_says_the_clone_is_being_read()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        SourceSelect(component).Change(string.Empty);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-source-status']")));

        SourceSelect(component).Change(DevbookSourceSelection.LocalFolderValue);

        component.WaitForAssertion(() => Assert.Equal(
            "Reading the local clone. Devbook can be edited here.",
            component.Find("[data-testid='devbook-source-status']").TextContent.Trim()));
    }

    /// <summary>The row is one row. A check answers the question the switch
    /// sentence was standing in for, so the answer replaces it.</summary>
    [Fact]
    public async Task A_version_check_takes_the_row_back_from_the_switch_sentence()
    {
        await using var harness = CreateHarness();
        harness.Git.NextCheck = new LocalGitRepositoryUpdateCheck(
            LocalGitRepositoryCurrency.UpToDate, 0, 0, false, "origin/main", "On the latest version of origin/main.");

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        SourceSelect(component).Change(DevbookSourceSelection.LocalFolderValue);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-source-status']")));

        component.Find("[data-testid='devbook-update-action']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(
                "On the latest version of origin/main.",
                component.Find("[data-testid='devbook-update-status']").TextContent.Trim());
            Assert.Empty(component.FindAll("[data-testid='devbook-source-status']"));
        });
    }

    /// <summary>The sentence names a repository, so it leaves with it.</summary>
    [Fact]
    public async Task Moving_the_scope_drops_the_switch_sentence()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        SourceSelect(component).Change(DevbookSourceSelection.LocalFolderValue);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-source-status']")));

        component.Render(parameters => parameters.Add(pane => pane.RepositoryAlias, null));

        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='devbook-source-status']")));
    }

    private static AngleSharp.Dom.IElement SourceSelect(IRenderedComponent<DevbookPane> component) =>
        component.Find("[data-testid='devbook-source-select'] select");

    private static Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-devbook-pane-source-status-tests", Guid.NewGuid().ToString("n"));
        var clone = Path.Combine(root, "clone");
        Write(clone, ".github/instructions/already-here.instructions.md", "---\napplyTo: \"**\"\n---\n\n# Already here\n");
        Write(clone, ".arc42/01-introduction-and-goals.md", "# Introduction\n");

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));

        _ = featureSettings.SetEnabled(DevbookFeatures.DevbookSections, true);
        _ = featureSettings.SetEnabled(DevbookFeatures.RepositoryDevbook, true);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var configuredRepository = Assert.Single(repositories) with
        {
            CloneDirectory = clone,
            DevbookFolders = DevbookFolderSetting.Defaults()
        };
        Assert.Null(gitHubSettings.SetRepositories([configuredRepository]));

        var git = new ScriptedGitRepositoryService();

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(featureSettings);
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(gitHubSettings, store));
        context.Services.AddSingleton<ILocalGitRepositoryService>(git);
        context.Services.AddSingleton<InstructionSourceDiscovery>();
        context.Services.AddSingleton<DevbookMenu>();
        context.Services.AddSingleton<DevbookScope>();
        // The pane publishes its open chapter here for the Ask AI source; a pane
        // rendered without it would fail on inject, as the application hosts would.
        context.Services.AddScoped<DevbookOpenChapter>();
        context.Services.AddSingleton<DevbookUpdateService>();
        context.Services.AddSingleton<IGitHubBranchCatalog>(new StubBranchCatalog());
        context.Services.AddSingleton<DevbookSourceSelection>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<DevbookFolderOpenService>();
        context.Services.AddSingleton<DevbookChapterWriter>();

        return new Harness(root, context, configuredRepository.Alias, git);
    }

    private static void Write(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed record Harness(
        string Root,
        BunitContext Context,
        string RepositoryAlias,
        ScriptedGitRepositoryService Git) : IAsyncDisposable
    {
        public IRenderedComponent<DevbookPane> Render() =>
            Context.Render<DevbookPane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

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
