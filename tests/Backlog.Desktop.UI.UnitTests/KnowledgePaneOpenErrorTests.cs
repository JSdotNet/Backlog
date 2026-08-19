using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The pane's folder-open error is a field, and a Razor attribute that forgets
/// its <c>@</c> binds the field <em>name</em> instead. That reads as an error
/// message on screen, so both states are asserted on rendered text: silent when
/// nothing failed, and the real reason when opening a folder did.
/// </summary>
public sealed class KnowledgePaneOpenErrorTests
{
    [Fact]
    public void Knowledge_pane_shows_no_open_error_before_a_folder_open_fails()
    {
        using var harness = CreateHarness();

        var component = harness.Render();

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".knowledge-menu__item")));
        Assert.Empty(component.FindAll("[data-testid='knowledge-menu-open-error']"));
    }

    [Fact]
    public async Task Knowledge_pane_shows_the_real_reason_when_opening_a_folder_fails()
    {
        using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".knowledge-menu__item")));

        var openButton = component.Find(".knowledge-stack__menu-heading [data-testid='knowledge-open-vscode-button']");
        var folderLabel = FolderLabelFrom(openButton.GetAttribute("title"));
        var launcherFailure = await LauncherFailureMessageAsync();
        openButton.Click();

        component.WaitForAssertion(() =>
        {
            var error = component.Find("[data-testid='knowledge-menu-open-error']").TextContent;
            Assert.Contains($"Couldn't open {folderLabel} in VS Code.", error, StringComparison.Ordinal);
            Assert.Contains(launcherFailure, error, StringComparison.Ordinal);
            Assert.DoesNotContain("_folderOpenError", error, StringComparison.Ordinal);
        });
    }

    /// <summary>The heading's open button names the folder it opens ("Open X in
    /// VS Code"), which is the same label the error message is built from — so
    /// the expected text is read back from the render instead of guessed.</summary>
    private static string FolderLabelFrom(string? openButtonTitle)
    {
        Assert.NotNull(openButtonTitle);
        Assert.StartsWith("Open ", openButtonTitle, StringComparison.Ordinal);
        Assert.EndsWith(" in VS Code", openButtonTitle, StringComparison.Ordinal);

        return openButtonTitle["Open ".Length..^" in VS Code".Length];
    }

    /// <summary>Taken from the launcher the harness registers rather than copied,
    /// so the assertion follows the message the pane actually receives.</summary>
    private static async Task<string> LauncherFailureMessageAsync()
    {
        var failure = await Assert.ThrowsAsync<KnowledgeFolderOpenException>(
            () => new UnsupportedFolderEditorLauncher().OpenFolderAsync("unused"));

        return failure.Message;
    }

    private static Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-knowledge-pane-tests", Guid.NewGuid().ToString("n"));
        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));

        _ = featureSettings.SetEnabled(KnowledgeFeatures.KnowledgeSections, true);
        _ = featureSettings.SetEnabled(KnowledgeFeatures.RepositoryKnowledge, true);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories);
        var configuredRepository = repository with
        {
            CloneDirectory = RepositoryRoot.Root.FullName,
            KnowledgeFolders = KnowledgeFolderSetting.Defaults()
        };
        Assert.Null(gitHubSettings.SetRepositories([configuredRepository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(featureSettings);
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(gitHubSettings, store));
        context.Services.AddSingleton<InstructionSourceDiscovery>();
        context.Services.AddSingleton<KnowledgeMenu>();
        context.Services.AddSingleton<KnowledgeScope>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<KnowledgeFolderOpenService>();

        return new Harness(root, context, configuredRepository.Alias);
    }

    private sealed record Harness(string Root, BunitContext Context, string RepositoryAlias) : IDisposable
    {
        public IRenderedComponent<KnowledgePane> Render() =>
            Context.Render<KnowledgePane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

        public void Dispose()
        {
            Context.Dispose();

            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
