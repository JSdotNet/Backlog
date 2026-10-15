using System.Runtime.Versioning;

using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Sync.Abstractions.Services;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// How the per-user <c>github.json</c> keeps the two secrets it holds — an
/// account's pasted token and a repository's token — at rest (local ADR 0030).
/// <para>
/// Asserted against the file's text rather than through <c>Current</c> alone, in
/// the idiom <see cref="RepositoryRegistrySplitTests"/> established: a store that
/// read the token back perfectly while leaving it in the clear on disk would pass
/// every test that only read it back.
/// </para>
/// <para>
/// Every test guards on <see cref="OperatingSystem.IsWindows"/>, as the device
/// credential's DPAPI tests do: the envelope is the Windows half of the decision
/// and the suite runs on Windows.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GitHubCredentialsAtRestTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "github-credentials-at-rest-tests-" + Guid.NewGuid().ToString("N"));

    private string WorkspaceRoot => Path.Combine(_root, "workspace");

    private string LocalPath => Path.Combine(_root, "install-1", "github.json");

    private GitHubSettingsStore Store(TimeProvider? clock = null) => new(LocalPath, () => WorkspaceRoot, clock);

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void An_account_token_is_not_in_the_file_and_a_restart_reads_it_back()
    {
        if (!OperatingSystem.IsWindows()) return;

        var store = Store();
        Assert.Null(store.SetAccounts([new GitHubAccount("JSdotNet")]));
        Assert.Null(store.SetAccountCredential("JSdotNet", GitHubCredentialKind.PersonalAccessToken, "ghp_account_at_rest"));

        var local = File.ReadAllText(store.SettingsPath);
        Assert.DoesNotContain("ghp_account_at_rest", local, StringComparison.Ordinal);
        Assert.Contains("\"token\": \"dpapi:", local, StringComparison.Ordinal);

        // The rest of the row stays legible, which is why the envelope is on the
        // value rather than on the file.
        Assert.Contains("\"login\": \"JSdotNet\"", local, StringComparison.Ordinal);

        Assert.Equal("ghp_account_at_rest", Assert.Single(Store().Current.Accounts).Token);
    }

    [Fact]
    public void A_repository_token_is_not_in_the_file_and_a_restart_reads_it_back()
    {
        if (!OperatingSystem.IsWindows()) return;

        var clone = Path.Combine(_root, "clone");

        var store = Store();
        Assert.Null(store.SetRepositories([new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog")]));
        Assert.Null(store.SetRepositoryToken("backlog", "ghp_repository_at_rest"));
        Assert.Null(store.SetCloneDirectory("backlog", clone));

        var local = File.ReadAllText(store.SettingsPath);
        Assert.DoesNotContain("ghp_repository_at_rest", local, StringComparison.Ordinal);
        Assert.Contains("\"token\": \"dpapi:", local, StringComparison.Ordinal);
        Assert.Contains("\"id\": \"JSdotNet/Backlog\"", local, StringComparison.Ordinal);
        Assert.Contains("cloneDirectory", local, StringComparison.Ordinal);

        var reopened = Store().Current.Find("backlog")!;
        Assert.Equal("ghp_repository_at_rest", reopened.Token);
        Assert.Equal(clone, reopened.CloneDirectory);
    }

    /// <summary>
    /// A file the release before the envelope wrote holds both tokens in the clear.
    /// It opens with both, and the plaintext is gone from the disk once it has —
    /// waiting for the next save would leave it there for as long as nobody touches
    /// Settings.
    /// </summary>
    [Fact]
    public void Plaintext_tokens_from_an_earlier_release_are_read_and_sealed_on_first_read()
    {
        if (!OperatingSystem.IsWindows()) return;

        WriteRegistryFile("""
            { "repositories": [ { "id": "JSdotNet/Backlog", "alias": "backlog" } ] }
            """);
        WriteLocalFile("""
            {
              "repositories": [ { "id": "JSdotNet/Backlog", "cloneDirectory": "/tmp/clone", "token": "ghp_legacy_repository" } ],
              "accounts": [ { "login": "JSdotNet", "credential": "PersonalAccessToken", "token": "ghp_legacy_account" } ],
              "apiEndpoint": "https://github.example/api/v3"
            }
            """);

        var store = Store();

        Assert.Equal("ghp_legacy_repository", store.Current.Find("backlog")!.Token);
        Assert.Equal("ghp_legacy_account", Assert.Single(store.Current.Accounts).Token);

        var local = File.ReadAllText(LocalPath);
        Assert.DoesNotContain("ghp_legacy_repository", local, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_legacy_account", local, StringComparison.Ordinal);
        Assert.Contains("\"token\": \"dpapi:", local, StringComparison.Ordinal);
        Assert.Contains("https://github.example/api/v3", local, StringComparison.Ordinal);

        // And the sealed file reads back to the same answer.
        var reopened = Store();
        Assert.Equal("ghp_legacy_repository", reopened.Current.Find("backlog")!.Token);
        Assert.Equal("ghp_legacy_account", Assert.Single(reopened.Current.Accounts).Token);
    }

    /// <summary>The oldest shape of all: one global token, which every repository
    /// inherits. It still reaches the repositories, and it is not left in the
    /// clear.</summary>
    [Fact]
    public void The_legacy_global_token_still_reaches_the_repositories_and_is_not_left_in_the_clear()
    {
        if (!OperatingSystem.IsWindows()) return;

        WriteLocalFile("""
            {
              "repositories": [ { "alias": "backlog", "owner": "JSdotNet", "name": "Backlog" } ],
              "token": "ghp_legacy_global",
              "apiEndpoint": "https://api.github.com"
            }
            """);

        var store = Store();

        Assert.Equal("ghp_legacy_global", store.Current.Find("backlog")!.Token);
        Assert.DoesNotContain("ghp_legacy_global", File.ReadAllText(LocalPath), StringComparison.Ordinal);
        Assert.Equal("ghp_legacy_global", Store().Current.Find("backlog")!.Token);
    }

    /// <summary>
    /// An envelope this Windows user cannot open — corrupt, or sealed under another
    /// profile or on another machine — reads as no token, and nothing else in the
    /// file is lost with it. Re-entering the token in Settings is the recovery.
    /// </summary>
    [Theory]
    [InlineData("dpapi:dGhpcyBpcyBiYXNlNjQgYnV0IG5vdCBhIERQQVBJIGVudmVsb3Bl")]
    [InlineData("dpapi:this is not base64 at all !!")]
    [InlineData("dpapi:")]
    public void A_token_this_user_cannot_open_reads_as_no_token_and_the_rest_still_loads(string stored)
    {
        if (!OperatingSystem.IsWindows()) return;

        WriteRegistryFile("""
            { "repositories": [ { "id": "JSdotNet/Backlog", "alias": "backlog" } ] }
            """);
        WriteLocalFile($$"""
            {
              "repositories": [ { "id": "JSdotNet/Backlog", "cloneDirectory": "/tmp/clone", "token": "{{stored}}" } ],
              "accounts": [ { "login": "JSdotNet", "credential": "PersonalAccessToken", "token": "{{stored}}" } ],
              "apiEndpoint": "https://github.example/api/v3"
            }
            """);

        var store = Store();

        var repository = store.Current.Find("backlog")!;
        Assert.Null(repository.Token);
        Assert.Equal("/tmp/clone", repository.CloneDirectory);

        var account = Assert.Single(store.Current.Accounts);
        Assert.Equal("JSdotNet", account.Login);
        Assert.Null(account.Token);
        Assert.Equal("https://github.example/api/v3", store.Current.ApiEndpoint);
    }

    /// <summary>
    /// The envelope is the local file's business only. Neither shared document
    /// carries a token in any form, and sealing a plaintext token on first read is
    /// not an identity change, so the accounts document keeps the stamp it had —
    /// otherwise every device would re-send its accounts on upgrade.
    /// </summary>
    [Fact]
    public void Sealing_a_token_never_reaches_the_shared_documents_and_does_not_restamp_the_accounts()
    {
        if (!OperatingSystem.IsWindows()) return;

        var stamp = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));

        WriteRegistryFile("""
            { "updatedAt": "2026-10-01T08:00:00+00:00", "repositories": [ { "id": "JSdotNet/Backlog", "alias": "backlog" } ] }
            """);
        WriteLocalFile("""
            {
              "repositories": [ { "id": "JSdotNet/Backlog", "token": "ghp_legacy_repository" } ],
              "accounts": [ { "login": "JSdotNet", "credential": "PersonalAccessToken", "token": "ghp_legacy_account" } ],
              "accountsUpdatedAt": "2026-10-01T08:00:00+00:00",
              "apiEndpoint": "https://api.github.com"
            }
            """);

        var store = Store(clock);

        var accounts = store.ReadReplica(GitHubReplicaDocument.Accounts)!;
        Assert.Equal(stamp, accounts.UpdatedAt);
        Assert.DoesNotContain("ghp_legacy_account", accounts.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("dpapi:", accounts.Content, StringComparison.Ordinal);

        var registry = File.ReadAllText(store.RegistryPath);
        Assert.DoesNotContain("ghp_legacy", registry, StringComparison.Ordinal);
        Assert.DoesNotContain("dpapi:", registry, StringComparison.Ordinal);
        Assert.DoesNotContain("token", registry, StringComparison.OrdinalIgnoreCase);

        var registryCopy = store.ReadReplica(GitHubReplicaDocument.Registry)!;
        Assert.DoesNotContain("dpapi:", registryCopy.Content, StringComparison.Ordinal);

        // A token pasted later is this machine's alone too.
        Assert.Null(store.SetAccountCredential("JSdotNet", GitHubCredentialKind.PersonalAccessToken, "ghp_pasted_later"));
        Assert.Equal(stamp, store.ReadReplica(GitHubReplicaDocument.Accounts)!.UpdatedAt);
    }

    private void WriteLocalFile(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LocalPath)!);
        File.WriteAllText(LocalPath, json);
    }

    private void WriteRegistryFile(string json)
    {
        var path = Path.Combine(WorkspaceRoot, "config", "repos.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }
}
