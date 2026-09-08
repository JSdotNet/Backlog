using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Storage tab's working-week editor.
/// <para>
/// Asserted against the store rather than against what the rows are showing,
/// because the store is what the dashboard reads: a row that took a time and
/// wrote nothing would leave the grid shaded exactly as it was. Committing on
/// change with no save button is the house rule the rest of this screen follows.
/// </para>
/// </summary>
public sealed class SettingsWorkingHoursTests
{
    [Fact]
    public void Every_day_of_the_week_gets_a_row_starting_at_Monday()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        var rows = settings.Component.FindAll("[data-testid='working-hours-row']");

        Assert.Equal(7, rows.Count);
        Assert.Contains("Monday", rows[0].TextContent);
        Assert.Contains("Sunday", rows[6].TextContent);
    }

    [Fact]
    public void The_rows_open_showing_what_is_in_force()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        Assert.Equal(
            "09:00",
            settings.Component.Find("[data-testid='working-hours-monday-start']").GetAttribute("value"));
        Assert.Equal(
            "17:30",
            settings.Component.Find("[data-testid='working-hours-monday-end']").GetAttribute("value"));
    }

    [Fact]
    public void A_committed_time_is_stored_straight_away()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        settings.Component.Find("[data-testid='working-hours-friday-end']").Change("13:00");

        Assert.Equal(new TimeOnly(13, 0), settings.WorkingWeek.Current.On(DayOfWeek.Friday).End);
        Assert.Equal(
            "13:00",
            settings.Component.Find("[data-testid='working-hours-friday-end']").GetAttribute("value"));
        Assert.DoesNotContain(
            "setting__status--error",
            settings.Component.Find("[data-testid='working-hours-status']").InnerHtml);
    }

    /// <summary>What the section says when every worked day runs the same hours,
    /// which is the shape the default week has and the one a reader who has
    /// changed nothing will see.</summary>
    [Fact]
    public void The_status_line_says_which_days_are_worked_and_between_what_hours()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        var status = settings.Component.Find("[data-testid='working-hours-status']").TextContent;

        Assert.Contains("Monday", status);
        Assert.Contains("Friday", status);
        Assert.DoesNotContain("Saturday", status);
        Assert.Contains("09:00 to 17:30", status);
    }

    /// <summary>The reason is reported beside the rows and nothing is written -
    /// the same shape as an interval the refresh store will not run at.</summary>
    [Fact]
    public void A_day_that_would_end_before_it_starts_says_so_and_changes_nothing()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        settings.Component.Find("[data-testid='working-hours-monday-end']").Change("08:00");

        Assert.Equal(new TimeOnly(17, 30), settings.WorkingWeek.Current.On(DayOfWeek.Monday).End);

        var status = settings.Component.Find("[data-testid='working-hours-status']");
        Assert.Contains("setting__status--error", status.InnerHtml);
    }

    [Fact]
    public void A_cleared_time_is_refused_rather_than_read_as_midnight()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        settings.Component.Find("[data-testid='working-hours-monday-start']").Change(string.Empty);

        Assert.Equal(new TimeOnly(9, 0), settings.WorkingWeek.Current.On(DayOfWeek.Monday).Start);
        Assert.Contains(
            "setting__status--error",
            settings.Component.Find("[data-testid='working-hours-status']").InnerHtml);
    }

    [Fact]
    public void Turning_a_day_off_is_stored_and_takes_its_hours_out_of_reach()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        settings.Component.Find("[data-testid='working-hours-monday-enabled']").Change(false);

        Assert.False(settings.WorkingWeek.Current.On(DayOfWeek.Monday).Working);
        Assert.True(settings.Component.Find("[data-testid='working-hours-monday-start']").HasAttribute("disabled"));
        Assert.True(settings.Component.Find("[data-testid='working-hours-monday-end']").HasAttribute("disabled"));
    }

    /// <summary>The hours come back with the day, which is the promise a day off
    /// makes: turning Saturday off is not the same as forgetting what it
    /// was.</summary>
    [Fact]
    public void A_day_turned_back_on_keeps_the_hours_it_had()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        settings.Component.Find("[data-testid='working-hours-tuesday-end']").Change("14:00");
        settings.Component.Find("[data-testid='working-hours-tuesday-enabled']").Change(false);
        settings.Component.Find("[data-testid='working-hours-tuesday-enabled']").Change(true);

        var tuesday = settings.WorkingWeek.Current.On(DayOfWeek.Tuesday);

        Assert.True(tuesday.Working);
        Assert.Equal(new TimeOnly(14, 0), tuesday.End);
    }

    [Fact]
    public void The_way_back_to_the_default_week_puts_every_day_back()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        settings.Component.Find("[data-testid='working-hours-saturday-enabled']").Change(true);
        settings.Component.Find("[data-testid='working-hours-monday-start']").Change("07:00");

        settings.Component.Find("[data-testid='working-hours-reset-button']").Click();

        Assert.False(settings.WorkingWeek.Current.On(DayOfWeek.Saturday).Working);
        Assert.Equal(new TimeOnly(9, 0), settings.WorkingWeek.Current.On(DayOfWeek.Monday).Start);
        Assert.Equal(
            "09:00",
            settings.Component.Find("[data-testid='working-hours-monday-start']").GetAttribute("value"));
    }

    /// <summary>Nothing to go back to is nothing to press. The button is the only
    /// control on the row, so an enabled one that would do nothing is the whole
    /// affordance lying.</summary>
    [Fact]
    public void There_is_nothing_to_reset_on_an_untouched_week()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        Assert.True(settings.Component.Find("[data-testid='working-hours-reset-button']").HasAttribute("disabled"));
    }

    private static void OpenStorageTab(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "Storage").Click();

    private static SettingsRenderContext RenderSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-working-hours-tests", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);

        var workingWeek = new WorkingHoursSettingsStore(Path.Combine(root, "working-hours", "working-hours.json"));
        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<ITasksRefreshSettings>(
            new TasksRefreshSettingsStore(Path.Combine(root, "refresh", "refresh.json")));
        context.Services.AddSingleton<IWorkingHoursSettings>(workingWeek);
        context.Services.AddSingleton(new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json")));
        context.Services.AddSingleton(new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json")));
        context.Services.AddSingleton(new GitHubIntegration(githubSettings, new NoGitHub(), new NoProbe()));
        context.Services.AddSingleton<FeedbackReporter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(githubSettings, store));
        context.Services.AddSingleton(new KnowledgeSourceSelection(githubSettings, new StubBranchCatalog()));

        return new SettingsRenderContext(root, context, context.Render<Settings>(), workingWeek);
    }

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

        public Task<GitHubUploadedFile> UploadFileAsync(
            GitHubRepositoryRef repository,
            string path,
            string branch,
            byte[] content,
            string commitMessage,
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
        WorkingHoursSettingsStore WorkingWeek) : IDisposable
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
