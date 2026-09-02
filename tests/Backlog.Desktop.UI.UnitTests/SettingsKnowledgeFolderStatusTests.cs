using AngleSharp.Dom;

using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The repository's knowledge-base sections used to restate the configured path
/// and stop there, which reads as confirmation: a section pointed at a folder
/// that was never in the clone looked exactly like one that was.
/// <para>
/// The existence check is asked of <see cref="IKnowledgeFolderSource"/> rather
/// than of the file system here, so the screen reports what a reader of that
/// folder would actually get — including the two answers that are not "missing":
/// no clone directory yet, and a path that cannot be resolved at all.
/// </para>
/// </summary>
public sealed class SettingsKnowledgeFolderStatusTests
{
    [Fact]
    public void The_architecture_section_is_labelled_for_what_it_holds()
    {
        using var settings = RenderSettings(cloneDirectory: CloneDirectory.WithArchitectureFolder);
        OpenRepositoriesTab(settings.Component);

        Assert.Equal("Architecture", Row(settings.Component, ".arc42").QuerySelector(".checkbox__label")!.TextContent);
    }

    /// <summary>
    /// The found state is the badge and nothing else. A sentence saying the same
    /// thing under a field that already shows it was the redundancy, so what is
    /// pinned here is both halves of the change: the badge is present and named,
    /// and the words are gone.
    /// </summary>
    [Fact]
    public void A_section_whose_folder_is_in_the_clone_is_marked_in_the_field_rather_than_in_words()
    {
        using var settings = RenderSettings(cloneDirectory: CloneDirectory.WithArchitectureFolder);
        OpenRepositoriesTab(settings.Component);

        var row = Row(settings.Component, ".arc42");
        var marker = Marker(row)!;

        Assert.Equal("img", marker.GetAttribute("role"));
        Assert.Equal("Folder found.", marker.GetAttribute("aria-label"));
        Assert.Contains("knowledge-folder__marker--found", marker.ClassName, StringComparison.Ordinal);

        // Inside the input's own box, not beside it: that is what makes it read as
        // the field's state rather than as a control of its own.
        Assert.Equal("knowledge-folder-path-input", marker.ParentElement!.ParentElement!.QuerySelector("input")!.GetAttribute("data-testid"));

        var status = Status(settings.Component, ".arc42");
        Assert.Equal("found", status.GetAttribute("data-folder-state"));
        Assert.Equal("Uses .arc42 at the repository root.", status.TextContent.Trim());
        Assert.DoesNotContain("Folder found", status.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_whose_folder_is_absent_names_the_path_that_was_looked_at()
    {
        using var settings = RenderSettings(cloneDirectory: CloneDirectory.Empty);
        OpenRepositoriesTab(settings.Component);

        var marker = Marker(Row(settings.Component, ".arc42"))!;
        Assert.Equal("Folder not found.", marker.GetAttribute("aria-label"));
        Assert.Contains("knowledge-folder__marker--missing", marker.ClassName, StringComparison.Ordinal);

        var status = Status(settings.Component, ".arc42");

        Assert.Equal("missing", status.GetAttribute("data-folder-state"));
        // The space between the two clauses is asserted, not just each clause: Razor
        // drops whitespace-only text between elements, so the sentences run together
        // unless the separator is written out.
        Assert.Contains("at the repository root. Architecture knowledge folder was not found at", status.TextContent, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(settings.CloneDirectory!, ".arc42"), status.TextContent, StringComparison.Ordinal);
    }

    /// <summary>The two states have to differ by more than a fill colour, or the
    /// row says nothing at all to a reader who cannot tell red from green.</summary>
    [Fact]
    public void The_two_marker_states_differ_by_glyph_as_well_as_by_colour()
    {
        using var found = RenderSettings(cloneDirectory: CloneDirectory.WithArchitectureFolder);
        using var missing = RenderSettings(cloneDirectory: CloneDirectory.Empty);
        OpenRepositoriesTab(found.Component);
        OpenRepositoriesTab(missing.Component);

        var foundGlyph = Marker(Row(found.Component, ".arc42"))!.TextContent.Trim();
        var missingGlyph = Marker(Row(missing.Component, ".arc42"))!.TextContent.Trim();

        Assert.NotEmpty(foundGlyph);
        Assert.NotEqual(foundGlyph, missingGlyph);
    }

    /// <summary>
    /// Nothing has been looked at yet when there is no clone, so saying the folder
    /// was not found would send somebody hunting for a folder rather than to the
    /// field above that is actually empty.
    /// </summary>
    [Fact]
    public void A_repository_with_no_clone_directory_is_pointed_at_the_clone_directory()
    {
        using var settings = RenderSettings(cloneDirectory: CloneDirectory.None);
        OpenRepositoriesTab(settings.Component);

        var status = Status(settings.Component, ".arc42");

        Assert.Equal("missing", status.GetAttribute("data-folder-state"));
        Assert.Contains("local clone directory", status.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("was not found at", status.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_turned_off_is_not_checked_for_a_folder_at_all()
    {
        using var settings = RenderSettings(cloneDirectory: CloneDirectory.Empty);
        OpenRepositoriesTab(settings.Component);

        Row(settings.Component, ".arc42").QuerySelector("[data-testid='knowledge-folder-enabled']")!.Change(false);

        settings.Component.WaitForAssertion(() =>
        {
            var row = Row(settings.Component, ".arc42");
            var status = Status(settings.Component, ".arc42");

            Assert.False(status.HasAttribute("data-folder-state"));
            Assert.Equal("Off for this repository.", status.TextContent.Trim());

            // A disabled field showing a state would be reporting on a folder
            // nobody asked about.
            Assert.Null(Marker(row));
        });
    }

    /// <summary>Instructions have no folder of their own to check - they follow
    /// Backlog's standard discovery - so the row keeps its original sentence, and
    /// has no path field to mark either way.</summary>
    [Fact]
    public void The_instructions_section_keeps_its_own_sentence()
    {
        using var settings = RenderSettings(cloneDirectory: CloneDirectory.Empty);
        OpenRepositoriesTab(settings.Component);

        var status = Status(settings.Component, "instructions");

        Assert.False(status.HasAttribute("data-folder-state"));
        Assert.Equal("Uses standard repository instruction discovery.", status.TextContent.Trim());
        Assert.Null(Marker(Row(settings.Component, "instructions")));
    }

    private enum CloneDirectory
    {
        None,
        Empty,
        WithArchitectureFolder
    }

    private static void OpenRepositoriesTab(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "Repositories").Click();

    /// <summary>Keyed on the section's own key chip rather than on row order, so a
    /// section added to the defaults does not move the assertions.</summary>
    private static IElement Row(IRenderedComponent<Settings> component, string key) =>
        component.FindAll("[data-testid='knowledge-folder-row']")
            .Single(row => row.QuerySelector(".knowledge-folder__toggle code")!.TextContent == key);

    private static IElement Status(IRenderedComponent<Settings> component, string key) =>
        Row(component, key).QuerySelector("[data-testid='knowledge-folder-status']")!;

    /// <summary>Nullable on purpose: absence is the assertion for a row that is off
    /// and for the one with no folder of its own.</summary>
    private static IElement? Marker(IElement row) => row.QuerySelector("[data-testid='knowledge-folder-marker']");

    private static SettingsRenderContext RenderSettings(CloneDirectory cloneDirectory)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-knowledge-status-tests", Guid.NewGuid().ToString("n"));

        string? clone = null;
        if (cloneDirectory is not CloneDirectory.None)
        {
            clone = Path.Combine(root, "clone");
            Directory.CreateDirectory(clone);
            if (cloneDirectory is CloneDirectory.WithArchitectureFolder) Directory.CreateDirectory(Path.Combine(clone, ".arc42"));
        }

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);

        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        Assert.Null(githubSettings.SetRepositories(repositories));
        if (clone is not null) Assert.Null(githubSettings.SetCloneDirectory("backlog", clone));

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<ITasksRefreshSettings>(
            new TasksRefreshSettingsStore(Path.Combine(root, "refresh", "refresh.json")));
        context.Services.AddSingleton(new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json")));
        context.Services.AddSingleton(new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json")));
        context.Services.AddSingleton(new GitHubIntegration(githubSettings, new UnreachableGitHub(), new NotConnectedProbe()));
        context.Services.AddSingleton<FeedbackReporter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(githubSettings, store));

        return new SettingsRenderContext(root, clone, context, context.Render<Settings>());
    }

    private sealed class UnreachableGitHub : IGitHubClient
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

    private sealed class NotConnectedProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not connected."));

        public void Invalidate()
        {
        }
    }

    private sealed record SettingsRenderContext(
        string Root,
        string? CloneDirectory,
        BunitContext TestContext,
        IRenderedComponent<Settings> Component) : IDisposable
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
