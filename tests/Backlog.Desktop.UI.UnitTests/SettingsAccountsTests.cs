using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Accounts panel, and the control that binds a repository to one of them.
/// <para>
/// Asserted against the settings store rather than against what the panel is
/// showing, because the store is what the transport reads: a panel that lit up the
/// right account and wrote nothing would leave every call going out as whoever this
/// machine happens to be signed in as, which is the 404 the whole change exists to
/// remove.
/// </para>
/// <para>
/// bUnit swallows what an event handler throws — <c>Click</c>, <c>Change</c> and
/// <c>UnhandledException</c> all report nothing — so every assertion here is on
/// state a throw would have prevented rather than on the absence of one.
/// </para>
/// </summary>
public sealed class SettingsAccountsTests
{
    [Fact]
    public void The_accounts_tab_offers_the_logins_the_gh_cli_already_holds()
    {
        using var settings = RenderSettings(cliAccounts: [Signed("JSdotNet", active: true)]);
        OpenAccountsTab(settings.Component);

        settings.Component.WaitForElement("[data-testid='add-gh-cli-account-button']").Click();

        var added = settings.GitHub.Current.Account("JSdotNet");
        Assert.NotNull(added);

        // The route that holds no secret: gh keeps the credential, so the account
        // is stored with the kind and nothing else.
        Assert.Equal(GitHubCredentialKind.GhCli, added.Credential);
        Assert.Null(added.Token);

        // github.com is the ordinary case and is stored as no host at all, rather
        // than as the default spelled out on every row.
        Assert.Null(added.Host);
    }

    [Fact]
    public void An_account_the_cli_already_reported_is_not_offered_twice()
    {
        using var settings = RenderSettings(cliAccounts: [Signed("JSdotNet", active: true)]);
        OpenAccountsTab(settings.Component);
        settings.Component.WaitForElement("[data-testid='add-gh-cli-account-button']").Click();

        settings.Component.WaitForAssertion(() =>
        {
            Assert.Empty(settings.Component.FindAll("[data-testid='add-gh-cli-account-button']"));
            Assert.Single(settings.Component.FindAll("[data-testid='gh-cli-accounts-empty']"));
        });
    }

    [Fact]
    public void A_login_can_be_typed_on_a_machine_the_cli_says_nothing_about()
    {
        using var settings = RenderSettings();
        OpenAccountsTab(settings.Component);

        // No save button: leaving the field is what commits it.
        settings.Component.Find("[data-testid='add-account-login-input']").Input("j-schepers_innobv");
        settings.Component.Find("[data-testid='add-account-login-input']").Change("j-schepers_innobv");

        Assert.NotNull(settings.GitHub.Current.Account("j-schepers_innobv"));
    }

    [Fact]
    public void Forgetting_an_account_leaves_the_workspaces_binding_alone()
    {
        using var settings = RenderSettings(seed: store =>
        {
            Assert.Null(store.SetAccounts([new GitHubAccount("JSdotNet")]));
            Assert.Null(store.SetRepositoryAccount("backlog", "JSdotNet"));
        });

        OpenAccountsTab(settings.Component);
        ExpandAccount(settings.Component, "JSdotNet");
        settings.Component.WaitForElement("[data-testid='remove-account-button']").Click();

        Assert.Null(settings.GitHub.Current.Account("JSdotNet"));

        // The binding is workspace data and forgetting a credential is a machine
        // act, so removing one here must not rewrite the shared registry. What is
        // left is an unsatisfied binding, which is a state with a name.
        Assert.Equal("JSdotNet", settings.GitHub.Current.Find("backlog")!.Account);
    }

    [Fact]
    public void Choosing_an_account_on_the_repository_card_binds_it_straight_away()
    {
        using var settings = RenderSettings(seed: store =>
            Assert.Null(store.SetAccounts([new GitHubAccount("JSdotNet")])));

        OpenRepositoryCard(settings.Component);
        settings.Component.Find("[data-testid='repo-account-select'] select").Change("JSdotNet");

        Assert.Equal("JSdotNet", settings.GitHub.Current.Find("backlog")!.Account);

        // The other repository is untouched: a binding is per repository, which is
        // the whole point of having one.
        Assert.Null(settings.GitHub.Current.Find("spec")!.Account);
    }

    [Fact]
    public void The_empty_option_is_how_a_repository_goes_back_to_the_default()
    {
        using var settings = RenderSettings(seed: store =>
        {
            Assert.Null(store.SetAccounts([new GitHubAccount("JSdotNet")]));
            Assert.Null(store.SetRepositoryAccount("backlog", "JSdotNet"));
        });

        OpenRepositoryCard(settings.Component);
        settings.Component.Find("[data-testid='repo-account-select'] select").Change(string.Empty);

        Assert.Null(settings.GitHub.Current.Find("backlog")!.Account);
        settings.Component.WaitForAssertion(() =>
            Assert.Contains("Default", Status(settings.Component, "repo-account-status")));
    }

    [Fact]
    public void A_binding_this_machine_cannot_satisfy_says_so_rather_than_looking_fine()
    {
        using var settings = RenderSettings(seed: store =>
        {
            Assert.Null(store.SetAccounts([new GitHubAccount("j-schepers_innobv")]));
            Assert.Null(store.SetRepositoryAccount("spec", "j-schepers_innobv"));

            // The day-one shape of a second install: the workspace names a login
            // this machine holds no credential for.
            Assert.Null(store.RemoveAccount("j-schepers_innobv"));
        });

        OpenAccountsTab(settings.Component);

        var reported = settings.Component.Find("[data-testid='unsatisfied-binding']").TextContent;

        // Both halves somebody needs: what was reached for, and as whom.
        Assert.Contains("innovadis-dev/spec-manager", reported);
        Assert.Contains("j-schepers_innobv", reported);

        OpenRepositoryCard(settings.Component);
        SettingsMenu.EditRepository(settings.Component, "spec");

        settings.Component.WaitForAssertion(() =>
        {
            Assert.Contains("no account for that login", Status(settings.Component, "repo-account-status"));

            // The select has to keep showing the login the registry names. Without
            // an option carrying it the control falls back to its first entry — the
            // empty one — and reads as "Default", which is the opposite of the
            // truth and hides the state this whole panel exists to report.
            var options = settings.Component
                .FindAll("[data-testid='repo-account-select'] option")
                .Select(option => option.GetAttribute("value"))
                .ToArray();

            Assert.Contains("j-schepers_innobv", options);
            Assert.Equal(
                "j-schepers_innobv",
                settings.Component.Find("[data-testid='repo-account-select'] select").GetAttribute("value"));
        });
    }

    /// <summary>Local ADR 0021: the identities travel to paired devices and the
    /// credential does not, and the panel says exactly that rather than the old
    /// "never synced".</summary>
    [Fact]
    public void The_accounts_note_says_identities_sync_and_credentials_stay_on_this_machine()
    {
        using var settings = RenderSettings(seed: store =>
            Assert.Null(store.SetAccounts([new GitHubAccount("JSdotNet")])));

        OpenAccountsTab(settings.Component);
        ExpandAccount(settings.Component, "JSdotNet");

        var note = settings.Component.WaitForElement("[data-testid='accounts-storage-note']").TextContent;
        Assert.Contains("sync to your paired devices", note, StringComparison.Ordinal);
        Assert.Contains("token", note, StringComparison.Ordinal);
        Assert.Contains("stay on this machine", note, StringComparison.Ordinal);
        Assert.DoesNotContain("never synced", note, StringComparison.Ordinal);
    }

    /// <summary>Local ADR 0021 Verification 4: a registry pulled from another device
    /// can bind a repository to a login this machine has no account for yet. The open
    /// panel reloads and lists the binding as unsatisfied — it never reads as the
    /// default.</summary>
    [Fact]
    public async Task A_binding_pulled_before_its_account_is_listed_as_unsatisfied()
    {
        using var settings = RenderSettings();
        OpenAccountsTab(settings.Component);
        Assert.Empty(settings.Component.FindAll("[data-testid='unsatisfied-binding']"));

        var other = new GitHubSettingsStore(
            Path.Combine(settings.Root, "other-device", "github.json"),
            () => Path.Combine(settings.Root, "other-device", "workspace"));
        Assert.Null(other.SetAccounts([new GitHubAccount("j-schepers_innobv")]));
        Assert.Null(other.SetRepositories(GitHubSettings.ParseText("spec = innovadis-dev/spec-manager").Repositories));
        Assert.Null(other.SetRepositoryAccount("spec", "j-schepers_innobv"));
        var registry = await new GitHubSettingsReplication(other).ReadAsync(
            GitHubReplicaDocument.Registry, TestContext.Current.CancellationToken);

        var outcome = await new GitHubSettingsReplication(settings.GitHub).ApplyAsync(
            GitHubReplicaDocument.Registry,
            registry! with { UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(1) },
            TestContext.Current.CancellationToken);
        Assert.Equal(GitHubReplicaOutcome.Taken, outcome);

        settings.Component.WaitForAssertion(() =>
        {
            var reported = settings.Component.Find("[data-testid='unsatisfied-binding']").TextContent;
            Assert.Contains("innovadis-dev/spec-manager", reported, StringComparison.Ordinal);
            Assert.Contains("j-schepers_innobv", reported, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void An_account_with_no_usable_credential_says_so()
    {
        using var settings = RenderSettings(seed: store =>
            Assert.Null(store.SetAccounts([new GitHubAccount("JSdotNet")])));

        OpenAccountsTab(settings.Component);
        ExpandAccount(settings.Component, "JSdotNet");
        settings.Component.Find("[data-testid='account-credential-select'] select").Change("PersonalAccessToken");

        Assert.Equal(
            GitHubCredentialKind.PersonalAccessToken,
            settings.GitHub.Current.Account("JSdotNet")!.Credential);

        settings.Component.WaitForAssertion(() =>
            Assert.Contains("No personal access token has been pasted", Status(settings.Component, "account-status")));
    }

    [Fact]
    public void A_pasted_token_is_stored_against_the_account_it_was_pasted_on()
    {
        using var settings = RenderSettings(seed: store => Assert.Null(store.SetAccounts(
        [
            new GitHubAccount("JSdotNet") { Credential = GitHubCredentialKind.PersonalAccessToken }
        ])));

        OpenAccountsTab(settings.Component);
        ExpandAccount(settings.Component, "JSdotNet");
        settings.Component.Find("[data-testid='account-token-input']").Input("ghp_pasted");
        settings.Component.Find("[data-testid='account-token-input']").Change("ghp_pasted");

        Assert.Equal("ghp_pasted", settings.GitHub.Current.Account("JSdotNet")!.Token);

        settings.Component.WaitForElement("[data-testid='account-clear-token-button']").Click();

        Assert.Null(settings.GitHub.Current.Account("JSdotNet")!.Token);
    }

    /// <summary>An endpoint typed on an account's card lands on that account and on
    /// no other; blank hands the login back to the install-wide endpoint.</summary>
    [Fact]
    public void An_endpoint_typed_on_an_account_card_is_stored_against_that_account()
    {
        using var settings = RenderSettings(seed: store => Assert.Null(store.SetAccounts(
        [
            new GitHubAccount("JSdotNet"),
            new GitHubAccount("octocat")
        ])));

        OpenAccountsTab(settings.Component);
        ExpandAccount(settings.Component, "octocat");

        var endpoint = settings.Component.Find("[data-testid='account-endpoint-input']");
        Assert.Equal("account-endpoint-octocat", endpoint.GetAttribute("id"));
        Assert.Equal(GitHubSettings.DefaultApiEndpoint, endpoint.GetAttribute("placeholder"));

        endpoint.Input(" https://ghe.example.internal/api/v3/ ");
        endpoint.Change(" https://ghe.example.internal/api/v3/ ");

        Assert.Equal("https://ghe.example.internal/api/v3", settings.GitHub.Current.Account("octocat")!.ApiEndpoint);
        Assert.Null(settings.GitHub.Current.Account("JSdotNet")!.ApiEndpoint);
        Assert.Equal(GitHubSettings.DefaultApiEndpoint, settings.GitHub.Current.ApiEndpoint);

        endpoint = settings.Component.Find("[data-testid='account-endpoint-input']");
        endpoint.Input("   ");
        endpoint.Change("   ");

        Assert.Null(settings.GitHub.Current.Account("octocat")!.ApiEndpoint);
    }

    /// <summary>
    /// The single most likely defect in the whole change, driven through the
    /// control somebody actually uses.
    /// <para>
    /// <c>SetRepositories</c> rebuilds every row from parsed text and the grammar
    /// has no account in it, so anything the store does not deliberately carry
    /// across is destroyed the moment the box is edited. For the binding that would
    /// mean silently sending the next call out as the wrong identity — the exact
    /// failure the binding exists to stop, arriving through the one control on this
    /// page nobody would think twice about touching.
    /// </para>
    /// </summary>
    [Fact]
    public void Retyping_the_repository_list_does_not_lose_the_bindings()
    {
        using var settings = RenderSettings(seed: store =>
        {
            Assert.Null(store.SetAccounts(
            [
                new GitHubAccount("JSdotNet"),
                new GitHubAccount("j-schepers_innobv")
            ]));

            Assert.Null(store.SetRepositoryAccount("backlog", "JSdotNet"));
            Assert.Null(store.SetRepositoryAccount("spec", "j-schepers_innobv"));
        });

        OpenRepositoryCard(settings.Component);

        var retyped = settings.GitHub.Current.ToText() + "\ndocs = JSdotNet/Backlog-docs";
        settings.Component.Find("[data-testid='github-repos-input']").Input(retyped);
        settings.Component.Find("[data-testid='github-repos-input']").Change(retyped);

        Assert.Equal(3, settings.GitHub.Current.Repositories.Count);
        Assert.Equal("JSdotNet", settings.GitHub.Current.Find("backlog")!.Account);
        Assert.Equal("j-schepers_innobv", settings.GitHub.Current.Find("spec")!.Account);
        Assert.Null(settings.GitHub.Current.Find("docs")!.Account);
    }

    [Fact]
    public void The_accounts_tab_is_not_offered_when_nothing_would_use_an_account()
    {
        using var settings = RenderSettings(gitHubIntegrationEnabled: false);

        Assert.DoesNotContain("GitHub accounts", SettingsTabs(settings.Component));
        Assert.Empty(settings.Component.FindAll("[data-testid='repo-account-select']"));
    }

    /// <summary>
    /// Forgetting the open account closes its card and leaves the other row
    /// standing, closed: the rows are keyed by login, so octocat's row is the same
    /// row after JSdotNet goes, and no card is handed to a login nobody chose.
    /// </summary>
    [Fact]
    public void Forgetting_an_open_account_closes_its_card_and_keeps_the_other_row()
    {
        using var settings = RenderSettings(seed: store => Assert.Null(store.SetAccounts(
        [
            new GitHubAccount("JSdotNet"),
            new GitHubAccount("octocat")
        ])));
        OpenAccountsTab(settings.Component);
        ExpandAccount(settings.Component, "JSdotNet");

        settings.Component.Find("[data-testid='remove-account-button']").Click();

        Assert.Null(settings.GitHub.Current.Account("JSdotNet"));
        settings.Component.WaitForAssertion(() =>
        {
            var row = Assert.Single(settings.Component.FindAll("[data-testid='account-row']"));
            Assert.Contains("octocat", row.TextContent, StringComparison.Ordinal);
            Assert.Equal("false", row.GetAttribute("aria-expanded"));
            Assert.Empty(settings.Component.FindAll("[data-testid='github-account-card']"));
        });
    }

    [Fact]
    public void With_no_account_the_headline_says_none_is_signed_in()
    {
        using var settings = RenderSettings();
        OpenAccountsTab(settings.Component);

        Assert.Equal("No GitHub account is signed in yet.", Headline(settings.Component));
        Assert.Empty(settings.Component.FindAll("[data-testid='account-row']"));
        Assert.Single(settings.Component.FindAll("[data-testid='github-accounts-empty']"));
    }

    /// <summary>The headline counts the accounts and names the one the GitHub CLI
    /// is using; each row says whether it is that one and how many repositories
    /// are worked as it.</summary>
    [Fact]
    public void The_headline_and_rows_name_the_active_account_and_count_its_repositories()
    {
        using var settings = RenderSettings(
            cliAccounts: [Signed("JSdotNet", active: true), Signed("octocat", active: false)],
            seed: store =>
            {
                Assert.Null(store.SetAccounts([new GitHubAccount("JSdotNet"), new GitHubAccount("octocat")]));
                Assert.Null(store.SetRepositoryAccount("backlog", "JSdotNet"));
                Assert.Null(store.SetRepositoryAccount("spec", "JSdotNet"));
            });
        OpenAccountsTab(settings.Component);

        settings.Component.WaitForAssertion(() =>
        {
            Assert.Equal("2 GitHub accounts signed in; JSdotNet is active.", Headline(settings.Component));

            var meta = settings.Component.FindAll("[data-testid='account-row-meta']")
                .Select(element => element.TextContent.Trim())
                .ToArray();
            Assert.Equal(["Active · 2 repositories", "Signed in · 0 repositories"], meta);
        });
    }

    /// <summary>Without the GitHub CLI there is nobody to say which account is in
    /// use, so the headline counts and names none, and no row claims to be active.</summary>
    [Fact]
    public void Without_a_cli_answer_no_account_is_called_active()
    {
        using var settings = RenderSettings(seed: store =>
        {
            Assert.Null(store.SetAccounts([new GitHubAccount("JSdotNet")]));
            Assert.Null(store.SetRepositoryAccount("backlog", "JSdotNet"));
        });
        OpenAccountsTab(settings.Component);

        Assert.Equal("1 GitHub account signed in.", Headline(settings.Component));
        Assert.Equal("Signed in · 1 repository", settings.Component.Find("[data-testid='account-row-meta']").TextContent.Trim());
    }

    /// <summary>Each row carries a decorative initial and the login in its own
    /// element, and the whole list sits in the "GitHub accounts" card with its
    /// description and the Add account action in the header.</summary>
    [Fact]
    public void The_accounts_card_lists_one_row_per_account()
    {
        using var settings = RenderSettings(seed: store => Assert.Null(store.SetAccounts(
        [
            new GitHubAccount("JSdotNet"),
            new GitHubAccount("octocat")
        ])));
        OpenAccountsTab(settings.Component);

        var card = settings.Component.Find("[data-testid='github-accounts-card']");
        Assert.Equal("GitHub accounts", card.QuerySelector(".settings-card__title")?.TextContent);
        Assert.Equal(
            "Signed in through the GitHub CLI. Each repository picks the account it reads with.",
            card.QuerySelector(".settings-card__description")?.TextContent);
        Assert.Equal("Add account", card.QuerySelector(".settings-card__action [data-testid='accounts-add-button']")?.TextContent.Trim());

        var rows = settings.Component.FindAll("[data-testid='account-row']");
        Assert.Equal(2, rows.Count);
        Assert.Equal("J", rows[0].QuerySelector(".account-row__avatar")?.TextContent);
        Assert.Equal("true", rows[0].QuerySelector(".account-row__avatar")?.GetAttribute("aria-hidden"));
        Assert.Equal("octocat", rows[1].QuerySelector(".account-row__login")?.TextContent);
        Assert.All(rows, row => Assert.Equal("false", row.GetAttribute("aria-expanded")));
        Assert.Empty(settings.Component.FindAll("[data-testid='github-account-card']"));
    }

    /// <summary>One card open at a time, right under its row and across the whole
    /// list; choosing the open row again closes it.</summary>
    [Fact]
    public void Choosing_a_row_opens_its_card_under_it_and_closes_the_other()
    {
        using var settings = RenderSettings(seed: store => Assert.Null(store.SetAccounts(
        [
            new GitHubAccount("JSdotNet"),
            new GitHubAccount("octocat")
        ])));
        OpenAccountsTab(settings.Component);

        ExpandAccount(settings.Component, "JSdotNet");
        AssertOpen(settings.Component, "JSdotNet");

        ExpandAccount(settings.Component, "octocat");
        AssertOpen(settings.Component, "octocat");

        ExpandAccount(settings.Component, "octocat");
        Assert.All(
            settings.Component.FindAll("[data-testid='account-row']"),
            row => Assert.Equal("false", row.GetAttribute("aria-expanded")));
        Assert.Empty(settings.Component.FindAll("[data-testid='github-account-card']"));
    }

    /// <summary>An account just added is the card to look at: its credential is
    /// chosen there.</summary>
    [Fact]
    public void An_account_just_added_opens_its_card()
    {
        using var settings = RenderSettings(cliAccounts: [Signed("JSdotNet", active: true)]);
        OpenAccountsTab(settings.Component);

        settings.Component.WaitForElement("[data-testid='add-gh-cli-account-button']").Click();

        settings.Component.WaitForAssertion(() => AssertOpen(settings.Component, "JSdotNet"));
    }

    [Fact]
    public void Add_account_takes_focus_to_the_login_field()
    {
        using var settings = RenderSettings();
        var focus = settings.TestContext.JSInterop.SetupVoid("backlogFocus", "account-login");
        OpenAccountsTab(settings.Component);

        settings.Component.Find("[data-testid='accounts-add-button']").Click();

        focus.VerifyInvoke("backlogFocus");
        Assert.NotNull(settings.Component.Find("#account-login"));
    }

    private static void AssertOpen(IRenderedComponent<Settings> component, string login)
    {
        var card = Assert.Single(component.FindAll("[data-testid='github-account-card']"));
        Assert.Equal($"{login} account settings", card.GetAttribute("aria-label"));

        // The card's list item comes straight after the open row's, and spans the grid.
        var editorItem = card.ParentElement!;
        Assert.Contains("account-list__editor", editorItem.ClassName, StringComparison.Ordinal);
        var openRow = editorItem.PreviousElementSibling!.QuerySelector("[data-testid='account-row']")!;
        Assert.Contains(login, openRow.TextContent, StringComparison.Ordinal);
        Assert.Equal("true", openRow.GetAttribute("aria-expanded"));
        Assert.Equal(card.GetAttribute("id"), openRow.GetAttribute("aria-controls"));

        Assert.Single(component.FindAll("[data-testid='account-row'][aria-expanded='true']"));
    }

    private static void ExpandAccount(IRenderedComponent<Settings> component, string login) =>
        component.WaitForElements("[data-testid='account-row']")
            .Single(row => string.Equals(
                row.QuerySelector(".account-row__login")?.TextContent, login, StringComparison.OrdinalIgnoreCase))
            .Click();

    private static string Headline(IRenderedComponent<Settings> component) =>
        component.Find("[data-testid='settings-headline']").TextContent.Trim();

    private static GhCliAccount Signed(string login, bool active) =>
        new(login, "github.com", active, "repo, read:org");

    private static string Status(IRenderedComponent<Settings> component, string testId) =>
        component.Find($"[data-testid='{testId}']").TextContent;

    private static string[] SettingsTabs(IRenderedComponent<Settings> component) =>
        SettingsMenu.Pages(component);

    private static void OpenAccountsTab(IRenderedComponent<Settings> component) =>
        SettingsMenu.Open(component, "GitHub accounts");

    private static void OpenRepositoryCard(IRenderedComponent<Settings> component) =>
        SettingsMenu.OpenRepository(component);

    /// <summary>
    /// The card's "Test this account": the probe is asked for the card's own account
    /// and its one sentence lands on the card, with the checklist's last step ticking
    /// on a pass. Nothing is written - the settings are what they were.
    /// </summary>
    [Fact]
    public void Testing_an_account_puts_the_probes_answer_on_its_card()
    {
        var probe = new RecordingAccountProbe(new GitHubAccountCheck(true, "GitHub recognises the token as JSdotNet at https://api.github.com."));
        using var settings = RenderSettings(
            seed: store => Assert.Null(store.SetAccounts([new GitHubAccount("JSdotNet") { Credential = GitHubCredentialKind.PersonalAccessToken, Token = "ghp_example" }])),
            accountProbe: probe);

        OpenAccountsTab(settings.Component);
        ExpandAccount(settings.Component, "JSdotNet");
        var before = settings.GitHub.Current;

        Assert.Equal("false", LastStep(settings.Component, "github-account-setup-steps").GetAttribute("data-done"));
        settings.Component.Find("[data-testid='test-account-button']").Click();

        settings.Component.WaitForAssertion(() =>
        {
            Assert.Contains("GitHub recognises the token as JSdotNet", Status(settings.Component, "account-check-status"));
            Assert.Equal("true", LastStep(settings.Component, "github-account-setup-steps").GetAttribute("data-done"));
        });
        Assert.Equal("JSdotNet", Assert.Single(probe.Asked).Login);
        Assert.Same(before, settings.GitHub.Current);
    }

    [Fact]
    public void A_failed_account_test_is_shown_as_the_problem_it_is()
    {
        var probe = new RecordingAccountProbe(new GitHubAccountCheck(false, "The credential for JSdotNet authenticates as hubot, not JSdotNet. Calls for JSdotNet would leave as the wrong person."));
        using var settings = RenderSettings(
            seed: store => Assert.Null(store.SetAccounts([new GitHubAccount("JSdotNet") { Credential = GitHubCredentialKind.PersonalAccessToken, Token = "ghp_example" }])),
            accountProbe: probe);

        OpenAccountsTab(settings.Component);
        ExpandAccount(settings.Component, "JSdotNet");
        settings.Component.Find("[data-testid='test-account-button']").Click();

        settings.Component.WaitForAssertion(() =>
        {
            var status = settings.Component.Find("[data-testid='account-check-status']");
            Assert.NotNull(status.QuerySelector(".setting__status--error"));
            Assert.Contains("authenticates as hubot", status.TextContent, StringComparison.Ordinal);
            Assert.Equal("false", LastStep(settings.Component, "github-account-setup-steps").GetAttribute("data-done"));
        });
    }

    /// <summary>The checklist follows the card: a token pasted ticks the credential
    /// step, and a forgotten one unticks it.</summary>
    [Fact]
    public void The_setup_checklist_ticks_as_the_card_is_filled_in()
    {
        using var settings = RenderSettings(seed: store =>
            Assert.Null(store.SetAccounts([new GitHubAccount("JSdotNet") { Credential = GitHubCredentialKind.PersonalAccessToken }])));

        OpenAccountsTab(settings.Component);
        ExpandAccount(settings.Component, "JSdotNet");

        var steps = settings.Component.FindAll("[data-testid='github-account-setup-steps'] [data-testid='setup-step']");
        Assert.Equal("false", steps[0].GetAttribute("data-done"));

        var token = settings.Component.Find("[data-testid='account-token-input']");
        token.Input("ghp_example");
        token.Change();

        settings.Component.WaitForAssertion(() =>
            Assert.Equal("true", settings.Component.FindAll("[data-testid='github-account-setup-steps'] [data-testid='setup-step']")[0].GetAttribute("data-done")));
    }

    private static AngleSharp.Dom.IElement LastStep(IRenderedComponent<Settings> component, string listTestId) =>
        component.FindAll($"[data-testid='{listTestId}'] [data-testid='setup-step']")[^1];

    private sealed class RecordingAccountProbe(GitHubAccountCheck answer) : IGitHubAccountProbe
    {
        public List<GitHubAccount> Asked { get; } = [];

        public Task<GitHubAccountCheck> CheckAccountAsync(GitHubAccount account, CancellationToken cancellationToken = default)
        {
            Asked.Add(account);
            return Task.FromResult(answer);
        }
    }

    private static SettingsRenderContext RenderSettings(
        IReadOnlyList<GhCliAccount>? cliAccounts = null,
        Action<GitHubSettingsStore>? seed = null,
        bool gitHubIntegrationEnabled = true,
        IGitHubAccountProbe? accountProbe = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-accounts-tests", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, gitHubIntegrationEnabled);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);

        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText(
            "backlog = JSdotNet/Backlog\nspec = innovadis-dev/spec-manager");
        Assert.Empty(errors);
        Assert.Null(githubSettings.SetRepositories(repositories));
        seed?.Invoke(githubSettings);

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IWorkingHoursSettings>(
            new WorkingHoursSettingsStore(Path.Combine(root, "working-hours", "working-hours.json")));
        context.Services.AddSingleton<IUsageResetSettings>(
            new UsageResetSettingsStore(Path.Combine(root, "usage-reset", "usage-reset.json")));
        context.Services.AddSingleton<ICaptureSourceSettings>(
            new CaptureSourcesSettingsStore(Path.Combine(root, "capture", "capture-sources.json")));
        context.Services.AddSingleton(new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json")));
        context.Services.AddSingleton(new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json")));
        context.Services.AddSingleton(new GitHubIntegration(
            githubSettings,
            new NoGitHub(),
            new NoProbe(),
            new StubCliAccounts(cliAccounts ?? []),
            accountProbe));
        context.Services.AddSingleton<FeedbackReporter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(githubSettings, store));
        context.Services.AddSingleton(new DevbookSourceSelection(githubSettings, new StubBranchCatalog()));

        return new SettingsRenderContext(root, context, context.Render<Settings>(), githubSettings);
    }

    /// <summary>The <c>gh</c> CLI without the subprocess. It answers with whatever
    /// the test says the machine is signed in to, and never with a token: a
    /// gh-sourced token is fetched per call and is not this panel's business.</summary>
    private sealed class StubCliAccounts(IReadOnlyList<GhCliAccount> accounts) : IGhCliAccountSource
    {
        public Task<IReadOnlyList<GhCliAccount>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(accounts);

        public Task<string?> GetTokenAsync(
            string login,
            string? host = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public void Invalidate()
        {
        }
    }

    /// <summary>Nothing here reaches GitHub — which account a call would leave as is
    /// a local choice, not a fact fetched from it.</summary>
    private sealed class NoGitHub : IGitHubClient
    {
        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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
