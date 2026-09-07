using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
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

        OpenRepositoriesTab(settings.Component);
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

        OpenRepositoriesTab(settings.Component);
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

        OpenRepositoriesTab(settings.Component);
        ShowRepositorySubpage(settings.Component, "spec");

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

    [Fact]
    public void An_account_with_no_usable_credential_says_so()
    {
        using var settings = RenderSettings(seed: store =>
            Assert.Null(store.SetAccounts([new GitHubAccount("JSdotNet")])));

        OpenAccountsTab(settings.Component);
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
        settings.Component.Find("[data-testid='account-token-input']").Input("ghp_pasted");
        settings.Component.Find("[data-testid='account-token-input']").Change("ghp_pasted");

        Assert.Equal("ghp_pasted", settings.GitHub.Current.Account("JSdotNet")!.Token);

        settings.Component.WaitForElement("[data-testid='account-clear-token-button']").Click();

        Assert.Null(settings.GitHub.Current.Account("JSdotNet")!.Token);
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

        OpenRepositoriesTab(settings.Component);

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

        Assert.DoesNotContain("Accounts", SettingsTabs(settings.Component));
        Assert.Empty(settings.Component.FindAll("[data-testid='repo-account-select']"));
    }

    private static GhCliAccount Signed(string login, bool active) =>
        new(login, "github.com", active, "repo, read:org");

    private static string Status(IRenderedComponent<Settings> component, string testId) =>
        component.Find($"[data-testid='{testId}']").TextContent;

    private static string[] SettingsTabs(IRenderedComponent<Settings> component) =>
        [.. component.FindAll(".settings-tabs button").Select(button => button.TextContent.Trim())];

    private static void OpenAccountsTab(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "Accounts").Click();

    private static void OpenRepositoriesTab(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "Repositories").Click();

    private static void ShowRepositorySubpage(IRenderedComponent<Settings> component, string alias) =>
        component.FindAll("[data-testid='repo-subpage-tab']")
            .Single(tab => tab.TextContent.Trim().Contains(alias, StringComparison.OrdinalIgnoreCase)
                           || tab.TextContent.Trim().EndsWith(alias, StringComparison.OrdinalIgnoreCase))
            .Click();

    private static SettingsRenderContext RenderSettings(
        IReadOnlyList<GhCliAccount>? cliAccounts = null,
        Action<GitHubSettingsStore>? seed = null,
        bool gitHubIntegrationEnabled = true)
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
        context.Services.AddSingleton<ITasksRefreshSettings>(
            new TasksRefreshSettingsStore(Path.Combine(root, "refresh", "refresh.json")));
        context.Services.AddSingleton(new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json")));
        context.Services.AddSingleton(new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json")));
        context.Services.AddSingleton(new GitHubIntegration(
            githubSettings,
            new NoGitHub(),
            new NoProbe(),
            new StubCliAccounts(cliAccounts ?? [])));
        context.Services.AddSingleton<FeedbackReporter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(githubSettings, store));

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
