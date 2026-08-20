using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The one place a repository's colour is chosen.
/// <para>
/// Asserted against the settings store rather than against what the swatches are
/// showing, because the store is what every other surface reads — a picker that lit up
/// the right swatch and wrote nothing would leave the filter, the list and the roadmap
/// exactly as they were.
/// </para>
/// </summary>
public sealed class SettingsRepositoryColourTests
{
    [Fact]
    public void ChoosingAColourStoresItStraightAway()
    {
        using var settings = RenderSettings();
        OpenRepositoriesTab(settings.Component);

        settings.Component.Find("[data-testid='repo-colour-3']").Click();

        // No save button, the way every other choice on this screen works.
        Assert.Equal(3, settings.GitHub.Current.Find("backlog")!.Colour);
    }

    [Fact]
    public void ResetGivesTheChoiceBackWithoutLeavingTheRepositoryColourless()
    {
        using var settings = RenderSettings();
        OpenRepositoriesTab(settings.Component);
        settings.Component.Find("[data-testid='repo-colour-3']").Click();

        settings.Component.Find("[data-testid='repo-colour-reset']").Click();

        Assert.Null(settings.GitHub.Current.Find("backlog")!.Colour);

        // "Default" is a hue picked for you, not the absence of one.
        Assert.Equal(1, settings.GitHub.Current.ColourFor("backlog"));
    }

    [Fact]
    public void ResetIsOfferedOnlyWhenThereIsAChoiceToGiveBack()
    {
        using var settings = RenderSettings();
        OpenRepositoriesTab(settings.Component);

        Assert.True(settings.Component.Find("[data-testid='repo-colour-reset']").HasAttribute("disabled"));

        settings.Component.Find("[data-testid='repo-colour-2']").Click();

        settings.Component.WaitForAssertion(() =>
            Assert.False(settings.Component.Find("[data-testid='repo-colour-reset']").HasAttribute("disabled")));
    }

    [Fact]
    public void ExactlyTheSanctionedFiveAreOffered()
    {
        using var settings = RenderSettings();
        OpenRepositoriesTab(settings.Component);

        for (var colour = 1; colour <= RepositoryColours.Available; colour++)
        {
            Assert.NotNull(settings.Component.Find($"[data-testid='repo-colour-{colour}']"));
        }

        // A sixth swatch would be product code inventing a colour, which
        // .design/color-scheme.md does not let it do.
        Assert.Empty(settings.Component.FindAll($"[data-testid='repo-colour-{RepositoryColours.Available + 1}']"));
    }

    [Fact]
    public void TheChosenColourIsSaidInWordsAndInState()
    {
        using var settings = RenderSettings();
        OpenRepositoriesTab(settings.Component);

        // The swatches are the only control on this screen carrying its meaning in
        // colour, so a reader who cannot see which one is ringed still has to be able
        // to tell what they have got.
        Assert.Contains("Default", settings.Component.Find("[data-testid='repo-colour-status']").TextContent);

        settings.Component.Find("[data-testid='repo-colour-4']").Click();

        settings.Component.WaitForAssertion(() =>
        {
            Assert.Equal("true", settings.Component.Find("[data-testid='repo-colour-4']").GetAttribute("aria-checked"));
            Assert.Equal("false", settings.Component.Find("[data-testid='repo-colour-3']").GetAttribute("aria-checked"));
            Assert.Contains("Colour 4", settings.Component.Find("[data-testid='repo-colour-status']").TextContent);
        });
    }

    private static void OpenRepositoriesTab(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "Repositories").Click();

    private static SettingsRenderContext RenderSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-colour-tests", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(BacklogFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);

        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        Assert.Null(githubSettings.SetRepositories(repositories));

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton(new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json")));
        context.Services.AddSingleton(new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json")));
        context.Services.AddSingleton(new GitHubIntegration(githubSettings, new NoGitHub(), new NoProbe()));
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(githubSettings, store));

        return new SettingsRenderContext(root, context, context.Render<Settings>(), githubSettings);
    }

    /// <summary>Nothing here reaches GitHub — the colour is a local choice about how the
    /// repository is drawn, not a fact fetched from it.</summary>
    private sealed class NoGitHub : IGitHubClient
    {
        public Task<GitHubIssue> CreateIssueAsync(
            GitHubRepositoryRef repository,
            string title,
            string? body,
            IEnumerable<string>? labels = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(
            GitHubRepositoryRef repository,
            int number,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not connected."));

        public void Invalidate()
        {
        }
    }

    private sealed record SettingsRenderContext(
        string Root,
        BunitContext TestContext,
        IRenderedComponent<Settings> Component,
        GitHubSettingsStore GitHub) : IDisposable
    {
        public void Dispose()
        {
            TestContext.Dispose();

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
