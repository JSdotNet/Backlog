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
/// The named weekly usage resets, on the Dashboard's own settings section.
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
    /// <summary>An empty list says so in a sentence, and what stands in meanwhile:
    /// detection, then the calendar.</summary>
    [Fact]
    public void The_usage_resets_open_empty_and_say_detection_stands_in()
    {
        using var section = RenderSection();

        Assert.Empty(section.Component.FindAll("[data-testid='usage-reset-row']"));
        Assert.Contains(
            "No reset yet. The dashboard uses the reset it detects from your assistant's records, or calendar weeks.",
            Squashed(section.Component.Find("[data-testid='usage-reset-empty']").TextContent),
            StringComparison.Ordinal);
        Assert.Empty(section.Component.FindAll("[data-testid='usage-reset-clear-button']"));
    }

    /// <summary>Adding a reset stores a whole one at once - the next free name, at the
    /// assistant's usual Monday afternoon - and takes the focus to its name.</summary>
    [Fact]
    public void Adding_a_reset_stores_the_next_free_account_and_focuses_its_name()
    {
        using var section = RenderSection();

        section.Component.Find("[data-testid='usage-reset-add-button']").Click();

        var added = Assert.Single(section.UsageReset.Resets);
        Assert.Equal("Account 1", added.Name);
        Assert.Equal(new UsageWeekReset(DayOfWeek.Monday, new TimeOnly(14, 0)), added.Reset);
        Assert.Equal("Account 1", section.Component.Find("[data-testid='usage-reset-name']").GetAttribute("value"));
        Assert.Equal("14:00", section.Component.Find("[data-testid='usage-reset-time']").GetAttribute("value"));
        Assert.Empty(section.Component.FindAll("[data-testid='usage-reset-empty']"));

        var focus = section.TestContext.JSInterop.VerifyInvoke("backlogFocus");
        Assert.Equal($"usage-reset-name-{added.Id}", focus.Arguments[0]);

        section.Component.Find("[data-testid='usage-reset-add-button']").Click();

        Assert.Equal(["Account 1", "Account 2"], section.UsageReset.Resets.Select(reset => reset.Name));
    }

    /// <summary>A row's name, day and time each commit on their own - the typed fields
    /// on change, which is blur or Enter - and keep the rest of the row.</summary>
    [Fact]
    public void A_rows_name_day_and_time_are_stored_on_change()
    {
        using var section = RenderSection();
        section.Component.Find("[data-testid='usage-reset-add-button']").Click();

        section.Component.Find("[data-testid='usage-reset-name']").Change("Work");
        section.Component.Find("[data-testid='usage-reset-day'] select").Change("Tuesday");
        section.Component.Find("[data-testid='usage-reset-time']").Change("21:00");

        var reset = Assert.Single(section.UsageReset.Resets);
        Assert.Equal("Work", reset.Name);
        Assert.Equal(new UsageWeekReset(DayOfWeek.Tuesday, new TimeOnly(21, 0)), reset.Reset);
        Assert.Contains("Weeks run from Tuesday 21:00", section.Component.Find("[data-testid='usage-reset-status']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_time_that_does_not_read_is_refused_and_the_reset_in_force_stays()
    {
        using var section = RenderSection();
        section.Component.Find("[data-testid='usage-reset-add-button']").Click();

        section.Component.Find("[data-testid='usage-reset-time']").Change("half past");

        Assert.Equal(new UsageWeekReset(DayOfWeek.Monday, new TimeOnly(14, 0)), Assert.Single(section.UsageReset.Resets).Reset);
        Assert.Equal("Give the reset a time, as HH:mm.", section.Component.Find("[data-testid='usage-reset-status']").TextContent.Trim());
    }

    /// <summary>A name another reset already has, in any case, is refused on the
    /// status line; the typed text stays in the field to be corrected, and the name in
    /// force stays in the store.</summary>
    [Theory]
    [InlineData("account 1", "There is already a reset called account 1.")]
    [InlineData("   ", "Give the reset a name.")]
    public void A_duplicate_or_blank_name_is_refused_and_the_name_in_force_stays(string typed, string reason)
    {
        using var section = RenderSection();
        section.Component.Find("[data-testid='usage-reset-add-button']").Click();
        section.Component.Find("[data-testid='usage-reset-add-button']").Click();

        section.Component.FindAll("[data-testid='usage-reset-name']")[1].Change(typed);

        Assert.Equal(["Account 1", "Account 2"], section.UsageReset.Resets.Select(reset => reset.Name));
        Assert.Equal(reason, section.Component.Find("[data-testid='usage-reset-status']").TextContent.Trim());

        var field = section.Component.FindAll("[data-testid='usage-reset-name']")[1];
        Assert.Equal(typed, field.GetAttribute("value"));
        Assert.Contains("setting__input--error", field.ClassList);
    }

    /// <summary>With two resets, the one the dashboard cuts by says so; with one there
    /// is nothing to tell apart. A pick made elsewhere - the Sessions tab, while this
    /// page sits hidden - moves the mark without the page being opened again.</summary>
    [Fact]
    public void The_reset_in_use_is_marked_once_there_are_two()
    {
        using var section = RenderSection();
        section.Component.Find("[data-testid='usage-reset-add-button']").Click();

        Assert.Empty(section.Component.FindAll("[data-testid='usage-reset-in-use']"));

        section.Component.Find("[data-testid='usage-reset-add-button']").Click();

        Assert.Single(section.Component.FindAll("[data-testid='usage-reset-row']")[0].QuerySelectorAll("[data-testid='usage-reset-in-use']"));

        // The pick the Sessions tab makes, straight on the store.
        _ = section.UsageReset.Select(section.UsageReset.Resets[1].Id);

        section.Component.WaitForAssertion(() =>
            Assert.Single(section.Component.FindAll("[data-testid='usage-reset-row']")[1].QuerySelectorAll("[data-testid='usage-reset-in-use']")));

        var rows = section.Component.FindAll("[data-testid='usage-reset-row']");
        Assert.Empty(rows[0].QuerySelectorAll("[data-testid='usage-reset-in-use']"));
        Assert.Equal("Cuts the dashboard's weeks", rows[1].QuerySelector("[data-testid='usage-reset-in-use']")!.TextContent.Trim());
        Assert.Contains("Account 2 cuts the dashboard's weeks", section.Component.Find("[data-testid='usage-reset-status']").TextContent, StringComparison.Ordinal);

        // Remove is the last thing in the row, and the badge is not a column of it but a
        // line of its own ahead of the controls, so the row with it lays out like the
        // row without.
        Assert.Equal("usage-reset-remove-button", rows[1].QuerySelectorAll("[data-testid]")[^1].GetAttribute("data-testid"));
        Assert.Equal("usage-reset-in-use", rows[1].QuerySelectorAll("[data-testid]")[0].GetAttribute("data-testid"));
        Assert.Contains("dashboard-usage-reset-row__badge", rows[1].QuerySelector("[data-testid='usage-reset-in-use']")!.ClassList);
    }

    /// <summary>Removing every reset hands the week back to detection: the file goes,
    /// and the list says it is empty again.</summary>
    [Fact]
    public void Removing_the_last_reset_hands_the_week_back_to_detection()
    {
        using var section = RenderSection();
        section.Component.Find("[data-testid='usage-reset-add-button']").Click();

        section.Component.Find("[data-testid='usage-reset-remove-button']").Click();

        Assert.Empty(section.UsageReset.Resets);
        Assert.Null(section.UsageReset.Current);
        Assert.False(File.Exists(section.UsageReset.SettingsPath));
        Assert.NotNull(section.Component.Find("[data-testid='usage-reset-empty']"));
    }

    /// <summary>Add a reset belongs to the whole card, so it sits in the card's action
    /// corner rather than under the last row.</summary>
    [Fact]
    public void Add_a_reset_is_the_cards_action()
    {
        using var section = RenderSection();

        Assert.NotNull(section.Component.Find("[data-testid='usage-reset-settings'] .settings-card__action [data-testid='usage-reset-add-button']"));
    }

    /// <summary>Each row's fields say whose they are, so tabbing through two rows does
    /// not hear the same names twice.</summary>
    [Fact]
    public void Each_rows_fields_are_named_for_their_row()
    {
        using var section = RenderSection();
        section.Component.Find("[data-testid='usage-reset-add-button']").Click();
        section.Component.Find("[data-testid='usage-reset-add-button']").Click();

        Assert.Equal(
            ["Reset name, row 1", "Reset name, row 2"],
            section.Component.FindAll("[data-testid='usage-reset-name']").Select(field => field.GetAttribute("aria-label")));
        Assert.Equal(
            ["Account 1 resets at", "Account 2 resets at"],
            section.Component.FindAll("[data-testid='usage-reset-time']").Select(field => field.GetAttribute("aria-label")));
        Assert.Equal(
            ["Account 1 resets on", "Account 2 resets on"],
            section.Component.FindAll("[data-testid='usage-reset-day'] select").Select(select => select.GetAttribute("aria-label")));
        Assert.Equal(
            ["Remove Account 1", "Remove Account 2"],
            section.Component.FindAll("[data-testid='usage-reset-remove-button']").Select(button => button.GetAttribute("aria-label")));
    }

    /// <summary>A file a single-reset version wrote opens as one row the person can
    /// rename.</summary>
    [Fact]
    public void A_reset_saved_before_names_opens_as_one_named_row()
    {
        using var section = RenderSection("""{ "day": "Monday", "time": "14:00" }""");

        Assert.Equal("Weekly reset", section.Component.Find("[data-testid='usage-reset-name']").GetAttribute("value"));
        Assert.Equal("14:00", section.Component.Find("[data-testid='usage-reset-time']").GetAttribute("value"));
        Assert.Contains("Weeks run from Monday 14:00", section.Component.Find("[data-testid='usage-reset-status']").TextContent, StringComparison.Ordinal);
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
    public void With_the_dashboard_on_the_settings_screen_offers_its_page_first_among_the_product_areas()
    {
        using var settings = RenderSettings(dashboard: true);

        var tabs = Tabs(settings.Component);
        Assert.Equal(["Dashboard", "Working week", "Inbox"], tabs[^3..]);

        OpenTab(settings.Component, "Dashboard");
        settings.Component.Find("[data-testid='usage-reset-add-button']").Click();

        Assert.Equal(new UsageWeekReset(DayOfWeek.Monday, new TimeOnly(14, 0)), settings.UsageReset.Current);
    }

    /// <summary>The page writes its own sentence over the frame rather than leaving
    /// its title there.</summary>
    [Fact]
    public void The_dashboard_page_supplies_its_headline()
    {
        using var settings = RenderSettings(dashboard: true);

        OpenTab(settings.Component, "Dashboard");

        Assert.Equal(
            "Choose what the dashboard counts and how it reads your week.",
            settings.Component.Find("[data-testid='settings-headline']").TextContent.Trim());
    }

    /// <summary>The cards on screen are the ones the registration declares, in the
    /// same order, so the settings search finds what the page draws.</summary>
    [Fact]
    public void The_cards_on_the_page_are_the_ones_its_section_declares()
    {
        var section = Registered("dashboard");
        using var page = RenderSection();

        Assert.Equal(["Weekly usage reset", "Monthly spend budget"], section.CardTitles!);
        Assert.Equal(
            section.CardTitles!,
            page.Component.FindAll(".settings-card__title").Select(title => title.TextContent.Trim()));
        Assert.NotNull(page.Component.Find("section.settings-card[data-testid='usage-reset-settings']"));
        Assert.NotNull(page.Component.Find("section.settings-card[data-testid='spend-budget-settings']"));
    }

    [Fact]
    public void With_the_dashboard_off_the_settings_screen_marks_the_dashboard_page_Off_and_keeps_the_working_week()
    {
        using var settings = RenderSettings(dashboard: false);

        var tabs = Tabs(settings.Component);
        Assert.Equal(["Dashboard", "Working week", "Inbox"], tabs[^3..]);
        Assert.Equal("Off", SettingsMenu.Badge(settings.Component, "dashboard"));
        Assert.NotEqual("Off", SettingsMenu.Badge(settings.Component, "working-week"));
    }

    [Fact]
    public void The_storage_tab_no_longer_carries_the_usage_reset()
    {
        using var settings = RenderSettings(dashboard: true);

        OpenTab(settings.Component, "Storage & backup");

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
        Assert.Contains("Budgets set for Copilot", section.Component.Find("[data-testid='spend-budget-status']").TextContent, StringComparison.Ordinal);

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
    [InlineData("lots", "Give the Anthropic budget as an amount, such as 50 or 49.50.")]
    [InlineData("-5", "A budget can't be negative.")]
    [InlineData("1e", "Give the Anthropic budget as an amount, such as 50 or 49.50.")]
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

        Assert.Contains("Budgets set for Copilot", section.Component.Find("[data-testid='spend-budget-status']").TextContent, StringComparison.Ordinal);
    }

    private static string[] Tabs(IRenderedComponent<Settings> component) =>
        SettingsMenu.Pages(component);

    private static void OpenTab(IRenderedComponent<Settings> component, string title) =>
        SettingsMenu.Open(component, title);

    private static SettingsSection Registered(string id) =>
        new ServiceCollection().AddDashboardSettings()
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<SettingsSection>()
            .Single(section => section.Id == id);

    /// <summary>Whitespace collapsed, because Razor keeps a sentence's line breaks.</summary>
    private static string Squashed(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static SectionRenderContext RenderSection(string? usageResetFile = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-dashboard-settings-tests", Guid.NewGuid().ToString("n"));
        var usageResetPath = Path.Combine(root, "usage-reset", "usage-reset.json");

        if (usageResetFile is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(usageResetPath)!);
            File.WriteAllText(usageResetPath, usageResetFile);
        }

        var usageReset = new UsageResetSettingsStore(usageResetPath);
        var spendBudgets = new SpendBudgetSettingsStore(Path.Combine(root, "spend-budgets", "spend-budgets.json"));

        var context = new BunitContext();
        context.JSInterop.SetupVoid("backlogFocus", _ => true);
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
        context.JSInterop.SetupVoid("backlogFocus", _ => true);
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
