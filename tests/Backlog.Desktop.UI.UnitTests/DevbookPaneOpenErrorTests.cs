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
public sealed class DevbookPaneOpenErrorTests
{
    [Fact]
    public async Task Devbook_pane_shows_no_open_error_before_a_folder_open_fails()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));
        Assert.Empty(component.FindAll("[data-testid='devbook-menu-open-error']"));
    }

    [Fact]
    public async Task Devbook_pane_shows_the_real_reason_when_opening_a_folder_fails()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        var openButton = component.Find(".devbook-stack__menu-heading [data-testid='devbook-open-vscode-button']");
        var folderLabel = FolderLabelFrom(openButton.GetAttribute("title"));
        var launcherFailure = await LauncherFailureMessageAsync();
        openButton.Click();

        component.WaitForAssertion(() =>
        {
            var error = component.Find("[data-testid='devbook-menu-open-error']").TextContent;
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
    /// so the assertion follows the message the pane actually receives.
    /// <para>The launcher throws the adapter's neutral
    /// <see cref="FolderEditorLaunchException"/>; the message survives the
    /// service's translation into <see cref="DevbookFolderOpenException"/>
    /// unchanged, which is what lets this be the pane's expected text. That
    /// preservation is asserted directly in
    /// <c>DevbookFolderOpenServiceTests</c>.</para></summary>
    private static async Task<string> LauncherFailureMessageAsync()
    {
        var failure = await Assert.ThrowsAsync<FolderEditorLaunchException>(
            () => new UnsupportedFolderEditorLauncher().OpenFolderAsync("unused"));

        return failure.Message;
    }

    private static Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-devbook-pane-tests", Guid.NewGuid().ToString("n"));
        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));

        _ = featureSettings.SetEnabled(DevbookFeatures.DevbookSections, true);
        _ = featureSettings.SetEnabled(DevbookFeatures.RepositoryDevbook, true);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories);
        var configuredRepository = repository with
        {
            CloneDirectory = RepositoryRoot.Root.FullName,
            DevbookFolders = DevbookFolderSetting.Defaults()
        };
        Assert.Null(gitHubSettings.SetRepositories([configuredRepository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(featureSettings);
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(gitHubSettings, store));
        context.Services.AddSingleton<InstructionSourceDiscovery>();
        context.Services.AddSingleton<DevbookMenu>();
        context.Services.AddSingleton<DevbookScope>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<DevbookUpdateService>();
        context.Services.AddSingleton<IGitHubBranchCatalog>(new StubBranchCatalog());
        context.Services.AddSingleton<DevbookSourceSelection>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<DevbookFolderOpenService>();
        // The pane renders a panel, and a panel renders the shared editing
        // surface, which writes. Composing the pane means composing the writer.
        context.Services.AddSingleton<DevbookChapterWriter>();

        return new Harness(root, context, configuredRepository.Alias);
    }

    private sealed record Harness(string Root, BunitContext Context, string RepositoryAlias) : IAsyncDisposable
    {
        public IRenderedComponent<DevbookPane> Render() =>
            Context.Render<DevbookPane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

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
