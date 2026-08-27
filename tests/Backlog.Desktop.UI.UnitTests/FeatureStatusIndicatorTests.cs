using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A feature's maturity, in the two places it is drawn.
///
/// <para>The two are deliberately not the same rule, and that difference is most
/// of what these tests are for. The settings row <em>is</em> the switch, so it
/// says what the feature is whether or not it is on. Everywhere else is a way in
/// to something you can click, so a feature that is switched off has nothing to
/// flag — an indicator on a control nobody can reach would be noise about a
/// choice already made.</para>
/// </summary>
public sealed class FeatureStatusIndicatorTests
{
    [Fact]
    public void A_released_feature_says_nothing_anywhere()
    {
        // The default, and the reason the enum's first value is Released: a
        // feature nobody has classified is an ordinary feature.
        Assert.Equal(string.Empty, AppFeatureStatusBadge.Slug(AppFeatureStatus.Released));
        Assert.Null(AppFeatureStatusBadge.Title(AppFeatureStatus.Released));

        var released = Assert.Single(AppFeatures.All, feature => feature.Key == BacklogFeatures.Backlog);
        Assert.Equal(AppFeatureStatus.Released, released.Status);
        Assert.Equal(string.Empty, AppFeatureStatusBadge.Slug(released.Status));
    }

    [Theory]
    [InlineData(AppFeatureStatus.Dev, "dev")]
    [InlineData(AppFeatureStatus.Beta, "beta")]
    public void An_unfinished_feature_maps_onto_one_badge_modifier_and_one_sentence(
        AppFeatureStatus status,
        string expectedSlug)
    {
        Assert.Equal(expectedSlug, AppFeatureStatusBadge.Slug(status));
        Assert.False(string.IsNullOrWhiteSpace(AppFeatureStatusBadge.Title(status)));
    }

    [Fact]
    public void An_entry_point_flags_an_unfinished_feature_only_while_it_is_switched_on()
    {
        using var features = NewFeatureStore();

        _ = features.Store.SetEnabled(AppFeatures.InboxPane, true);
        Assert.Equal("dev", AppFeatureStatusBadge.SlugFor(AppFeatures.All, features.Store, AppFeatures.InboxPane));
        Assert.NotNull(AppFeatureStatusBadge.TitleFor(AppFeatures.All, features.Store, AppFeatures.InboxPane));

        _ = features.Store.SetEnabled(AppFeatures.InboxPane, false);
        Assert.Equal(string.Empty, AppFeatureStatusBadge.SlugFor(AppFeatures.All, features.Store, AppFeatures.InboxPane));
        Assert.Null(AppFeatureStatusBadge.TitleFor(AppFeatures.All, features.Store, AppFeatures.InboxPane));
    }

    [Fact]
    public void An_enabled_released_feature_still_flags_nothing()
    {
        using var features = NewFeatureStore();

        // Repository knowledge rather than GitHub integration: the latter moved to
        // Dev when the catalog was regrouped, and a test that quietly follows a
        // status change is testing nothing.
        _ = features.Store.SetEnabled(KnowledgeFeatures.RepositoryKnowledge, true);

        Assert.Equal(
            AppFeatureStatus.Released,
            Assert.Single(AppFeatures.All, f => f.Key == KnowledgeFeatures.RepositoryKnowledge).Status);
        Assert.Equal(
            string.Empty,
            AppFeatureStatusBadge.SlugFor(AppFeatures.All, features.Store, KnowledgeFeatures.RepositoryKnowledge));
    }

    /// <summary>A decoration fails quietly. <c>IsEnabled</c> throws for a key no
    /// catalog defines and is the one that has to — a badge going missing is not
    /// worth taking the screen down for.</summary>
    [Fact]
    public void A_key_the_catalog_does_not_define_flags_nothing_rather_than_throwing()
    {
        using var features = NewFeatureStore();

        Assert.Equal(string.Empty, AppFeatureStatusBadge.SlugFor(AppFeatures.All, features.Store, "not-a-feature"));
        Assert.Null(AppFeatureStatusBadge.TitleFor(AppFeatures.All, features.Store, "not-a-feature"));
    }

    [Fact]
    public void The_settings_row_shows_the_status_of_a_feature_that_is_switched_off()
    {
        // Inbox is Dev and ships off. The badge is what tells somebody looking at
        // the switch why they might not want to flip it.
        using var context = RenderSettings(features => features.SetEnabled(AppFeatures.InboxPane, false));

        var badge = context.Component.Find($"[data-testid='feature-status-{AppFeatures.InboxPane}']");

        Assert.Contains("badge--feature-dev", badge.ClassList);
        Assert.Equal("DEV", badge.TextContent.ToUpperInvariant());
    }

    [Fact]
    public void The_settings_rows_carry_one_badge_per_unfinished_feature_and_none_for_the_rest()
    {
        using var context = RenderSettings();

        foreach (var feature in AppFeatures.All)
        {
            var badges = context.Component.FindAll($"[data-testid='feature-status-{feature.Key}']");

            if (feature.Status == AppFeatureStatus.Released)
            {
                Assert.Empty(badges);
                continue;
            }

            var badge = Assert.Single(badges);
            Assert.Contains($"badge--feature-{AppFeatureStatusBadge.Slug(feature.Status)}", badge.ClassList);
            Assert.Equal(AppFeatureStatusBadge.Title(feature.Status), badge.GetAttribute("title"));
        }
    }

    /// <summary>The badge sits on the label's line rather than under the
    /// description, and outside the span the checkbox is named by — so the switch
    /// is still called "Inbox pane" and not "Inbox pane dev".</summary>
    [Fact]
    public void The_settings_badge_qualifies_the_name_without_becoming_part_of_it()
    {
        using var context = RenderSettings();

        var badge = context.Component.Find($"[data-testid='feature-status-{AppFeatures.InboxPane}']");
        var row = badge.ParentElement!;

        Assert.Contains("feature-flag__title", row.QuerySelector(".feature-flag__title")!.ClassList);
        Assert.Equal("Inbox pane", row.QuerySelector(".feature-flag__title")!.TextContent);
        Assert.DoesNotContain("dev", row.QuerySelector(".feature-flag__title")!.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_settings_screen_draws_one_heading_per_section_with_its_own_switches()
    {
        using var context = RenderSettings();

        var headings = context.Component.FindAll(".feature-group__title").Select(h => h.TextContent.Trim()).ToList();
        var grids = context.Component.FindAll(".feature-flags");

        Assert.Equal(AppFeatures.Sections.Select(s => s.Title), headings);
        Assert.Equal(AppFeatures.Sections.Count, grids.Count);

        for (var i = 0; i < AppFeatures.Sections.Count; i++)
        {
            var section = AppFeatures.Sections[i];
            var titles = grids[i].QuerySelectorAll(".feature-flag__title").Select(t => t.TextContent.Trim());

            Assert.Equal(section.Features.Select(f => f.Name), titles);
        }
    }

    private static FeatureStore NewFeatureStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-feature-status-tests", Guid.NewGuid().ToString("n"));

        return new FeatureStore(root, new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features.json")));
    }

    private static SettingsRenderContext RenderSettings(Action<AppFeatureSettingsStore>? configureFeatures = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-feature-status-tests", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        configureFeatures?.Invoke(features);

        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, _) = GitHubSettings.ParseText("JSdotNet/Backlog");
        _ = githubSettings.SetRepositories(repositories);

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton(new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json")));
        context.Services.AddSingleton(new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json")));
        context.Services.AddSingleton(new GitHubIntegration(githubSettings, new StubGitHubClient(), new StubProbe()));
        context.Services.AddSingleton<FeedbackReporter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(githubSettings, store));

        return new SettingsRenderContext(root, context, context.Render<Settings>());
    }

    private sealed record FeatureStore(string Root, AppFeatureSettingsStore Store) : IDisposable
    {
        public void Dispose() => DeleteRoot(Root);
    }

    private sealed record SettingsRenderContext(
        string Root,
        BunitContext TestContext,
        IRenderedComponent<Settings> Component) : IDisposable
    {
        public void Dispose()
        {
            TestContext.Dispose();
            DeleteRoot(Root);
        }
    }

    private static void DeleteRoot(string root)
    {
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class StubGitHubClient : IGitHubClient
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

        public Task<GitHubUploadedFile> UploadFileAsync(
            GitHubRepositoryRef repository,
            string path,
            string branch,
            byte[] content,
            string commitMessage,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not connected."));

        public void Invalidate()
        {
        }
    }
}
