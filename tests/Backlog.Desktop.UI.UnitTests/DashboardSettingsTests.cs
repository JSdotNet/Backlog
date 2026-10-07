using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI;
using Backlog.Modules.Dashboard.UI.Extensions;
using Backlog.Modules.Inbox.Abstractions.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The weekly usage reset, on the Dashboard's own settings section.
/// <para>
/// Asserted against the store rather than against what the row is showing,
/// because the store is what the dashboard reads. The section is the Dashboard's
/// component, so most of these render it on its own; the last ones render the
/// settings screen to prove the shell carries it only through the section the
/// Dashboard registers, after the Inbox's and behind the dashboard's switch.
/// </para>
/// </summary>
public sealed class DashboardSettingsTests
{
    /// <summary>Nothing set opens as "Not set" with the time disabled, and says what
    /// stands in: detection, then the calendar.</summary>
    [Fact]
    public void The_usage_reset_opens_unset_and_says_detection_stands_in()
    {
        using var section = RenderSection();

        Assert.True(section.Component.Find("[data-testid='usage-reset-time']").HasAttribute("disabled"));
        Assert.Contains("uses the reset it detects", section.Component.Find("[data-testid='usage-reset-status']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>Picking a day sets the reset at once, at the assistant's usual two in
    /// the afternoon, and the time can then be changed and is stored straight away.</summary>
    [Fact]
    public void Picking_a_day_sets_the_reset_and_the_time_is_stored_on_change()
    {
        using var section = RenderSection();

        section.Component.Find("[data-testid='usage-reset-day'] select").Change("Monday");

        Assert.Equal(new UsageWeekReset(DayOfWeek.Monday, new TimeOnly(14, 0)), section.UsageReset.Current);
        Assert.Equal("14:00", section.Component.Find("[data-testid='usage-reset-time']").GetAttribute("value"));

        section.Component.Find("[data-testid='usage-reset-time']").Change("21:00");

        Assert.Equal(new UsageWeekReset(DayOfWeek.Monday, new TimeOnly(21, 0)), section.UsageReset.Current);
        Assert.Contains("Weeks run from Monday 21:00", section.Component.Find("[data-testid='usage-reset-status']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_time_that_does_not_read_is_refused_and_the_reset_in_force_stays()
    {
        using var section = RenderSection();
        section.Component.Find("[data-testid='usage-reset-day'] select").Change("Tuesday");

        section.Component.Find("[data-testid='usage-reset-time']").Change("half past");

        Assert.Equal(new UsageWeekReset(DayOfWeek.Tuesday, new TimeOnly(14, 0)), section.UsageReset.Current);
        Assert.Equal("Give the reset a time, as HH:mm.", section.Component.Find("[data-testid='usage-reset-status']").TextContent.Trim());
    }

    [Fact]
    public void Clearing_the_reset_hands_the_week_back_to_detection()
    {
        using var section = RenderSection();

        section.Component.Find("[data-testid='usage-reset-day'] select").Change("Friday");
        section.Component.Find("[data-testid='usage-reset-clear-button']").Click();

        Assert.Null(section.UsageReset.Current);
        Assert.True(section.Component.Find("[data-testid='usage-reset-clear-button']").HasAttribute("disabled"));
        Assert.True(section.Component.Find("[data-testid='usage-reset-time']").HasAttribute("disabled"));
    }

    [Fact]
    public void The_section_says_where_the_reset_is_kept()
    {
        using var section = RenderSection();

        Assert.Contains(
            section.UsageReset.SettingsPath,
            section.Component.Find("[data-testid='usage-reset-settings']").TextContent,
            StringComparison.Ordinal);
    }

    // --- On the settings screen ---------------------------------------------------

    [Fact]
    public void With_the_dashboard_on_the_settings_screen_offers_its_page_after_the_inbox()
    {
        using var settings = RenderSettings(dashboard: true);

        var tabs = Tabs(settings.Component);
        Assert.Equal(["Inbox", "Dashboard", "Working week"], tabs[^3..]);

        OpenTab(settings.Component, "Dashboard");
        settings.Component.Find("[data-testid='usage-reset-day'] select").Change("Monday");

        Assert.Equal(new UsageWeekReset(DayOfWeek.Monday, new TimeOnly(14, 0)), settings.UsageReset.Current);
    }

    [Fact]
    public void With_the_dashboard_off_the_settings_screen_offers_no_dashboard_page_but_keeps_the_working_week()
    {
        using var settings = RenderSettings(dashboard: false);

        var tabs = Tabs(settings.Component);
        Assert.DoesNotContain("Dashboard", tabs);
        Assert.Equal(["Inbox", "Working week"], tabs[^2..]);
    }

    [Fact]
    public void The_storage_tab_no_longer_carries_the_usage_reset()
    {
        using var settings = RenderSettings(dashboard: true);

        OpenTab(settings.Component, "Storage");

        Assert.Empty(settings.Component.FindAll("[data-testid^='usage-reset']"));
    }

    /// <summary>Nothing set opens every provider empty and says none has a budget.</summary>
    [Fact]
    public void The_spend_budgets_open_empty_and_say_no_provider_has_one()
    {
        using var section = RenderSection();

        Assert.Equal(string.Empty, section.Component.Find("#spend-budget-claude").GetAttribute("value") ?? string.Empty);
        Assert.Equal(string.Empty, section.Component.Find("#spend-budget-copilot").GetAttribute("value") ?? string.Empty);
        Assert.Equal(string.Empty, section.Component.Find("#spend-budget-azure-foundry").GetAttribute("value") ?? string.Empty);
        Assert.Contains("No budgets set", section.Component.Find("[data-testid='spend-budget-status']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>An amount is stored for that provider alone, and survives a new store
    /// over the same file - which is what the next launch is.</summary>
    [Fact]
    public void An_amount_is_stored_for_that_provider_alone_and_read_back_next_time()
    {
        using var section = RenderSection();

        section.Component.Find("#spend-budget-copilot").Change("49.50");

        Assert.Equal(49.50m, section.SpendBudgets.BudgetFor(SpendProvider.Copilot));
        Assert.Null(section.SpendBudgets.BudgetFor(SpendProvider.Claude));
        Assert.Null(section.SpendBudgets.BudgetFor(SpendProvider.AzureFoundry));
        Assert.Contains("Budgets set for GitHub Copilot", section.Component.Find("[data-testid='spend-budget-status']").TextContent, StringComparison.Ordinal);

        Assert.Equal(49.50m, new SpendBudgetSettingsStore(section.SpendBudgets.SettingsPath).BudgetFor(SpendProvider.Copilot));
    }

    /// <summary>Emptying the field is how a budget is taken away.</summary>
    [Fact]
    public void Emptying_the_field_removes_the_budget()
    {
        using var section = RenderSection();
        section.Component.Find("#spend-budget-claude").Change("100");

        section.Component.Find("#spend-budget-claude").Change("");

        Assert.Null(section.SpendBudgets.BudgetFor(SpendProvider.Claude));
    }

    [Theory]
    [InlineData("lots", "Give the Claude budget as an amount, such as 50 or 49.50.")]
    [InlineData("-5", "A budget can't be negative.")]
    [InlineData("1e", "Give the Claude budget as an amount, such as 50 or 49.50.")]
    public void An_amount_that_is_not_a_budget_is_refused_and_the_one_in_force_stays(string typed, string reason)
    {
        using var section = RenderSection();
        section.Component.Find("#spend-budget-claude").Change("100");

        section.Component.Find("#spend-budget-claude").Change(typed);

        Assert.Equal(100m, section.SpendBudgets.BudgetFor(SpendProvider.Claude));
        Assert.Equal(reason, section.Component.Find("[data-testid='spend-budget-status']").TextContent.Trim());
    }

    /// <summary>The field is a text input, so text that is not an amount reaches the
    /// page as typed and is refused - a number input would hand over an empty value,
    /// which reads as "no budget" and would delete the one in force.</summary>
    [Fact]
    public void The_budget_fields_are_text_inputs_so_unreadable_text_is_not_read_as_empty()
    {
        using var section = RenderSection();

        Assert.Equal("text", section.Component.Find("#spend-budget-claude").GetAttribute("type"));
        Assert.Equal("decimal", section.Component.Find("#spend-budget-claude").GetAttribute("inputmode"));
    }

    /// <summary>A refusal stays on screen while its text does, even after another
    /// field is committed.</summary>
    [Fact]
    public void A_refusal_outlives_a_commit_in_another_field()
    {
        using var section = RenderSection();

        section.Component.Find("#spend-budget-claude").Change("-5");
        section.Component.Find("#spend-budget-copilot").Change("20");

        Assert.Equal(20m, section.SpendBudgets.BudgetFor(SpendProvider.Copilot));
        Assert.Equal("A budget can't be negative.", section.Component.Find("[data-testid='spend-budget-status']").TextContent.Trim());

        section.Component.Find("#spend-budget-claude").Change("");

        Assert.Contains("Budgets set for GitHub Copilot", section.Component.Find("[data-testid='spend-budget-status']").TextContent, StringComparison.Ordinal);
    }

    private static string[] Tabs(IRenderedComponent<Settings> component) =>
        [.. component.FindAll(".settings-tabs button").Select(button => button.TextContent.Trim())];

    private static void OpenTab(IRenderedComponent<Settings> component, string title) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == title).Click();

    private static SectionRenderContext RenderSection()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-dashboard-settings-tests", Guid.NewGuid().ToString("n"));

        var usageReset = new UsageResetSettingsStore(Path.Combine(root, "usage-reset", "usage-reset.json"));
        var spendBudgets = new SpendBudgetSettingsStore(Path.Combine(root, "spend-budgets", "spend-budgets.json"));

        var context = new BunitContext();
        context.Services.AddSingleton<IUsageResetSettings>(usageReset);
        context.Services.AddSingleton<ISpendBudgetSettings>(spendBudgets);

        return new SectionRenderContext(root, context, context.Render<DashboardSettings>(), usageReset, spendBudgets);
    }

    private static SettingsRenderContext RenderSettings(bool dashboard)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-dashboard-settings-tests", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);
        _ = features.SetEnabled(AppFeatures.InboxPane, true);
        _ = features.SetEnabled(DashboardFeatures.Dashboard, dashboard);

        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"), () => Path.Combine(root, "workspace"));
        var usageReset = new UsageResetSettingsStore(Path.Combine(root, "usage-reset", "usage-reset.json"));

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IWorkingHoursSettings>(
            new WorkingHoursSettingsStore(Path.Combine(root, "working-hours", "working-hours.json")));
        context.Services.AddSingleton<IUsageResetSettings>(usageReset);
        context.Services.AddSingleton<ISpendBudgetSettings>(
            new SpendBudgetSettingsStore(Path.Combine(root, "spend-budgets", "spend-budgets.json")));
        context.Services.AddSingleton(new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json")));
        context.Services.AddSingleton(new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json")));
        context.Services.AddSingleton(new GitHubIntegration(githubSettings, new NoGitHub(), new NoProbe()));
        context.Services.AddSingleton<FeedbackReporter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(githubSettings, store));
        context.Services.AddSingleton(new DevbookSourceSelection(githubSettings, new StubBranchCatalog()));
        context.Services.AddSingleton<IInboxRoutingRules>(
            new InboxRoutingRulesStore(Path.Combine(root, "inbox", "inbox-routing-rules.json")));
        context.Services.AddInboxSettings();
        context.Services.AddDashboardSettings();

        return new SettingsRenderContext(root, context, context.Render<Settings>(), usageReset);
    }

    private sealed class NoGitHub : IGitHubClient
    {
        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssue> CreateIssueAsync(GitHubRepositoryRef repository, string title, string? body, IEnumerable<string>? labels = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(GitHubRepositoryRef repository, string path, string branch, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
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

    private static void DeleteQuietly(string root)
    {
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed record SectionRenderContext(
        string Root,
        BunitContext TestContext,
        IRenderedComponent<DashboardSettings> Component,
        UsageResetSettingsStore UsageReset,
        SpendBudgetSettingsStore SpendBudgets) : IDisposable
    {
        public void Dispose()
        {
            TestContext.Dispose();
            DeleteQuietly(Root);
        }
    }

    private sealed record SettingsRenderContext(
        string Root,
        BunitContext TestContext,
        IRenderedComponent<Settings> Component,
        UsageResetSettingsStore UsageReset) : IDisposable
    {
        public void Dispose()
        {
            TestContext.Dispose();
            DeleteQuietly(Root);
        }
    }
}
