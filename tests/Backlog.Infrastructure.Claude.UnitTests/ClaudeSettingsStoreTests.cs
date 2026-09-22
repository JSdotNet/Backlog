using System.Text.Json;
using Backlog.Infrastructure.Claude;

namespace Backlog.Infrastructure.Claude.UnitTests;

public class ClaudeSettingsStoreTests
{
    [Fact]
    public void A_missing_settings_file_reads_as_one_unconfigured_account()
    {
        using var directory = new TemporaryDirectory();

        var store = new ClaudeSettingsStore(directory.File("claude.json"));

        var account = Assert.Single(store.Current.Accounts);
        Assert.False(store.Current.IsConfigured);
        Assert.False(account.IsConfigured);
        Assert.Equal(ClaudeSettingsStore.DefaultApiVersion, account.ApiVersion);
        Assert.Equal(ClaudeSettingsStore.DefaultApiEndpoint, account.ApiEndpoint);
        Assert.False(string.IsNullOrWhiteSpace(account.Id));
    }

    [Fact]
    public void A_saved_key_survives_a_restart()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.File("claude.json");

        var store = new ClaudeSettingsStore(path);
        store.SetAdminApiKey(store.Current.Accounts[0].Id, "  sk-ant-admin01-example  ");

        var reopened = new ClaudeSettingsStore(path);

        var account = Assert.Single(reopened.Current.Accounts);
        Assert.Equal("sk-ant-admin01-example", account.AdminApiKey);
        Assert.True(account.LooksLikeAdminKey);
    }

    /// <summary>
    /// The file this store wrote before accounts existed was one flat object. It has
    /// to read as one account with nothing lost — a key somebody pasted months ago
    /// must not vanish because the file grew a list — and the first save moves it to
    /// the new shape for good.
    /// </summary>
    [Fact]
    public void A_flat_settings_file_from_before_accounts_reads_as_one_account_and_is_rewritten_as_a_list()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.File("claude.json");
        File.WriteAllText(path, """
            {
              "adminApiKey": "sk-ant-admin01-legacy",
              "workspaceId": "wrkspc_legacy",
              "actor": "person@example.com",
              "apiVersion": "2023-06-01",
              "apiEndpoint": "https://claude.example.internal"
            }
            """);

        var store = new ClaudeSettingsStore(path);

        var account = Assert.Single(store.Current.Accounts);
        Assert.Equal("sk-ant-admin01-legacy", account.AdminApiKey);
        Assert.Equal("wrkspc_legacy", account.WorkspaceId);
        Assert.Equal("person@example.com", account.Actor);
        Assert.Equal("https://claude.example.internal", account.ApiEndpoint);

        store.SetDisplayName(account.Id, "work");

        using var written = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(written.RootElement.TryGetProperty("accounts", out var accounts));
        Assert.Equal(1, accounts.GetArrayLength());
        Assert.False(written.RootElement.TryGetProperty("adminApiKey", out _));

        var reopened = Assert.Single(new ClaudeSettingsStore(path).Current.Accounts);
        Assert.Equal("sk-ant-admin01-legacy", reopened.AdminApiKey);
        Assert.Equal("work", reopened.DisplayName);
    }

    [Fact]
    public void Two_accounts_keep_their_own_keys_endpoints_and_actors_across_a_restart()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.File("claude.json");

        var store = new ClaudeSettingsStore(path);
        var personal = store.Current.Accounts[0].Id;
        store.SetDisplayName(personal, "personal");
        store.SetAdminApiKey(personal, "sk-ant-admin01-personal");
        store.SetActor(personal, "me@example.com");

        Assert.Null(store.AddAccount());
        var work = store.Current.Accounts[1].Id;
        store.SetDisplayName(work, "work");
        store.SetAdminApiKey(work, "sk-ant-admin01-work");
        store.SetActor(work, "me@employer.example");
        store.SetApiEndpoint(work, "https://claude.employer.example/");
        store.SetWorkspaceId(work, "wrkspc_team");

        var reopened = new ClaudeSettingsStore(path).Current;

        Assert.Equal(2, reopened.Accounts.Count);
        Assert.Equal(["personal", "work"], reopened.Accounts.Select(a => a.DisplayName));
        Assert.Equal("sk-ant-admin01-personal", reopened.Account(personal)!.AdminApiKey);
        Assert.Equal(ClaudeSettingsStore.DefaultApiEndpoint, reopened.Account(personal)!.ApiEndpoint);
        Assert.Equal("sk-ant-admin01-work", reopened.Account(work)!.AdminApiKey);
        Assert.Equal("https://claude.employer.example", reopened.Account(work)!.ApiEndpoint);
        Assert.Equal("wrkspc_team", reopened.Account(work)!.WorkspaceId);
        Assert.Equal(2, reopened.ReportingAccounts.Count);
    }

    [Fact]
    public void Removing_an_account_forgets_its_key_and_removing_the_last_leaves_a_blank_one()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.File("claude.json");

        var store = new ClaudeSettingsStore(path);
        var first = store.Current.Accounts[0].Id;
        store.SetAdminApiKey(first, "sk-ant-admin01-first");
        store.AddAccount();
        var second = store.Current.Accounts[1].Id;
        store.SetAdminApiKey(second, "sk-ant-admin01-second");

        Assert.Null(store.RemoveAccount(first));

        Assert.DoesNotContain("sk-ant-admin01-first", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal(second, Assert.Single(store.Current.Accounts).Id);

        Assert.Null(store.RemoveAccount(second));

        var blank = Assert.Single(store.Current.Accounts);
        Assert.False(blank.IsConfigured);
        Assert.NotEqual(second, blank.Id);
        Assert.NotNull(store.RemoveAccount(second));
    }

    [Fact]
    public void A_custom_api_endpoint_is_normalized_and_survives_a_restart()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.File("claude.json");

        var store = new ClaudeSettingsStore(path);
        store.SetApiEndpoint(store.Current.Accounts[0].Id, " https://claude.example.internal/v1/ ");

        var reopened = new ClaudeSettingsStore(path);

        Assert.Equal("https://claude.example.internal/v1", reopened.Current.Accounts[0].ApiEndpoint);
    }

    [Fact]
    public void Setting_a_workspace_leaves_the_key_alone()
    {
        using var directory = new TemporaryDirectory();
        var store = new ClaudeSettingsStore(directory.File("claude.json"));
        var id = store.Current.Accounts[0].Id;

        store.SetAdminApiKey(id, "sk-ant-admin01-example");
        store.SetWorkspaceId(id, "wrkspc_01");

        Assert.Equal("sk-ant-admin01-example", store.Current.Accounts[0].AdminApiKey);
        Assert.Equal("wrkspc_01", store.Current.Accounts[0].WorkspaceId);
    }

    [Fact]
    public void Clearing_the_key_leaves_nothing_behind_on_disk()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.File("claude.json");

        var store = new ClaudeSettingsStore(path);
        var id = store.Current.Accounts[0].Id;
        store.SetAdminApiKey(id, "sk-ant-admin01-example");
        store.ClearAdminApiKey(id);

        Assert.DoesNotContain("sk-ant-admin01-example", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.False(new ClaudeSettingsStore(path).Current.IsConfigured);
    }

    [Fact]
    public void A_corrupt_settings_file_never_stops_the_app_from_opening()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.File("claude.json");
        File.WriteAllText(path, "{ not json at all");

        var store = new ClaudeSettingsStore(path);

        Assert.False(store.Current.IsConfigured);
        Assert.Single(store.Current.Accounts);
    }

    [Fact]
    public void Changing_the_settings_tells_whoever_is_listening()
    {
        using var directory = new TemporaryDirectory();
        var store = new ClaudeSettingsStore(directory.File("claude.json"));

        var changes = 0;
        store.Changed += () => changes++;

        store.SetAdminApiKey(store.Current.Accounts[0].Id, "sk-ant-admin01-example");

        Assert.Equal(1, changes);
    }

    [Fact]
    public void The_label_is_the_most_specific_thing_typed_so_far()
    {
        var blank = new ClaudeAccount();
        Assert.Equal("Claude account", blank.Label);

        Assert.Equal("claude.example.internal", (blank with { ApiEndpoint = "https://claude.example.internal" }).Label);
        Assert.Equal("me@example.com", (blank with { ApiEndpoint = "https://claude.example.internal", Actor = "me@example.com" }).Label);
        Assert.Equal("work", (blank with { Actor = "me@example.com", DisplayName = " work " }).Label);
    }

    /// <summary>Blank is the state a fresh account starts in; any typed field, the
    /// endpoint included, ends it. The id does not count - every account has one.</summary>
    [Fact]
    public void An_account_is_blank_until_something_is_typed_into_it()
    {
        var blank = new ClaudeAccount();
        Assert.True(blank.IsBlank);

        Assert.False((blank with { DisplayName = "work" }).IsBlank);
        Assert.False((blank with { AdminApiKey = "sk-ant-admin01-example" }).IsBlank);
        Assert.False((blank with { WorkspaceId = "wrkspc_01" }).IsBlank);
        Assert.False((blank with { Actor = "me@example.com" }).IsBlank);
        Assert.False((blank with { ApiEndpoint = "https://claude.example.internal" }).IsBlank);
    }
}
