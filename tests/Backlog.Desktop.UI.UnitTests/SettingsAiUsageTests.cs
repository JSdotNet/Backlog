using Bunit;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Microsoft.Extensions.Time.Testing;
using Backlog.Modules.Tasks.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class SettingsAiUsageTests
{
    [Fact]
    public void Ai_tab_and_usage_endpoints_are_visible_when_only_usage_metrics_enabled()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        Assert.Contains("AI providers", SettingsTabs(context.Component));
        Assert.Empty(context.Component.FindAll("[data-testid='claude-usage-settings']"));

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
        {
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']"));
            Assert.Empty(context.Component.FindAll("[data-testid='azure-foundry-settings']"));
        });

        var claudeInput = context.Component.Find("[data-testid='claude-usage-endpoint-input']");
        claudeInput.Input(" https://claude.example.internal/v1/ ");
        claudeInput.Change();

        var githubInput = context.Component.Find("[data-testid='github-usage-endpoint-input']");
        githubInput.Input(" https://ghe.example.internal/api/v3/ ");
        githubInput.Change();

        Assert.Equal("https://claude.example.internal/v1", context.ClaudeStore.Current.Accounts[0].ApiEndpoint);
        Assert.Equal("https://ghe.example.internal/api/v3", context.GitHub.Settings.Current.ApiEndpoint);
        Assert.Contains("Claude usage API settings updated.", context.Component.Find("[data-testid='claude-usage-status']").TextContent);
        Assert.Contains("GitHub usage API settings updated.", context.Component.Find("[data-testid='github-usage-status']").TextContent);
    }

    /// <summary>
    /// Two vendors, two cards. They shared one before, which meant a change to either
    /// endpoint saved both and answered through a status line that could not say which
    /// vendor it meant.
    /// </summary>
    [Fact]
    public void Claude_and_github_have_their_own_cards_and_report_separately()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
        {
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']"));
            Assert.Single(context.Component.FindAll("[data-testid='github-usage-settings']"));
        });

        // The credential fields belong to Claude's card alone.
        var claudeCard = context.Component.Find("[data-testid='claude-usage-settings']");
        Assert.NotNull(claudeCard.QuerySelector("[data-testid='claude-api-key-input']"));

        var gitHubCard = context.Component.Find("[data-testid='github-usage-settings']");
        Assert.Null(gitHubCard.QuerySelector("[data-testid='claude-api-key-input']"));
        Assert.Null(gitHubCard.QuerySelector("[data-testid='claude-usage-endpoint-input']"));

        var claudeInput = context.Component.Find("[data-testid='claude-usage-endpoint-input']");
        claudeInput.Input("https://claude.example.internal");
        claudeInput.Change();

        // Claude's card answered; GitHub's is still saying what it says at rest.
        Assert.Contains("Claude usage API settings updated.", context.Component.Find("[data-testid='claude-usage-status']").TextContent);
        Assert.DoesNotContain("updated", context.Component.Find("[data-testid='github-usage-status']").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The one fact about an Anthropic admin key that cannot be discovered from it:
    /// which actor in the organization is you. Without it the dashboard would have to
    /// show the whole organization's spend under the heading "your usage", so it shows
    /// nothing and says why.
    /// </summary>
    [Fact]
    public void The_claude_account_is_saved_and_the_status_line_says_whose_usage_will_be_reported()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']")));

        var status = context.Component.Find("[data-testid='claude-usage-status']");
        Assert.Contains("needs a key", status.TextContent, StringComparison.Ordinal);
        Assert.Contains("needs your Claude account", status.TextContent, StringComparison.Ordinal);

        var actorInput = context.Component.Find("[data-testid='claude-usage-actor-input']");
        actorInput.Input("  person@example.com  ");
        actorInput.Change();

        Assert.Equal("person@example.com", context.ClaudeStore.Current.Accounts[0].Actor);
    }

    [Fact]
    public void Clearing_the_claude_account_forgets_it_rather_than_storing_blank_space()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']")));

        var actorInput = context.Component.Find("[data-testid='claude-usage-actor-input']");
        actorInput.Input("person@example.com");
        actorInput.Change();

        actorInput = context.Component.Find("[data-testid='claude-usage-actor-input']");
        actorInput.Input("   ");
        actorInput.Change();

        Assert.Null(context.ClaudeStore.Current.Accounts[0].Actor);
    }

    /// <summary>
    /// The key is written but never read back into the field. A stored secret that
    /// re-renders into an input is a secret one screenshot away from being shared, so
    /// the placeholder carries the "already set" signal instead.
    /// </summary>
    [Fact]
    public void The_claude_api_key_is_stored_and_never_rendered_back_into_the_field()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']")));

        var keyInput = context.Component.Find("[data-testid='claude-api-key-input']");
        Assert.Equal("password", keyInput.GetAttribute("type"));

        keyInput.Input("  sk-ant-admin01-example  ");
        keyInput.Change();

        Assert.Equal("sk-ant-admin01-example", context.ClaudeStore.Current.Accounts[0].AdminApiKey);

        keyInput = context.Component.Find("[data-testid='claude-api-key-input']");
        Assert.Equal(string.Empty, keyInput.GetAttribute("value"));
        Assert.Equal("stored - hidden", keyInput.GetAttribute("placeholder"));
    }

    [Fact]
    public void Forgetting_the_claude_api_key_clears_it_and_the_button_waits_until_one_is_stored()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']")));

        Assert.True(context.Component.Find("[data-testid='claude-clear-key-button']").HasAttribute("disabled"));

        var keyInput = context.Component.Find("[data-testid='claude-api-key-input']");
        keyInput.Input("sk-ant-admin01-example");
        keyInput.Change();

        context.Component.Find("[data-testid='claude-clear-key-button']").Click();

        Assert.Null(context.ClaudeStore.Current.Accounts[0].AdminApiKey);
        Assert.True(context.Component.Find("[data-testid='claude-clear-key-button']").HasAttribute("disabled"));
    }

    /// <summary>
    /// Anthropic also accepts a personal Console key that is not scoped to a workspace,
    /// so a key without the admin prefix is stored and sent. It is worth saying that it
    /// does not look like an admin key, because a workspace-scoped key is the common
    /// mistake and fails with an opaque 401 — but saying it is not the same as refusing.
    /// </summary>
    [Fact]
    public void A_key_without_the_admin_prefix_is_stored_and_the_card_says_what_may_be_wrong()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']")));

        var keyInput = context.Component.Find("[data-testid='claude-api-key-input']");
        keyInput.Input("sk-ant-api03-personal-key");
        keyInput.Change();

        Assert.Equal("sk-ant-api03-personal-key", context.ClaudeStore.Current.Accounts[0].AdminApiKey);

        var keyId = context.Component.Find("[data-testid='claude-api-key-input']").GetAttribute("id");
        var hint = context.Component.Find($"#{keyId}-help").TextContent;
        Assert.Contains("workspace", hint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("still be sent", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_workspace_filter_is_saved_and_blank_forgets_it()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']")));

        var workspaceInput = context.Component.Find("[data-testid='claude-workspace-input']");
        workspaceInput.Input("  wrkspc_01  ");
        workspaceInput.Change();

        Assert.Equal("wrkspc_01", context.ClaudeStore.Current.Accounts[0].WorkspaceId);

        workspaceInput = context.Component.Find("[data-testid='claude-workspace-input']");
        workspaceInput.Input("   ");
        workspaceInput.Change();

        Assert.Null(context.ClaudeStore.Current.Accounts[0].WorkspaceId);
    }

    /// <summary>
    /// One person, two Claude organizations. The second card gets its own endpoint,
    /// actor and key; the first keeps what it had; and forgetting the second brings
    /// the page back to one card without touching the first's key.
    /// </summary>
    [Fact]
    public void A_second_claude_account_has_its_own_fields_and_forgetting_it_leaves_the_first_alone()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']")));

        var firstKey = context.Component.Find("[data-testid='claude-api-key-input']");
        firstKey.Input("sk-ant-admin01-personal");
        firstKey.Change();

        context.Component.Find("[data-testid='add-claude-account-button']").Click();

        Assert.Equal(2, context.ClaudeStore.Current.Accounts.Count);
        Assert.Equal(2, context.Component.FindAll("[data-testid='claude-account-subpage-tab']").Count);
        Assert.Contains("2 Claude organizations", context.Component.Find("[data-testid='claude-accounts-status']").TextContent, StringComparison.Ordinal);

        // The new account's card is the one open, so its fields are the ones on screen.
        var second = context.ClaudeStore.Current.Accounts[1];
        var endpoint = context.Component.Find("[data-testid='claude-usage-endpoint-input']");
        Assert.Equal($"claude-usage-endpoint-{second.Id}", endpoint.GetAttribute("id"));
        endpoint.Input("https://claude.employer.example/");
        endpoint.Change();

        var actor = context.Component.Find("[data-testid='claude-usage-actor-input']");
        actor.Input("me@employer.example");
        actor.Change();

        var key = context.Component.Find("[data-testid='claude-api-key-input']");
        key.Input("sk-ant-admin01-work");
        key.Change();

        var name = context.Component.Find("[data-testid='claude-account-name-input']");
        name.Input("work");
        name.Change();

        var accounts = context.ClaudeStore.Current.Accounts;
        Assert.Equal("sk-ant-admin01-personal", accounts[0].AdminApiKey);
        Assert.Equal(ClaudeSettingsStore.DefaultApiEndpoint, accounts[0].ApiEndpoint);
        Assert.Equal("sk-ant-admin01-work", accounts[1].AdminApiKey);
        Assert.Equal("https://claude.employer.example", accounts[1].ApiEndpoint);
        Assert.Equal("me@employer.example", accounts[1].Actor);
        Assert.Equal("work", accounts[1].DisplayName);

        var tabs = context.Component.FindAll("[data-testid='claude-account-subpage-tab']").Select(tab => tab.TextContent.Trim()).ToArray();
        Assert.Equal(["Claude account", "work"], tabs);

        context.Component.Find("[data-testid='remove-claude-account-button']").Click();

        var remaining = Assert.Single(context.ClaudeStore.Current.Accounts);
        Assert.Equal("sk-ant-admin01-personal", remaining.AdminApiKey);
        Assert.Single(context.Component.FindAll("[data-testid='claude-account-subpage-tab']"));
    }

    /// <summary>
    /// The page always has one card, so the only account cannot go — forgetting it
    /// blanks it. A blank card has nothing left to forget, and offering the button
    /// anyway made a click look like it did nothing.
    /// </summary>
    [Fact]
    public void The_only_claude_account_can_be_forgotten_while_it_holds_something_and_not_once_blank()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']")));

        Assert.True(context.Component.Find("[data-testid='remove-claude-account-button']").HasAttribute("disabled"));

        var actor = context.Component.Find("[data-testid='claude-usage-actor-input']");
        actor.Input("me@example.com");
        actor.Change();

        var button = context.Component.Find("[data-testid='remove-claude-account-button']");
        Assert.False(button.HasAttribute("disabled"));
        button.Click();

        // A fresh blank account, and the strip agrees with it: one tab, selected, wired
        // to the card on screen - not the tab of the account that just went.
        var remaining = Assert.Single(context.ClaudeStore.Current.Accounts);
        Assert.Null(remaining.Actor);
        var tab = Assert.Single(context.Component.FindAll("[data-testid='claude-account-subpage-tab']"));
        Assert.Equal($"tab-{remaining.Id}", tab.GetAttribute("id"));
        Assert.Equal("true", tab.GetAttribute("aria-selected"));
        Assert.Equal(string.Empty, context.Component.Find("[data-testid='claude-usage-actor-input']").GetAttribute("value"));
        Assert.True(context.Component.Find("[data-testid='remove-claude-account-button']").HasAttribute("disabled"));
    }

    /// <summary>
    /// Forgetting the first of two, from its own page. Without a key per card the
    /// surviving component is handed the second account, and the strip has to follow.
    /// </summary>
    [Fact]
    public void Forgetting_the_first_claude_account_keeps_the_second_on_screen()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']")));

        context.Component.Find("[data-testid='add-claude-account-button']").Click();
        var name = context.Component.Find("[data-testid='claude-account-name-input']");
        name.Input("work");
        name.Change();

        context.Component.FindAll("[data-testid='claude-account-subpage-tab']")[0].Click();
        context.Component.Find("[data-testid='remove-claude-account-button']").Click();

        var remaining = Assert.Single(context.ClaudeStore.Current.Accounts);
        Assert.Equal("work", remaining.DisplayName);
        var tab = Assert.Single(context.Component.FindAll("[data-testid='claude-account-subpage-tab']"));
        Assert.Equal("work", tab.TextContent.Trim());
        Assert.Equal("true", tab.GetAttribute("aria-selected"));
        Assert.Equal("work", context.Component.Find("[data-testid='claude-account-name-input']").GetAttribute("value"));
    }

    /// <summary>
    /// "Test this account" asks Anthropic three things and puts each answer on its
    /// own line under the card, so a card that fails at step two still says step one
    /// passed. A pass ticks the checklist's last step; nothing is written.
    /// </summary>
    [Fact]
    public void Testing_a_claude_account_lists_what_anthropic_said_step_by_step()
    {
        var probe = new RecordingClaudeProbe(new ClaudeAccountCheck(
        [
            new ClaudeAccountCheckStep("Admin API key", ClaudeCheckOutcome.Passed, "Opens the organization Acme Corp."),
            new ClaudeAccountCheckStep("Your Claude account", ClaudeCheckOutcome.Passed, "me@example.com is a member of Acme Corp (developer)."),
            new ClaudeAccountCheckStep("Workspace", ClaudeCheckOutcome.Skipped, "No workspace set - the whole organization is reported.")
        ]));
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true, claudeProbe: probe);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']")));

        var key = context.Component.Find("[data-testid='claude-api-key-input']");
        key.Input("sk-ant-admin01-example");
        key.Change();
        var actor = context.Component.Find("[data-testid='claude-usage-actor-input']");
        actor.Input("me@example.com");
        actor.Change();

        var before = context.ClaudeStore.Current;
        Assert.Equal("false", LastStep(context.Component).GetAttribute("data-done"));

        context.Component.Find("[data-testid='test-claude-account-button']").Click();

        context.Component.WaitForAssertion(() =>
        {
            var lines = context.Component.FindAll("[data-testid='claude-account-check'] [data-testid='claude-account-check-step']");
            Assert.Equal(3, lines.Count);
            Assert.Contains("Opens the organization Acme Corp.", lines[0].TextContent, StringComparison.Ordinal);
            Assert.Equal("passed", lines[0].GetAttribute("data-outcome"));
            Assert.Equal("skipped", lines[2].GetAttribute("data-outcome"));
            Assert.Equal("true", LastStep(context.Component).GetAttribute("data-done"));
        });

        var asked = Assert.Single(probe.Asked);
        Assert.Equal("sk-ant-admin01-example", asked.AdminApiKey);
        Assert.Equal("me@example.com", asked.Actor);
        Assert.Same(before, context.ClaudeStore.Current);
    }

    [Fact]
    public void A_failed_claude_test_marks_the_failing_line_and_leaves_the_last_step_unticked()
    {
        var probe = new RecordingClaudeProbe(new ClaudeAccountCheck(
        [
            new ClaudeAccountCheckStep("Admin API key", ClaudeCheckOutcome.Failed, "Anthropic rejected the admin key - check it has not been revoked."),
            new ClaudeAccountCheckStep("Your Claude account", ClaudeCheckOutcome.Skipped, "Not asked - the key was refused."),
            new ClaudeAccountCheckStep("Workspace", ClaudeCheckOutcome.Skipped, "Not asked - the key was refused.")
        ]));
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true, claudeProbe: probe);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']")));

        var key = context.Component.Find("[data-testid='claude-api-key-input']");
        key.Input("sk-ant-admin01-example");
        key.Change();

        context.Component.Find("[data-testid='test-claude-account-button']").Click();

        context.Component.WaitForAssertion(() =>
        {
            var lines = context.Component.FindAll("[data-testid='claude-account-check'] [data-testid='claude-account-check-step']");
            Assert.Equal("failed", lines[0].GetAttribute("data-outcome"));
            Assert.Contains("rejected the admin key", lines[0].TextContent, StringComparison.Ordinal);
            Assert.Equal("false", LastStep(context.Component).GetAttribute("data-done"));
        });
    }

    /// <summary>Without a key there is nothing to test, and the checklist's first
    /// step already says so - the button waits for it.</summary>
    [Fact]
    public void The_claude_checklist_ticks_as_the_card_is_filled_and_the_test_waits_for_a_key()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true, claudeProbe: new RecordingClaudeProbe(new ClaudeAccountCheck([])));

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']")));

        var steps = context.Component.FindAll("[data-testid='claude-account-setup-steps'] [data-testid='setup-step']");
        Assert.Equal(4, steps.Count);
        Assert.All(steps, step => Assert.Equal("false", step.GetAttribute("data-done")));
        Assert.True(context.Component.Find("[data-testid='test-claude-account-button']").HasAttribute("disabled"));
        Assert.Contains("console.anthropic.com", steps[0].QuerySelector("a")!.GetAttribute("href"), StringComparison.Ordinal);

        var key = context.Component.Find("[data-testid='claude-api-key-input']");
        key.Input("sk-ant-admin01-example");
        key.Change();

        context.Component.WaitForAssertion(() =>
        {
            var after = context.Component.FindAll("[data-testid='claude-account-setup-steps'] [data-testid='setup-step']");
            Assert.Equal("true", after[0].GetAttribute("data-done"));
            Assert.Equal("false", after[1].GetAttribute("data-done"));
            Assert.False(context.Component.Find("[data-testid='test-claude-account-button']").HasAttribute("disabled"));
        });
    }

    private static AngleSharp.Dom.IElement LastStep(IRenderedComponent<Settings> component) =>
        component.FindAll("[data-testid='claude-account-setup-steps'] [data-testid='setup-step']")[^1];

    private sealed class RecordingClaudeProbe(ClaudeAccountCheck answer) : IClaudeAccountProbe
    {
        public List<ClaudeAccount> Asked { get; } = [];

        public Task<ClaudeAccountCheck> CheckAsync(ClaudeAccount account, CancellationToken cancellationToken = default)
        {
            Asked.Add(account);
            return Task.FromResult(answer);
        }
    }

    /// <summary>
    /// The GitHub usage card's "Test the connection" answers the two questions the
    /// card raises: whom this machine is signed in as, and whether each configured
    /// account's credential is really that account's - one line each, so a card with
    /// two accounts says which one is wrong.
    /// </summary>
    [Fact]
    public void Testing_the_github_usage_connection_reports_the_machine_and_every_account()
    {
        var probe = new RecordingGitHubAccountProbe(login => new GitHubAccountCheck(
            login == "JSdotNet",
            login == "JSdotNet"
                ? "GitHub recognises the token as JSdotNet at https://api.github.com."
                : "GitHub rejected the token — check it hasn't expired."));
        using var context = RenderSettings(
            aiAssistantEnabled: false,
            usageMetricsEnabled: true,
            seed: store => Assert.Null(store.SetAccounts(
            [
                new GitHubAccount("JSdotNet") { Credential = GitHubCredentialKind.PersonalAccessToken, Token = "ghp_a" },
                new GitHubAccount("octocat") { Credential = GitHubCredentialKind.PersonalAccessToken, Token = "ghp_b" }
            ])),
            connection: new GitHubConnection(true, "Connected through the GitHub CLI as JSdotNet.", "JSdotNet"),
            gitHubAccountProbe: probe);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='github-usage-settings']")));

        Assert.Equal("false", LastStep(context.Component, "github-usage-setup-steps").GetAttribute("data-done"));
        context.Component.Find("[data-testid='test-github-usage-button']").Click();

        context.Component.WaitForAssertion(() =>
        {
            var lines = context.Component.FindAll("[data-testid='github-usage-check'] [data-testid='github-usage-check-line']");
            Assert.Equal(3, lines.Count);
            Assert.Contains("Connected through the GitHub CLI as JSdotNet.", lines[0].TextContent, StringComparison.Ordinal);
            Assert.Equal("passed", lines[0].GetAttribute("data-outcome"));
            Assert.Contains("JSdotNet", lines[1].TextContent, StringComparison.Ordinal);
            Assert.Equal("passed", lines[1].GetAttribute("data-outcome"));
            Assert.Contains("octocat", lines[2].TextContent, StringComparison.Ordinal);
            Assert.Equal("failed", lines[2].GetAttribute("data-outcome"));
            // One account failed, so the card's test step stays unticked.
            Assert.Equal("false", LastStep(context.Component, "github-usage-setup-steps").GetAttribute("data-done"));
        });
        Assert.Equal(["JSdotNet", "octocat"], probe.Asked);
    }

    [Fact]
    public void The_github_usage_checklist_ticks_when_this_machine_can_reach_github_and_the_test_passes()
    {
        using var context = RenderSettings(
            aiAssistantEnabled: false,
            usageMetricsEnabled: true,
            connection: new GitHubConnection(true, "Connected through the GitHub CLI as JSdotNet.", "JSdotNet"),
            gitHubAccountProbe: new RecordingGitHubAccountProbe(_ => new GitHubAccountCheck(true, "fine")));

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='github-usage-settings']")));

        context.Component.WaitForAssertion(() =>
        {
            var steps = context.Component.FindAll("[data-testid='github-usage-setup-steps'] [data-testid='setup-step']");
            Assert.Equal(3, steps.Count);
            Assert.Equal("true", steps[0].GetAttribute("data-done"));
            Assert.Equal("false", steps[2].GetAttribute("data-done"));
        });

        context.Component.Find("[data-testid='test-github-usage-button']").Click();

        context.Component.WaitForAssertion(() =>
            Assert.Equal("true", LastStep(context.Component, "github-usage-setup-steps").GetAttribute("data-done")));
    }

    /// <summary>
    /// The Foundry card's "Test the connection": the probe's one sentence lands on
    /// the card and the checklist's last step ticks on a pass. The button waits for
    /// the three fields the completion needs, because the probe would only say so.
    /// </summary>
    [Fact]
    public void Testing_the_foundry_connection_puts_the_probes_answer_on_the_card()
    {
        var probe = new RecordingFoundryProbe(new AzureFoundryCheck(true, "The deployment chat at foundry.example.com answered."));
        using var context = RenderSettings(aiAssistantEnabled: true, usageMetricsEnabled: false, foundryProbe: probe);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='azure-foundry-settings']")));

        var steps = context.Component.FindAll("[data-testid='azure-foundry-setup-steps'] [data-testid='setup-step']");
        Assert.Equal(4, steps.Count);
        Assert.All(steps, step => Assert.Equal("false", step.GetAttribute("data-done")));
        Assert.True(context.Component.Find("[data-testid='test-azure-foundry-button']").HasAttribute("disabled"));

        Fill(context.Component, "azure-foundry-endpoint-input", "https://foundry.example.com");
        Fill(context.Component, "azure-foundry-deployment-input", "chat");
        Fill(context.Component, "azure-foundry-api-key-input", "secret");

        context.Component.WaitForAssertion(() =>
        {
            var filled = context.Component.FindAll("[data-testid='azure-foundry-setup-steps'] [data-testid='setup-step']");
            Assert.Equal(["true", "true", "true", "false"], filled.Select(step => step.GetAttribute("data-done")).ToArray());
            Assert.False(context.Component.Find("[data-testid='test-azure-foundry-button']").HasAttribute("disabled"));
        });

        context.Component.Find("[data-testid='test-azure-foundry-button']").Click();

        context.Component.WaitForAssertion(() =>
        {
            Assert.Contains("The deployment chat at foundry.example.com answered.", context.Component.Find("[data-testid='azure-foundry-check-status']").TextContent, StringComparison.Ordinal);
            Assert.Equal("true", LastStep(context.Component, "azure-foundry-setup-steps").GetAttribute("data-done"));
        });
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public void A_failed_foundry_test_is_shown_as_the_problem_it_is()
    {
        var probe = new RecordingFoundryProbe(new AzureFoundryCheck(false, "Azure Foundry returned 401: Access denied due to invalid subscription key."));
        using var context = RenderSettings(aiAssistantEnabled: true, usageMetricsEnabled: false, foundryProbe: probe);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='azure-foundry-settings']")));

        Fill(context.Component, "azure-foundry-endpoint-input", "https://foundry.example.com");
        Fill(context.Component, "azure-foundry-deployment-input", "chat");
        Fill(context.Component, "azure-foundry-api-key-input", "wrong");

        context.Component.WaitForAssertion(() =>
            Assert.False(context.Component.Find("[data-testid='test-azure-foundry-button']").HasAttribute("disabled")));
        context.Component.Find("[data-testid='test-azure-foundry-button']").Click();

        context.Component.WaitForAssertion(() =>
        {
            var status = context.Component.Find("[data-testid='azure-foundry-check-status']");
            Assert.NotNull(status.QuerySelector(".setting__status--error"));
            Assert.Contains("returned 401", status.TextContent, StringComparison.Ordinal);
            Assert.Equal("false", LastStep(context.Component, "azure-foundry-setup-steps").GetAttribute("data-done"));
        });
    }

    private static void Fill(IRenderedComponent<Settings> component, string testId, string value)
    {
        var input = component.Find($"[data-testid='{testId}']");
        input.Input(value);
        input.Change();
    }

    private static AngleSharp.Dom.IElement LastStep(IRenderedComponent<Settings> component, string listTestId) =>
        component.FindAll($"[data-testid='{listTestId}'] [data-testid='setup-step']")[^1];

    private sealed class RecordingGitHubAccountProbe(Func<string, GitHubAccountCheck> answer) : IGitHubAccountProbe
    {
        public List<string> Asked { get; } = [];

        public Task<GitHubAccountCheck> CheckAccountAsync(GitHubAccount account, CancellationToken cancellationToken = default)
        {
            Asked.Add(account.Login);
            return Task.FromResult(answer(account.Login));
        }
    }

    private sealed class RecordingFoundryProbe(AzureFoundryCheck answer) : IAzureFoundryConnectionProbe
    {
        public int Calls { get; private set; }

        public Task<AzureFoundryCheck> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(answer);
        }
    }

    /// <summary>Switching subpages switches which account the fields write to.</summary>
    [Fact]
    public void Picking_a_claude_account_subpage_edits_that_account()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']")));

        context.Component.Find("[data-testid='add-claude-account-button']").Click();
        var first = context.ClaudeStore.Current.Accounts[0];

        context.Component.FindAll("[data-testid='claude-account-subpage-tab']")[0].Click();

        var actor = context.Component.Find("[data-testid='claude-usage-actor-input']");
        Assert.Equal($"claude-usage-actor-{first.Id}", actor.GetAttribute("id"));
        actor.Input("me@example.com");
        actor.Change();

        Assert.Equal("me@example.com", context.ClaudeStore.Current.Accounts[0].Actor);
        Assert.Null(context.ClaudeStore.Current.Accounts[1].Actor);
    }

    /// <summary>
    /// GitHub's card names every configured account and where its usage is read,
    /// so the whole picture is on the one card - even though the endpoint itself is
    /// edited on the account's own card, where the fact belongs.
    /// </summary>
    [Fact]
    public void The_github_usage_card_lists_each_accounts_endpoint()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true, seed: store =>
            Assert.Null(store.SetAccounts(
            [
                new GitHubAccount("JSdotNet"),
                new GitHubAccount("octocat") { ApiEndpoint = "https://ghe.example.internal/api/v3" }
            ])));

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='github-usage-settings']")));

        var lines = context.Component.FindAll("[data-testid='github-usage-account-endpoint']").Select(line => line.TextContent).ToArray();

        Assert.Equal(2, lines.Length);
        Assert.Contains("JSdotNet", lines[0], StringComparison.Ordinal);
        Assert.Contains("https://api.github.com", lines[0], StringComparison.Ordinal);
        Assert.Contains("octocat", lines[1], StringComparison.Ordinal);
        Assert.Contains("https://ghe.example.internal/api/v3", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Ai_tab_shows_azure_foundry_only_when_only_ai_assistant_enabled()
    {
        using var context = RenderSettings(aiAssistantEnabled: true, usageMetricsEnabled: false);

        Assert.Contains("AI providers", SettingsTabs(context.Component));

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
        {
            Assert.Single(context.Component.FindAll("[data-testid='azure-foundry-settings']"));
            Assert.Empty(context.Component.FindAll("[data-testid='claude-usage-settings']"));
        });
    }

    [Fact]
    public void Ai_tab_shows_both_sections_when_usage_metrics_and_ai_assistant_are_enabled()
    {
        using var context = RenderSettings(aiAssistantEnabled: true, usageMetricsEnabled: true);

        Assert.Contains("AI providers", SettingsTabs(context.Component));

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
        {
            Assert.Single(context.Component.FindAll("[data-testid='azure-foundry-settings']"));
            Assert.Single(context.Component.FindAll("[data-testid='claude-usage-settings']"));
        });
    }

    /// <summary>
    /// The cost scope is the one Foundry setting the dashboard reads rather than
    /// the assistant, so it is its own field with its own status line — and a
    /// value typed there lands in the same store the chat connection uses.
    /// </summary>
    [Fact]
    public void Azure_foundry_section_stores_the_cost_scope_the_dashboard_reads_spend_for()
    {
        using var context = RenderSettings(aiAssistantEnabled: true, usageMetricsEnabled: false);
        OpenAiTab(context.Component);

        const string scope = "/subscriptions/abc/resourceGroups/rg/providers/Microsoft.CognitiveServices/accounts/ai";
        context.Component.WaitForAssertion(() =>
            Assert.Contains("needs a resource ID", context.Component.Find("[data-testid='azure-foundry-cost-status']").TextContent));

        var input = context.Component.Find("[data-testid='azure-foundry-cost-scope-input']");
        input.Input(scope);
        input.Change();

        context.Component.WaitForAssertion(() =>
        {
            Assert.Equal(scope, context.AzureFoundryStore.Current.CostScope);
            Assert.Contains(scope, context.Component.Find("[data-testid='azure-foundry-cost-status']").TextContent);
            Assert.Contains("cost scope updated", context.Component.Find("[data-testid='azure-foundry-status']").TextContent);
        });
    }

    /// <summary>
    /// The page opens on a sentence about every provider it offers, and one tile
    /// per provider says the same thing as a figure. A provider that is not set up
    /// is a dashed place for one, and Azure Foundry's says what needs it.
    /// </summary>
    [Fact]
    public void The_headline_and_tiles_follow_each_providers_state()
    {
        using var context = RenderSettings(aiAssistantEnabled: true, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Equal(
                "No AI provider is set up yet.",
                context.Component.Find("[data-testid='settings-headline']").TextContent.Trim()));

        var tiles = context.Component.FindAll("[data-testid='ai-provider-tile']");
        Assert.Equal(["claude", "github", "azurefoundry"], tiles.Select(tile => tile.GetAttribute("data-provider")).ToArray());
        Assert.All(tiles, tile =>
        {
            Assert.Contains("ai-provider-tile--unset", tile.ClassList);
            Assert.Contains("Not set up", tile.TextContent, StringComparison.Ordinal);
        });
        Assert.Contains("The AI assistant needs it", tiles[2].TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("The AI assistant needs it", tiles[0].TextContent, StringComparison.Ordinal);

        context.Component.Find("[data-testid='add-claude-account-button']").Click();
        foreach (var (index, key) in new[] { (0, "sk-ant-admin01-personal"), (1, "sk-ant-admin01-work") })
        {
            context.Component.FindAll("[data-testid='claude-account-subpage-tab']")[index].Click();
            var input = context.Component.Find("[data-testid='claude-api-key-input']");
            input.Input(key);
            input.Change();
        }

        context.Component.WaitForAssertion(() =>
        {
            Assert.Equal(
                "Two Claude organizations report usage; GitHub and Azure Foundry are not set up.",
                context.Component.Find("[data-testid='settings-headline']").TextContent.Trim());

            var claude = context.Component.FindAll("[data-testid='ai-provider-tile']")[0];
            Assert.Equal("true", claude.GetAttribute("data-configured"));
            Assert.DoesNotContain("ai-provider-tile--unset", claude.ClassList);
            Assert.Contains("2 organizations", claude.TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Github_counts_one_organization_for_this_machines_sign_in_and_foundry_reads_ready()
    {
        using var context = RenderSettings(
            aiAssistantEnabled: true,
            usageMetricsEnabled: true,
            connection: new GitHubConnection(true, "Connected through the GitHub CLI as JSdotNet.", "JSdotNet"));
        Assert.Null(context.AzureFoundryStore.SetConnection("https://ai.example.openai.azure.com", "gpt-5.4", null, AzureFoundrySettingsStore.DefaultApiVersion));
        Assert.Null(context.AzureFoundryStore.SetApiKey("key"));

        OpenAiTab(context.Component);

        context.Component.WaitForAssertion(() =>
        {
            Assert.Equal(
                "GitHub and Azure Foundry report usage; Claude is not set up.",
                context.Component.Find("[data-testid='settings-headline']").TextContent.Trim());

            var tiles = context.Component.FindAll("[data-testid='ai-provider-tile']");
            Assert.Contains("1 organization", tiles[1].TextContent, StringComparison.Ordinal);
            Assert.Contains("Ready", tiles[2].TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("The AI assistant needs it", tiles[2].TextContent, StringComparison.Ordinal);
        });
        Assert.Empty(context.Component.FindAll("[data-testid='azure-foundry-assistant-hidden']"));
    }

    /// <summary>
    /// Under each figure, when its usage was last read on this device: a dot and
    /// how long ago, or the reason the last read failed, or nothing at all when it
    /// was never read. A read that lands while the page is open shows up on it.
    /// </summary>
    [Fact]
    public void Each_tile_says_when_its_usage_was_last_read()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        var reads = new StubUsageReadStatus();
        reads.Set(UsageSource.GitHub, new UsageReadStatus(clock.GetUtcNow().AddMinutes(-6), null, null));
        using var context = RenderSettings(
            aiAssistantEnabled: true,
            usageMetricsEnabled: true,
            connection: new GitHubConnection(true, "Connected.", "JSdotNet"),
            reads: reads,
            clock: clock);

        OpenAiTab(context.Component);
        var key = context.Component.Find("[data-testid='claude-api-key-input']");
        key.Input("sk-ant-admin01-personal");
        key.Change();

        context.Component.WaitForAssertion(() =>
        {
            var tiles = context.Component.FindAll("[data-testid='ai-provider-tile']");
            Assert.Empty(tiles[0].QuerySelectorAll("[data-testid='ai-provider-read']"));

            var github = tiles[1].QuerySelector("[data-testid='ai-provider-read']");
            Assert.NotNull(github);
            Assert.Equal("Read 6 min ago", github.TextContent.Trim());
            Assert.Contains("ai-provider-tile__read--ok", github.ClassList);
            Assert.NotNull(github.QuerySelector(".ai-provider-tile__dot"));

            Assert.Empty(tiles[2].QuerySelectorAll("[data-testid='ai-provider-read']"));
        });

        var account = context.ClaudeStore.Current.Accounts[0];
        reads.Set(UsageSource.Claude(account.Id), new UsageReadStatus(null, clock.GetUtcNow(), "401 Unauthorized"));

        context.Component.WaitForAssertion(() =>
        {
            var claude = context.Component.FindAll("[data-testid='ai-provider-tile']")[0].QuerySelector("[data-testid='ai-provider-read']");
            Assert.NotNull(claude);
            Assert.Equal("Last read failed: 401 Unauthorized", claude.TextContent.Trim());
            Assert.Contains("ai-provider-tile__read--failed", claude.ClassList);
            Assert.Null(claude.QuerySelector(".ai-provider-tile__dot"));
        });
    }

    /// <summary>
    /// The assistant's feature is on, and the assistant is still nowhere: the card
    /// whose fields fix that says why, until they are filled in.
    /// </summary>
    [Fact]
    public void The_foundry_card_warns_that_the_assistant_stays_hidden_until_it_is_set_up()
    {
        using var context = RenderSettings(aiAssistantEnabled: true, usageMetricsEnabled: false);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
        {
            var warning = context.Component.Find("[data-testid='azure-foundry-settings'] [data-testid='azure-foundry-assistant-hidden']");
            Assert.Equal(
                "Until both are filled in, the AI assistant stays hidden even though its feature is on.",
                warning.TextContent.Trim());
        });

        var endpoint = context.Component.Find("[data-testid='azure-foundry-endpoint-input']");
        endpoint.Input("https://ai.example.openai.azure.com");
        endpoint.Change();
        var deployment = context.Component.Find("[data-testid='azure-foundry-deployment-input']");
        deployment.Input("gpt-5.4");
        deployment.Change();
        var key = context.Component.Find("[data-testid='azure-foundry-api-key-input']");
        key.Input("key");
        key.Change();

        context.Component.WaitForAssertion(() =>
            Assert.Empty(context.Component.FindAll("[data-testid='azure-foundry-assistant-hidden']")));
    }

    /// <summary>
    /// The cards sit in two columns: Claude's alone on the left, GitHub's and Azure
    /// Foundry's stacked on the right. Claude's organizations are pages on the
    /// shared tab strip, added from the card's header.
    /// </summary>
    [Fact]
    public void Claude_stands_left_with_its_organizations_on_the_shared_tab_strip()
    {
        using var context = RenderSettings(aiAssistantEnabled: true, usageMetricsEnabled: true);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Equal(2, context.Component.FindAll(".ai-providers__cards > .ai-providers__column").Count));

        var columns = context.Component.FindAll(".ai-providers__cards > .ai-providers__column");
        Assert.Equal(
            ["claude-usage-settings"],
            columns[0].Children.Select(card => card.GetAttribute("data-testid")).ToArray());
        Assert.Equal(
            ["github-usage-settings", "azure-foundry-settings"],
            columns[1].Children.Select(card => card.GetAttribute("data-testid")).ToArray());

        var add = context.Component.Find("[data-testid='claude-usage-settings'] .settings-card__action [data-testid='add-claude-account-button']");
        Assert.Equal("Add organization", add.TextContent.Trim());

        var tab = Assert.Single(context.Component.FindAll("[data-testid='claude-account-subpage-tab']"));
        Assert.Contains("tabs__tab--active", tab.ClassList);
        Assert.Contains("tabs__list", context.Component.Find("[data-testid='claude-account-subpages']").ClassList);
    }

    [Fact]
    public void The_page_declares_its_card_titles_for_the_settings_search()
    {
        var page = Assert.Single(SettingsPages.BuiltIn, page => page.Id == SettingsPages.Ai);

        Assert.Equal("AI providers", page.Title);
        Assert.Equal(["Claude usage API", "GitHub usage API", "Azure Foundry AI"], page.CardTitles);
    }

    /// <summary>
    /// The page declares, for the search and for a card's address, only the cards
    /// it draws under the features as they stand - so a switched-off feature's
    /// cards are never offered as a link to nothing.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void The_cards_the_page_declares_are_the_cards_it_draws(bool aiAssistantEnabled, bool usageMetricsEnabled)
    {
        using var context = RenderSettings(aiAssistantEnabled, usageMetricsEnabled);

        OpenAiTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.NotEmpty(context.Component.FindAll(".ai-providers__cards .settings-card__title")));

        Assert.Equal(
            SettingsPages.AiCardTitles(usageMetricsEnabled, aiAssistantEnabled),
            context.Component.FindAll(".ai-providers__cards .settings-card__title").Select(title => title.TextContent.Trim()));
    }

    [Fact]
    public void With_usage_metrics_off_finding_usage_lists_no_ai_card()
    {
        using var context = RenderSettings(aiAssistantEnabled: true, usageMetricsEnabled: false);

        context.Component.Find("[data-testid='settings-search-input']").Input("usage");

        Assert.DoesNotContain(
            context.Component.FindAll("[data-testid='settings-menu-card']"),
            card => card.GetAttribute("href")!.StartsWith("settings/ai#", StringComparison.Ordinal));
    }

    [Fact]
    public void With_usage_metrics_on_finding_usage_lists_both_usage_cards_as_links()
    {
        using var context = RenderSettings(aiAssistantEnabled: false, usageMetricsEnabled: true);

        context.Component.Find("[data-testid='settings-search-input']").Input("usage");

        Assert.Equal(
            ["settings/ai#card-claude-usage-api", "settings/ai#card-github-usage-api"],
            context.Component.FindAll("[data-testid='settings-menu-card']")
                .Select(card => card.GetAttribute("href"))
                .Where(href => href!.StartsWith("settings/ai#", StringComparison.Ordinal)));
    }

    /// <summary>A read model the test writes into, raising Changed as a reader would.</summary>
    private sealed class StubUsageReadStatus : IUsageReadStatus
    {
        private readonly Dictionary<UsageSource, UsageReadStatus> _statuses = [];

        public event Action? Changed;

        public IReadOnlyDictionary<UsageSource, UsageReadStatus> All => _statuses;

        public UsageReadStatus? For(UsageSource source) => _statuses.GetValueOrDefault(source);

        public void Set(UsageSource source, UsageReadStatus status)
        {
            _statuses[source] = status;
            Changed?.Invoke();
        }
    }

    private static string[] SettingsTabs(IRenderedComponent<Settings> component) =>
        SettingsMenu.Pages(component);

    private static void OpenAiTab(IRenderedComponent<Settings> component) =>
        SettingsMenu.Open(component, "AI providers");

    private static SettingsRenderContext RenderSettings(
        bool aiAssistantEnabled,
        bool usageMetricsEnabled,
        Action<GitHubSettingsStore>? seed = null,
        IClaudeAccountProbe? claudeProbe = null,
        GitHubConnection? connection = null,
        IGitHubAccountProbe? gitHubAccountProbe = null,
        IAzureFoundryConnectionProbe? foundryProbe = null,
        IUsageReadStatus? reads = null,
        TimeProvider? clock = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-tests", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, aiAssistantEnabled);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, usageMetricsEnabled);

        var azureFoundry = new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json"));
        var claude = new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json"));
        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, _) = GitHubSettings.ParseText("JSdotNet/Backlog");
        _ = githubSettings.SetRepositories(repositories);
        seed?.Invoke(githubSettings);

        var github = new GitHubIntegration(githubSettings, new StubGitHubClient(), new StubProbe(connection), accountProbe: gitHubAccountProbe);

        var testContext = new BunitContext();
        testContext.Services.AddSingleton(store);
        testContext.Services.AddSingleton<IAppFeatureSettings>(features);
        testContext.Services.AddSingleton<IWorkingHoursSettings>(
            new WorkingHoursSettingsStore(Path.Combine(root, "working-hours", "working-hours.json")));
        testContext.Services.AddSingleton<IUsageResetSettings>(
            new UsageResetSettingsStore(Path.Combine(root, "usage-reset", "usage-reset.json")));
        testContext.Services.AddSingleton<ICaptureSourceSettings>(
            new CaptureSourcesSettingsStore(Path.Combine(root, "capture", "capture-sources.json")));
        testContext.Services.AddSingleton(azureFoundry);
        testContext.Services.AddSingleton(claude);
        if (claudeProbe is not null) testContext.Services.AddSingleton(claudeProbe);
        if (foundryProbe is not null) testContext.Services.AddSingleton(foundryProbe);
        if (reads is not null) testContext.Services.AddSingleton(reads);
        if (clock is not null) testContext.Services.AddSingleton(clock);
        testContext.Services.AddSingleton(github);
        testContext.Services.AddSingleton<FeedbackReporter>();
        testContext.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        testContext.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(githubSettings, store));
        testContext.Services.AddSingleton(new DevbookSourceSelection(githubSettings, new StubBranchCatalog()));

        var component = testContext.Render<Settings>();
        return new SettingsRenderContext(root, testContext, component, claude, azureFoundry, github);
    }

    private sealed record SettingsRenderContext(
        string Root,
        BunitContext TestContext,
        IRenderedComponent<Settings> Component,
        ClaudeSettingsStore ClaudeStore,
        AzureFoundrySettingsStore AzureFoundryStore,
        GitHubIntegration GitHub) : IDisposable
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
