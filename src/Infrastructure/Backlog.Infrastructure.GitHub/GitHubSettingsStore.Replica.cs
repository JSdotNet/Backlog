using System.Text.Json;
using System.Text.Json.Nodes;

using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.Services;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// The replica half of the store: the repository registry and the account
/// identities as two whole documents, each last-write-wins on its own stamp (local
/// ADR 0020, after ADR 0018).
/// <para>
/// <b>The registry document</b> is <c>repos.json</c> itself — rows, rename record,
/// removal record and its <c>updatedAt</c>. It never held a token or a path, so it
/// may travel as it is. <b>The accounts document</b> is built from the account list:
/// one entry per login with its display name, host and endpoint, and nothing else.
/// The credential kind and the token are this machine's and never enter it.
/// </para>
/// <para>
/// <b>When a stamp moves.</b> Every save rewrites both files, so a stamp cannot move
/// on every write: a pasted token would then announce a new accounts document. Each
/// write compares the shared content it is about to write with the content the stamp
/// was given for — the <em>signature</em> — and restamps only when they differ. A
/// copy taken from another device sets both the stamp and the signature to the
/// copy's, so the write that follows is not a change and nothing is pushed back.
/// </para>
/// </summary>
public sealed partial class GitHubSettingsStore
{
    private static readonly JsonSerializerOptions SignatureOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>The registry document's stamp, or null for a registry nobody has put
    /// anything in — which is no document, and nothing to send.</summary>
    private DateTimeOffset? _registryStamp;

    /// <summary>The registry's shared content as of <see cref="_registryStamp"/>,
    /// serialized without the stamp.</summary>
    private string _registrySignature = string.Empty;

    /// <summary>The accounts document's stamp, or null while no account was ever
    /// saved here.</summary>
    private DateTimeOffset? _accountsStamp;

    /// <summary>The account identities as of <see cref="_accountsStamp"/>.</summary>
    private string _accountsSignature = string.Empty;

    /// <summary>
    /// Raised after a local write gave the registry or the accounts a new version —
    /// what the replication port forwards as its own <c>Changed</c>. Never for a copy
    /// taken from another device, and never for a write that changed neither
    /// document. <see cref="Changed"/> is the notice every screen reloads on; this
    /// one is for the sync loop alone.
    /// </summary>
    internal event Action? SharedDocumentChanged;

    /// <summary>The document as this device holds it, or null when it was never
    /// saved here or cannot be read.</summary>
    internal GitHubReplicaCopyDto? ReadReplica(GitHubReplicaDocument document) => document switch
    {
        GitHubReplicaDocument.Registry => LocalRegistryCopy().Copy,
        GitHubReplicaDocument.Accounts => _accountsStamp is { } stamp
            ? new GitHubReplicaCopyDto(AccountsDocument(Current.Accounts, stamp), stamp)
            : null,
        _ => null,
    };

    /// <summary>
    /// Offers a copy from another device. Unreadable before anything else, so a
    /// copy that does not parse is never weighed against the local one; then echo on
    /// an equal stamp and refused on an older one, neither writing anything; and a
    /// later one taken, at its own stamp.
    /// </summary>
    internal GitHubReplicaOutcome ApplyReplica(GitHubReplicaDocument document, GitHubReplicaCopyDto inbound)
    {
        ArgumentNullException.ThrowIfNull(inbound);

        return document switch
        {
            GitHubReplicaDocument.Registry => ApplyRegistry(inbound),
            GitHubReplicaDocument.Accounts => ApplyAccounts(inbound),
            _ => GitHubReplicaOutcome.Unreadable,
        };
    }

    // --- The registry -------------------------------------------------------------

    private GitHubReplicaOutcome ApplyRegistry(GitHubReplicaCopyDto inbound)
    {
        if (ParseRegistry(inbound.Content) is not { } document) return GitHubReplicaOutcome.Unreadable;

        var local = LocalRegistryCopy();

        // A registry this device cannot read is never written over, the rule every
        // shared write here keeps; it is not weighed either, because it has no stamp
        // anybody could read.
        if (local.Unreadable) return GitHubReplicaOutcome.Refused;

        if (local.Copy is { } held)
        {
            if (inbound.UpdatedAt == held.UpdatedAt) return GitHubReplicaOutcome.Echo;
            if (inbound.UpdatedAt < held.UpdatedAt) return GitHubReplicaOutcome.Refused;
        }

        // The one key this store owns the value of. Every other key is written as it
        // arrived, so one a newer build added survives until this build next saves.
        document["updatedAt"] = JsonValue.Create(inbound.UpdatedAt);

        try
        {
            var path = RegistryPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, document.ToJsonString(JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // Nothing landed, so nothing was taken; the replica offers it again.
            return GitHubReplicaOutcome.Refused;
        }

        // Re-composed from the file just written, so each repository's machine half
        // in github.json joins its row again by id — the stamp and the baseline come
        // from the file too, so this is not announced as a local change.
        Current = Load();
        Changed?.Invoke();

        return GitHubReplicaOutcome.Taken;
    }

    /// <summary>The registry file as a copy, or null when there is no document; and
    /// whether the file is there and cannot be read.</summary>
    private (GitHubReplicaCopyDto? Copy, bool Unreadable) LocalRegistryCopy()
    {
        try
        {
            var path = RegistryPath;
            if (!File.Exists(path)) return (null, false);

            var content = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<RegistryDto>(content, JsonOptions);
            if (dto is null) return (null, false);

            // A registry with nothing in it and no stamp is the empty file the first
            // save writes on a fresh workspace: no document, so a newly paired device
            // never sends an empty list over another device's.
            var stamp = dto.UpdatedAt ?? (IsEmpty(dto) ? null : LastWriteOf(path));
            return stamp is { } at ? (new GitHubReplicaCopyDto(content, at), false) : (null, false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return (null, true);
        }
    }

    /// <summary>The inbound text as a registry, or null when it is not one: a JSON
    /// object with a <c>repositories</c> array that reads as the registry's
    /// shape.</summary>
    private static JsonObject? ParseRegistry(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        try
        {
            if (JsonNode.Parse(content) is not JsonObject document) return null;
            if (!document.TryGetPropertyValue("repositories", out var rows) || rows is not JsonArray) return null;
            if (JsonSerializer.Deserialize<RegistryDto>(content, JsonOptions) is null) return null;

            return document;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Restamps the registry when <paramref name="dto"/> differs from the
    /// content the stamp was given for, and writes the stamp onto it either way.
    /// True when it restamped.</summary>
    private bool StampRegistry(RegistryDto dto)
    {
        var signature = RegistrySignature(dto);
        var restamped = !string.Equals(signature, _registrySignature, StringComparison.Ordinal);

        if (restamped)
        {
            _registryStamp = NextStamp(_registryStamp);
            _registrySignature = signature;
        }

        dto.UpdatedAt = _registryStamp;
        return restamped;
    }

    private string RegistrySignature(List<RegistryRepositoryDto> rows) => RegistrySignature(RegistryDtoFor(rows));

    private static string RegistrySignature(RegistryDto dto)
    {
        var stamp = dto.UpdatedAt;
        dto.UpdatedAt = null;

        try
        {
            return JsonSerializer.Serialize(dto, SignatureOptions);
        }
        finally
        {
            dto.UpdatedAt = stamp;
        }
    }

    private static bool IsEmpty(RegistryDto dto) =>
        dto.Repositories.Count == 0
        && (dto.Renames is null || dto.Renames.Count == 0)
        && (dto.Removals is null || dto.Removals.Count == 0);

    // --- The accounts -------------------------------------------------------------

    private GitHubReplicaOutcome ApplyAccounts(GitHubReplicaCopyDto inbound)
    {
        if (ParseAccounts(inbound.Content) is not { } entries) return GitHubReplicaOutcome.Unreadable;

        if (_accountsStamp is { } held)
        {
            if (inbound.UpdatedAt == held) return GitHubReplicaOutcome.Echo;
            if (inbound.UpdatedAt < held) return GitHubReplicaOutcome.Refused;
        }

        var next = new List<GitHubAccount>();

        foreach (var entry in entries)
        {
            if (GitHubAccount.NormalizeLogin(entry.Login) is not { } login) continue;

            // A login this machine holds keeps how this machine satisfies it; one it
            // does not is asked of gh, which needs no secret written anywhere. A login
            // the list no longer names is simply not carried, and its token goes with
            // it, as removing it here would.
            var identity = Current.Account(login) is { } known
                ? known with { Login = login }
                : new GitHubAccount(login) { Credential = GitHubCredentialKind.GhCli };

            next.Add(identity with
            {
                DisplayName = entry.DisplayName,
                Host = entry.Host,
                ApiEndpoint = entry.ApiEndpoint
            });
        }

        var accounts = NormalizeAccounts(next);

        // The copy's version, set before the write so the write is not a change.
        _accountsStamp = inbound.UpdatedAt;
        _accountsSignature = AccountsSignature(accounts);

        // Through the one door every account write takes. The repositories are
        // unchanged, so the registry is rewritten as it was and keeps its stamp; a
        // binding that names a login just dropped stays, and reads as unsatisfied.
        _ = Save(new GitHubSettings
        {
            Repositories = [.. Current.Repositories],
            ApiEndpoint = Current.ApiEndpoint,
            ShowRepositoryColours = Current.ShowRepositoryColours,
            Accounts = accounts
        });

        return GitHubReplicaOutcome.Taken;
    }

    /// <summary>The inbound text as an account list, or null when it is not one: a
    /// JSON object with an <c>accounts</c> array of objects.</summary>
    private static List<AccountIdentityDto>? ParseAccounts(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        try
        {
            if (JsonNode.Parse(content) is not JsonObject document) return null;
            if (!document.TryGetPropertyValue("accounts", out var list) || list is not JsonArray entries) return null;
            if (entries.Any(entry => entry is not JsonObject)) return null;

            return JsonSerializer.Deserialize<AccountsDocumentDto>(content, JsonOptions)?.Accounts;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The accounts document: the stamp and each account's identity, in the
    /// order this machine lists them. Built field by field from a type that has no
    /// credential and no token, so neither can ever be serialized into it.</summary>
    private static string AccountsDocument(IEnumerable<GitHubAccount> accounts, DateTimeOffset stamp) =>
        JsonSerializer.Serialize(new AccountsDocumentDto { UpdatedAt = stamp, Accounts = IdentitiesOf(accounts) }, JsonOptions);

    private static List<AccountIdentityDto> IdentitiesOf(IEnumerable<GitHubAccount> accounts) =>
    [
        .. accounts.Select(account => new AccountIdentityDto
        {
            Login = account.Login,
            DisplayName = account.DisplayName,
            Host = account.Host,
            ApiEndpoint = account.ApiEndpoint
        })
    ];

    /// <summary>Restamps the accounts when their identities differ from the ones the
    /// stamp was given for. True when it restamped.</summary>
    private bool StampAccounts(IEnumerable<GitHubAccount> accounts)
    {
        var signature = AccountsSignature(accounts);
        if (string.Equals(signature, _accountsSignature, StringComparison.Ordinal)) return false;

        _accountsStamp = NextStamp(_accountsStamp);
        _accountsSignature = signature;
        return true;
    }

    private static string AccountsSignature(IEnumerable<GitHubAccount> accounts) =>
        JsonSerializer.Serialize(IdentitiesOf(accounts), SignatureOptions);

    // --- Stamps -------------------------------------------------------------------

    /// <summary>
    /// The stamp for a change: now, or one tick past the stamp held when that is not
    /// already later — ADR 0018's rule. A copy taken from a PC whose clock runs ahead
    /// carries a stamp this clock has not reached, and a change stamped with plain
    /// "now" would read as older than the copy it was made on.
    /// </summary>
    private DateTimeOffset NextStamp(DateTimeOffset? held)
    {
        var now = _clock.GetUtcNow();
        return held is { } previous && now <= previous ? previous.AddTicks(1) : now;
    }

    private static DateTimeOffset? LastWriteOf(string path)
    {
        try
        {
            return File.Exists(path)
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The accounts document on the wire. Four fields per entry, written
    /// even when null so every device reads the same shape.</summary>
    private sealed class AccountsDocumentDto
    {
        public DateTimeOffset? UpdatedAt { get; set; }

        public List<AccountIdentityDto> Accounts { get; set; } = [];
    }

    /// <summary>One account as it travels: who it is, never how this machine signs
    /// in as it.</summary>
    private sealed class AccountIdentityDto
    {
        public string? Login { get; set; }
        public string? DisplayName { get; set; }
        public string? Host { get; set; }
        public string? ApiEndpoint { get; set; }
    }
}
