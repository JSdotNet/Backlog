using System.Text.Json;
using System.Text.Json.Nodes;

using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.Services;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The repository registry and the account identities as two whole documents that
/// travel between paired devices (local ADR 0020).
/// <para>
/// A device is a per-user <c>github.json</c> over its own workspace root — never a
/// shared one, because the point of the decision is that the root is on a local disk
/// and no file sync carries <c>repos.json</c>. A document travels by reading it off
/// one device's port and offering it to the other's, which is exactly what the sync
/// client does with it and nothing more: the client never looks inside.
/// </para>
/// </summary>
public sealed class GitHubSettingsReplicationTests : IDisposable
{
    private const string Work = "j-schepers_innobv";
    private const string SpecManager = "innovadis-dev/spec-manager";

    private static readonly DateTimeOffset Morning = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "github-settings-replication-tests-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // --- Verification 1: a binding travels -------------------------------------

    [Fact]
    public async Task A_binding_set_on_one_device_sends_the_other_devices_calls_out_as_that_account()
    {
        var a = Device("a");
        var b = Device("b");

        Assert.Null(a.Store.SetAccounts([new GitHubAccount(Work) { DisplayName = "Work" }]));
        Assert.Null(a.Store.SetRepositories(Parse(SpecManager)));
        Assert.Null(a.Store.SetRepositoryAccount("spec-manager", Work));

        Assert.Equal(GitHubReplicaOutcome.Taken, await Carry(a, b, GitHubReplicaDocument.Registry));
        Assert.Equal(GitHubReplicaOutcome.Taken, await Carry(a, b, GitHubReplicaDocument.Accounts));

        var choice = b.Store.Current.AccountForPath($"repos/{SpecManager}/issues");
        Assert.Equal(Work, choice.Login);
        Assert.False(choice.IsUnsatisfied);

        var gh = new StubGhCliAccountSource { Tokens = { [Work] = "gho_work" } };
        var credential = await new GitHubCredentialResolver(b.Store, gh)
            .ResolveAsync($"repos/{SpecManager}/issues", Cancellation);

        Assert.NotNull(credential);
        Assert.Equal(Work, credential.Account);
        Assert.Equal("gho_work", credential.Token);
    }

    // --- Verification 2: an account travels without its credential -------------

    [Fact]
    public async Task An_account_travels_with_its_identity_and_without_its_token_or_credential_kind()
    {
        var a = Device("a");
        var b = Device("b");

        Assert.Null(a.Store.SetAccounts(
        [
            new GitHubAccount(Work)
            {
                DisplayName = "Work",
                Host = "github.com",
                ApiEndpoint = "https://api.github.com",
                Credential = GitHubCredentialKind.PersonalAccessToken,
                Token = "ghp_never_leaves_this_machine"
            }
        ]));

        var copy = await a.Replication.ReadAsync(GitHubReplicaDocument.Accounts, Cancellation);
        Assert.NotNull(copy);

        // Neither the token nor the credential kind, under any spelling.
        Assert.DoesNotContain("ghp_never_leaves_this_machine", copy.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("token", copy.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", copy.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PersonalAccessToken", copy.Content, StringComparison.OrdinalIgnoreCase);

        var entry = Assert.Single(JsonNode.Parse(copy.Content)!["accounts"]!.AsArray())!.AsObject();
        Assert.Equal(["login", "displayName", "host", "apiEndpoint"], entry.Select(property => property.Key));
        Assert.Equal(Work, (string?)entry["login"]);
        Assert.Equal("Work", (string?)entry["displayName"]);
        Assert.Equal("github.com", (string?)entry["host"]);
        Assert.Equal("https://api.github.com", (string?)entry["apiEndpoint"]);

        Assert.Equal(GitHubReplicaOutcome.Taken, await b.Replication.ApplyAsync(GitHubReplicaDocument.Accounts, copy, Cancellation));

        var account = Assert.Single(b.Store.Current.Accounts);
        Assert.Equal(Work, account.Login);
        Assert.Equal("Work", account.DisplayName);
        Assert.Equal("github.com", account.Host);
        Assert.Equal("https://api.github.com", account.ApiEndpoint);
        Assert.Equal(GitHubCredentialKind.GhCli, account.Credential);
        Assert.Null(account.Token);
        Assert.DoesNotContain("ghp_never_leaves_this_machine", File.ReadAllText(b.Store.SettingsPath), StringComparison.Ordinal);
    }

    /// <summary>The registry is <c>repos.json</c> itself, and that file never held a
    /// token or a path: the document carries none either.</summary>
    [Fact]
    public async Task The_registry_document_is_the_registry_file_and_holds_no_token_or_clone_directory()
    {
        var a = Device("a");

        Assert.Null(a.Store.SetRepositories(Parse(SpecManager)));
        Assert.Null(a.Store.SetRepositoryToken("spec-manager", "ghp_repository_secret"));
        Assert.Null(a.Store.SetCloneDirectory("spec-manager", Path.Combine(_root, "clone")));

        var copy = await a.Replication.ReadAsync(GitHubReplicaDocument.Registry, Cancellation);

        Assert.NotNull(copy);
        Assert.Equal(File.ReadAllText(a.Store.RegistryPath), copy.Content);
        Assert.DoesNotContain("ghp_repository_secret", copy.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("cloneDirectory", copy.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("token", copy.Content, StringComparison.OrdinalIgnoreCase);
    }

    // --- Verification 3: a local credential survives a pull ----------------------

    [Fact]
    public async Task A_pulled_account_keeps_this_machines_token_and_credential_kind()
    {
        var a = Device("a");
        var b = Device("b");

        Assert.Null(b.Store.SetAccounts([new GitHubAccount(Work)
        {
            DisplayName = "Old name",
            Credential = GitHubCredentialKind.PersonalAccessToken,
            Token = "ghp_b_only"
        }]));

        a.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Null(a.Store.SetAccounts([new GitHubAccount(Work) { DisplayName = "Innovadis", Host = "github.com" }]));

        Assert.Equal(GitHubReplicaOutcome.Taken, await Carry(a, b, GitHubReplicaDocument.Accounts));

        var account = Assert.Single(b.Store.Current.Accounts);
        Assert.Equal("Innovadis", account.DisplayName);
        Assert.Equal("github.com", account.Host);
        Assert.Equal(GitHubCredentialKind.PersonalAccessToken, account.Credential);
        Assert.Equal("ghp_b_only", account.Token);
    }

    // --- Verification 4: a missing credential shows, never falls back ------------

    /// <summary>The registry can arrive before the accounts, so a binding briefly
    /// names a login this machine has no row for. It is unsatisfied — the state the
    /// Settings screen lists — and a call for it is refused with the login named,
    /// never sent as the default account.</summary>
    [Fact]
    public async Task A_binding_that_arrived_before_its_account_is_unsatisfied_and_refused()
    {
        var a = Device("a");
        var b = Device("b");

        Assert.Null(a.Store.SetAccounts([new GitHubAccount(Work)]));
        Assert.Null(a.Store.SetRepositories(Parse(SpecManager)));
        Assert.Null(a.Store.SetRepositoryAccount("spec-manager", Work));

        Assert.Equal(GitHubReplicaOutcome.Taken, await Carry(a, b, GitHubReplicaDocument.Registry));

        var choice = b.Store.Current.AccountForPath($"repos/{SpecManager}/issues");
        Assert.True(choice.IsUnsatisfied);
        Assert.Equal(Work, choice.Login);

        var gh = new StubGhCliAccountSource { Tokens = { ["default-account"] = "gho_default" } };
        var refused = await Assert.ThrowsAsync<GitHubNotConfiguredException>(() =>
            new GitHubCredentialResolver(b.Store, gh).ResolveAsync($"repos/{SpecManager}/issues", Cancellation));
        Assert.Contains(Work, refused.Message, StringComparison.Ordinal);

        // A person still binds only to an account they can see.
        Assert.Equal($"'other-login' is not a configured account.", b.Store.SetRepositoryAccount("spec-manager", "other-login"));
    }

    [Fact]
    public async Task A_pulled_account_the_cli_is_not_signed_in_as_is_refused_with_the_login_named()
    {
        var a = Device("a");
        var b = Device("b");

        Assert.Null(a.Store.SetAccounts([new GitHubAccount(Work)]));
        Assert.Null(a.Store.SetRepositories(Parse(SpecManager)));
        Assert.Null(a.Store.SetRepositoryAccount("spec-manager", Work));
        await Carry(a, b, GitHubReplicaDocument.Registry);
        await Carry(a, b, GitHubReplicaDocument.Accounts);

        var gh = new StubGhCliAccountSource { Tokens = { ["default-account"] = "gho_default" } };
        var refused = await Assert.ThrowsAsync<GitHubNotConfiguredException>(() =>
            new GitHubCredentialResolver(b.Store, gh).ResolveAsync($"repos/{SpecManager}/issues", Cancellation));

        Assert.Contains(Work, refused.Message, StringComparison.Ordinal);
        Assert.Equal([Work], gh.Asked);
    }

    // --- Verification 5: a removal travels ---------------------------------------

    [Fact]
    public async Task A_repository_removed_on_one_device_stays_removed_on_the_other_after_a_restart()
    {
        var a = Device("a");
        var b = Device("b");

        Assert.Null(a.Store.SetRepositories(Parse(SpecManager, "JSdotNet/Backlog")));
        await Carry(a, b, GitHubReplicaDocument.Registry);

        // B knows something of its own about the repository, which is exactly what
        // used to carry a row back into a registry that no longer has it.
        Assert.Null(b.Store.SetCloneDirectory("spec-manager", Path.Combine(_root, "b-clone")));

        a.Clock.Advance(TimeSpan.FromMinutes(5));
        b.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Null(a.Store.RemoveRepository("spec-manager"));
        Assert.Equal(GitHubReplicaOutcome.Taken, await Carry(a, b, GitHubReplicaDocument.Registry));

        Assert.Equal(["backlog"], b.Store.Current.Repositories.Select(r => r.Alias));

        var restarted = Device("b");
        Assert.Equal(["backlog"], restarted.Store.Current.Repositories.Select(r => r.Alias));
        Assert.True(restarted.Store.Current.WasRemoved(SpecManager));
    }

    // --- Verification 6: a rename travels ----------------------------------------

    [Fact]
    public async Task A_repository_renamed_on_one_device_resolves_from_its_old_id_on_the_other()
    {
        var a = Device("a");
        var b = Device("b");

        Assert.Null(a.Store.SetRepositories(Parse("JSdotNet/Old")));
        await Carry(a, b, GitHubReplicaDocument.Registry);
        var clone = Path.Combine(_root, "b-clone");
        Assert.Null(b.Store.SetCloneDirectory("old", clone));

        a.Clock.Advance(TimeSpan.FromMinutes(5));
        b.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Null(a.Store.RenameRepository("old", "JSdotNet/New", out _));
        Assert.Equal(GitHubReplicaOutcome.Taken, await Carry(a, b, GitHubReplicaDocument.Registry));

        var repository = Assert.Single(b.Store.Current.Repositories);
        Assert.Equal("JSdotNet/New", repository.FullName);
        Assert.Same(repository, b.Store.Current.Find("JSdotNet/Old"));

        // The machine half follows the id it became rather than being orphaned.
        Assert.Equal(clone, repository.CloneDirectory);
        Assert.Equal(clone, Device("b").Store.Current.Find("JSdotNet/Old")!.CloneDirectory);
    }

    // --- Verification 7: a new device sends nothing --------------------------------

    [Fact]
    public async Task A_device_that_never_saved_either_document_answers_nothing_to_read()
    {
        var fresh = Device("fresh");

        // Saves that leave both documents empty still write the files, and still
        // leave nothing to send.
        Assert.Null(fresh.Store.SetShowRepositoryColours(true));
        Assert.Null(fresh.Store.SetApiEndpoint("https://api.github.com"));

        Assert.Null(await fresh.Replication.ReadAsync(GitHubReplicaDocument.Registry, Cancellation));
        Assert.Null(await fresh.Replication.ReadAsync(GitHubReplicaDocument.Accounts, Cancellation));
    }

    [Fact]
    public async Task A_new_device_takes_both_documents_from_the_device_that_has_them()
    {
        var a = Device("a");
        var fresh = Device("fresh");

        Assert.Null(a.Store.SetAccounts([new GitHubAccount(Work)]));
        Assert.Null(a.Store.SetRepositories(Parse(SpecManager)));

        Assert.Equal(GitHubReplicaOutcome.Taken, await Carry(a, fresh, GitHubReplicaDocument.Registry));
        Assert.Equal(GitHubReplicaOutcome.Taken, await Carry(a, fresh, GitHubReplicaDocument.Accounts));

        Assert.Equal(SpecManager, Assert.Single(fresh.Store.Current.Repositories).FullName);
        Assert.Equal(Work, Assert.Single(fresh.Store.Current.Accounts).Login);
        Assert.Equal(
            (await a.Replication.ReadAsync(GitHubReplicaDocument.Registry, Cancellation))!.UpdatedAt,
            (await fresh.Replication.ReadAsync(GitHubReplicaDocument.Registry, Cancellation))!.UpdatedAt);
    }

    // --- Verification 8: stamps decide ---------------------------------------------

    [Theory]
    [InlineData(GitHubReplicaDocument.Registry)]
    [InlineData(GitHubReplicaDocument.Accounts)]
    public async Task An_older_copy_is_refused_and_an_equal_one_is_an_echo_and_neither_writes(GitHubReplicaDocument document)
    {
        var a = Device("a");
        var b = Device("b");

        Assert.Null(a.Store.SetAccounts([new GitHubAccount(Work) { DisplayName = "A's" }]));
        Assert.Null(a.Store.SetRepositories(Parse(SpecManager)));
        var older = (await a.Replication.ReadAsync(document, Cancellation))!;

        b.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Null(b.Store.SetAccounts([new GitHubAccount("someone-else")]));
        Assert.Null(b.Store.SetRepositories(Parse("JSdotNet/Backlog")));
        var mine = (await b.Replication.ReadAsync(document, Cancellation))!;
        var files = (File.ReadAllText(b.Store.RegistryPath), File.ReadAllText(b.Store.SettingsPath));

        var changes = 0;
        b.Store.Changed += () => changes++;

        Assert.Equal(GitHubReplicaOutcome.Refused, await b.Replication.ApplyAsync(document, older, Cancellation));
        Assert.Equal(GitHubReplicaOutcome.Echo, await b.Replication.ApplyAsync(document, mine with { Content = older.Content }, Cancellation));

        Assert.Equal(files, (File.ReadAllText(b.Store.RegistryPath), File.ReadAllText(b.Store.SettingsPath)));
        Assert.Equal(mine, await b.Replication.ReadAsync(document, Cancellation));
        Assert.Equal(0, changes);
    }

    // --- Verification 9: an unreadable payload is skipped ----------------------------

    [Theory]
    [InlineData(GitHubReplicaDocument.Accounts, "not json at all")]
    [InlineData(GitHubReplicaDocument.Accounts, "[]")]
    [InlineData(GitHubReplicaDocument.Accounts, """{"accounts":"Work"}""")]
    [InlineData(GitHubReplicaDocument.Accounts, """{"updatedAt":"2026-10-05T09:00:00+00:00"}""")]
    [InlineData(GitHubReplicaDocument.Registry, "not json at all")]
    [InlineData(GitHubReplicaDocument.Registry, """{"repositories":{"id":"a/b"}}""")]
    [InlineData(GitHubReplicaDocument.Registry, """{"renames":[]}""")]
    [InlineData(GitHubReplicaDocument.Registry, "")]
    public async Task A_copy_that_does_not_parse_leaves_the_local_settings_as_they_were(GitHubReplicaDocument document, string content)
    {
        var b = Device("b");
        Assert.Null(b.Store.SetAccounts([new GitHubAccount(Work) { DisplayName = "Kept" }]));
        Assert.Null(b.Store.SetRepositories(Parse(SpecManager)));
        var files = (File.ReadAllText(b.Store.RegistryPath), File.ReadAllText(b.Store.SettingsPath));

        var outcome = await b.Replication.ApplyAsync(
            document, new GitHubReplicaCopyDto(content, Morning.AddDays(1)), Cancellation);

        Assert.Equal(GitHubReplicaOutcome.Unreadable, outcome);
        Assert.Equal(files, (File.ReadAllText(b.Store.RegistryPath), File.ReadAllText(b.Store.SettingsPath)));
        Assert.Equal("Kept", Assert.Single(b.Store.Current.Accounts).DisplayName);
        Assert.Equal(SpecManager, Assert.Single(b.Store.Current.Repositories).FullName);
    }

    /// <summary>A registry this device cannot read is never written over, the rule
    /// every shared write already keeps: the copy is refused rather than taken.</summary>
    [Fact]
    public async Task A_registry_copy_is_refused_while_the_local_registry_cannot_be_read()
    {
        var a = Device("a");
        var b = Device("b");
        Assert.Null(a.Store.SetRepositories(Parse(SpecManager)));

        Directory.CreateDirectory(Path.GetDirectoryName(b.Store.RegistryPath)!);
        File.WriteAllText(b.Store.RegistryPath, "{ this is not json");
        b = Device("b");

        Assert.Equal(GitHubReplicaOutcome.Refused, await Carry(a, b, GitHubReplicaDocument.Registry));
        Assert.Equal("{ this is not json", File.ReadAllText(b.Store.RegistryPath));
        Assert.Null(await b.Replication.ReadAsync(GitHubReplicaDocument.Registry, Cancellation));
    }

    // --- Verification 11: machine data stays ----------------------------------------

    [Fact]
    public async Task A_pulled_registry_leaves_each_repositorys_machine_data_on_this_device()
    {
        var a = Device("a");
        var b = Device("b");

        Assert.Null(a.Store.SetRepositories(Parse(SpecManager)));
        await Carry(a, b, GitHubReplicaDocument.Registry);

        var clone = Path.Combine(_root, "b-clone");
        var folder = Path.Combine(_root, "b-docs");
        Assert.Null(b.Store.SetCloneDirectory("spec-manager", clone));
        Assert.Null(b.Store.SetRepositoryToken("spec-manager", "ghp_b_repository"));
        Assert.Null(b.Store.SetDevbookSource("spec-manager", null, useLocalFolder: true));
        var key = b.Store.Current.Repositories[0].DevbookFolders.First(f => f.SupportsPathOverride).Key;
        Assert.Null(b.Store.SetDevbookFolder("spec-manager", key, enabled: false, folder));

        a.Clock.Advance(TimeSpan.FromMinutes(5));
        b.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Null(a.Store.SetRepositoryColour("spec-manager", 4));
        Assert.Null(a.Store.SetDevbookSource("spec-manager", "docs", useLocalFolder: false));
        Assert.Equal(GitHubReplicaOutcome.Taken, await Carry(a, b, GitHubReplicaDocument.Registry));

        foreach (var store in new[] { b.Store, Device("b").Store })
        {
            var repository = Assert.Single(store.Current.Repositories);

            // The shared half came across...
            Assert.Equal(4, repository.Colour);
            Assert.Equal("docs", repository.DevbookBranch);

            // ...and the machine half stayed exactly where it was.
            Assert.Equal(clone, repository.CloneDirectory);
            Assert.Equal("ghp_b_repository", repository.Token);
            Assert.True(repository.UseLocalDevbookFolder);
            var overridden = repository.DevbookFolders.Single(f => f.Key == key);
            Assert.False(overridden.Enabled);
            Assert.Equal(folder, overridden.Path);
        }
    }

    // --- Accounts: the list is set whole ----------------------------------------------

    [Fact]
    public async Task A_pulled_account_list_adds_unknown_logins_and_removes_absent_ones_leaving_bindings_alone()
    {
        var a = Device("a");
        var b = Device("b");

        Assert.Null(b.Store.SetAccounts(
        [
            new GitHubAccount("gone-login") { Credential = GitHubCredentialKind.PersonalAccessToken, Token = "ghp_gone" },
        ]));
        Assert.Null(b.Store.SetRepositories(Parse(SpecManager)));
        Assert.Null(b.Store.SetRepositoryAccount("spec-manager", "gone-login"));

        a.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Null(a.Store.SetAccounts([new GitHubAccount(Work) { DisplayName = "Work" }]));

        Assert.Equal(GitHubReplicaOutcome.Taken, await Carry(a, b, GitHubReplicaDocument.Accounts));

        var account = Assert.Single(b.Store.Current.Accounts);
        Assert.Equal(Work, account.Login);
        Assert.Equal(GitHubCredentialKind.GhCli, account.Credential);

        // Removed with its local credential, as a removal on this machine does...
        Assert.DoesNotContain("ghp_gone", File.ReadAllText(b.Store.SettingsPath), StringComparison.Ordinal);

        // ...and the binding that names it stays, unsatisfied.
        Assert.Equal("gone-login", b.Store.Current.Repositories[0].Account);
        Assert.True(b.Store.Current.AccountForPath($"repos/{SpecManager}").IsUnsatisfied);
    }

    // --- When a document is stamped, and who hears about it ----------------------------

    [Fact]
    public async Task A_shared_change_stamps_its_document_and_raises_changed()
    {
        var a = Device("a");
        var raised = 0;
        a.Replication.Changed += () => raised++;

        Assert.Null(a.Store.SetRepositories(Parse(SpecManager)));
        Assert.Equal(1, raised);
        Assert.Equal(Morning, (await a.Replication.ReadAsync(GitHubReplicaDocument.Registry, Cancellation))!.UpdatedAt);

        a.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(a.Store.SetRepositoryColour("spec-manager", 2));
        Assert.Equal(2, raised);
        Assert.Equal(Morning.AddMinutes(1), (await a.Replication.ReadAsync(GitHubReplicaDocument.Registry, Cancellation))!.UpdatedAt);

        a.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(a.Store.SetAccounts([new GitHubAccount(Work)]));
        Assert.Equal(3, raised);
        Assert.Equal(Morning.AddMinutes(2), (await a.Replication.ReadAsync(GitHubReplicaDocument.Accounts, Cancellation))!.UpdatedAt);

        // The registry did not change with the accounts, so its stamp did not move.
        Assert.Equal(Morning.AddMinutes(1), (await a.Replication.ReadAsync(GitHubReplicaDocument.Registry, Cancellation))!.UpdatedAt);
    }

    /// <summary>A pasted token, a credential choice, a clone directory and the
    /// install-wide switches are this machine's alone: neither document changes, so
    /// neither stamp moves and nothing is announced.</summary>
    [Fact]
    public async Task A_machine_only_change_moves_no_stamp_and_raises_nothing()
    {
        var a = Device("a");
        Assert.Null(a.Store.SetAccounts([new GitHubAccount(Work)]));
        Assert.Null(a.Store.SetRepositories(Parse(SpecManager)));
        var registry = await a.Replication.ReadAsync(GitHubReplicaDocument.Registry, Cancellation);
        var accounts = await a.Replication.ReadAsync(GitHubReplicaDocument.Accounts, Cancellation);

        var raised = 0;
        a.Replication.Changed += () => raised++;
        a.Clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Null(a.Store.SetAccountCredential(Work, GitHubCredentialKind.PersonalAccessToken, "ghp_pasted"));
        Assert.Null(a.Store.SetAccountCredential(Work, GitHubCredentialKind.PersonalAccessToken, "ghp_pasted_again"));
        Assert.Null(a.Store.SetRepositoryToken("spec-manager", "ghp_repository"));
        Assert.Null(a.Store.SetCloneDirectory("spec-manager", Path.Combine(_root, "clone")));
        Assert.Null(a.Store.SetShowRepositoryColours(true));
        Assert.Null(a.Store.SetApiEndpoint("https://ghe.example/api/v3"));

        Assert.Equal(0, raised);
        Assert.Equal(registry, await a.Replication.ReadAsync(GitHubReplicaDocument.Registry, Cancellation));
        Assert.Equal(accounts, await a.Replication.ReadAsync(GitHubReplicaDocument.Accounts, Cancellation));
    }

    /// <summary>A copy taken from another device is not a local change: the store
    /// announces it so open screens reload, and the port stays silent so the sync
    /// loop does not push it straight back.</summary>
    [Theory]
    [InlineData(GitHubReplicaDocument.Registry)]
    [InlineData(GitHubReplicaDocument.Accounts)]
    public async Task A_taken_copy_reloads_the_settings_without_announcing_a_local_change(GitHubReplicaDocument document)
    {
        var a = Device("a");
        var b = Device("b");
        Assert.Null(a.Store.SetAccounts([new GitHubAccount(Work)]));
        Assert.Null(a.Store.SetRepositories(Parse(SpecManager)));

        var reloaded = 0;
        var announced = 0;
        b.Store.Changed += () => reloaded++;
        b.Replication.Changed += () => announced++;

        Assert.Equal(GitHubReplicaOutcome.Taken, await Carry(a, b, document));

        Assert.True(reloaded > 0);
        Assert.Equal(0, announced);
        Assert.Equal(
            (await a.Replication.ReadAsync(document, Cancellation))!.UpdatedAt,
            (await b.Replication.ReadAsync(document, Cancellation))!.UpdatedAt);
    }

    /// <summary>A copy from a PC whose clock runs ahead carries a stamp this clock has
    /// not reached. The next local change is stamped past it rather than at "now",
    /// or it would read as older than the copy it was made on (ADR 0018's rule).</summary>
    [Fact]
    public async Task A_change_made_after_taking_a_copy_from_the_future_is_stamped_past_it()
    {
        var a = Device("a");
        var b = Device("b");
        a.Clock.Advance(TimeSpan.FromHours(3));
        Assert.Null(a.Store.SetAccounts([new GitHubAccount(Work)]));
        Assert.Null(a.Store.SetRepositories(Parse(SpecManager)));
        await Carry(a, b, GitHubReplicaDocument.Registry);
        await Carry(a, b, GitHubReplicaDocument.Accounts);

        Assert.Null(b.Store.SetRepositoryColour("spec-manager", 1));
        Assert.Null(b.Store.SetAccounts([new GitHubAccount(Work), new GitHubAccount("second")]));

        Assert.Equal(Morning.AddHours(3).AddTicks(1), (await b.Replication.ReadAsync(GitHubReplicaDocument.Registry, Cancellation))!.UpdatedAt);
        Assert.Equal(Morning.AddHours(3).AddTicks(1), (await b.Replication.ReadAsync(GitHubReplicaDocument.Accounts, Cancellation))!.UpdatedAt);
    }

    // --- Files written before the stamps existed -----------------------------------------

    [Fact]
    public async Task Files_written_before_the_stamps_are_stamped_once_from_their_last_write_time()
    {
        var device = Path.Combine(_root, "legacy");
        var registryPath = Path.Combine(device, "workspace", "config", "repos.json");
        var localPath = Path.Combine(device, "local", "github.json");
        Directory.CreateDirectory(Path.GetDirectoryName(registryPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        File.WriteAllText(registryPath, """{ "repositories": [ { "id": "innovadis-dev/spec-manager", "alias": "spec-manager" } ] }""");
        File.WriteAllText(localPath, """{ "repositories": [], "accounts": [ { "login": "j-schepers_innobv", "credential": "GhCli" } ] }""");
        var registryWritten = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var localWritten = new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(registryPath, registryWritten);
        File.SetLastWriteTimeUtc(localPath, localWritten);

        var legacy = Device("legacy");

        Assert.Equal(
            new DateTimeOffset(registryWritten),
            (await legacy.Replication.ReadAsync(GitHubReplicaDocument.Registry, Cancellation))!.UpdatedAt);
        Assert.Equal(
            new DateTimeOffset(localWritten),
            (await legacy.Replication.ReadAsync(GitHubReplicaDocument.Accounts, Cancellation))!.UpdatedAt);

        // Once: a save that changes neither document writes the stamps it read, so
        // the rewrite does not move them.
        Assert.Null(legacy.Store.SetShowRepositoryColours(true));

        Assert.Equal(
            new DateTimeOffset(registryWritten),
            (await legacy.Replication.ReadAsync(GitHubReplicaDocument.Registry, Cancellation))!.UpdatedAt);
        Assert.Equal(
            new DateTimeOffset(localWritten),
            (await legacy.Replication.ReadAsync(GitHubReplicaDocument.Accounts, Cancellation))!.UpdatedAt);
        Assert.Contains("\"updatedAt\"", File.ReadAllText(registryPath), StringComparison.Ordinal);
        Assert.Contains("\"accountsUpdatedAt\"", File.ReadAllText(localPath), StringComparison.Ordinal);
    }

    private sealed record DeviceUnderTest(GitHubSettingsStore Store, IGitHubSettingsReplication Replication, FakeTimeProvider Clock);

    /// <summary>One device: its own per-user file and its own workspace root. Asking
    /// for the same name again is the same device after a restart.</summary>
    private DeviceUnderTest Device(string name)
    {
        var clock = new FakeTimeProvider(Morning);
        var store = new GitHubSettingsStore(
            Path.Combine(_root, name, "local", "github.json"),
            () => Path.Combine(_root, name, "workspace"),
            clock);

        return new DeviceUnderTest(store, new GitHubSettingsReplication(store), clock);
    }

    private static async Task<GitHubReplicaOutcome> Carry(DeviceUnderTest from, DeviceUnderTest to, GitHubReplicaDocument document)
    {
        var copy = await from.Replication.ReadAsync(document, Cancellation);
        Assert.NotNull(copy);

        // Through the wire's own shape, so a stamp that would not survive the trip
        // is caught here rather than on two real PCs.
        var travelled = JsonSerializer.Deserialize<GitHubReplicaCopyDto>(JsonSerializer.Serialize(copy))!;
        return await to.Replication.ApplyAsync(document, travelled, Cancellation);
    }

    private static List<GitHubRepositoryRef> Parse(params string[] lines)
    {
        var (repositories, errors) = GitHubSettings.ParseText(string.Join('\n', lines));
        Assert.Empty(errors);
        return repositories;
    }
}
