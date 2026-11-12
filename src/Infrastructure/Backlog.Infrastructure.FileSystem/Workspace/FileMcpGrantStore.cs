using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using Backlog.SharedKernel.Mcp;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The MCP grants (local ADR 0052 §3), in <c>mcp-grants.json</c> in the installation's
/// app-data folder: legible JSON, one record per grant, with only each grant's secret sealed
/// under the Windows user (local ADR 0030 §2).
/// <para>
/// <b>Read from the file on every call.</b> The listener checks every grant token against the
/// stored version, and a person who bumps a version in the file by hand — beside the Agents
/// card on the MCP server settings page — expects the next request to be refused. The
/// file is a few hundred bytes, so a cache would buy nothing that matters and cost that.
/// </para>
/// <para>
/// <b>The secret.</b> Each grant gets 32 random bytes, base64url, sealed. No token carries it
/// and nothing reads it yet besides <see cref="OpenSecret"/>: it is for the later registration
/// and rotation steps. A record that has no secret, or one in the clear, gets it sealed on the
/// first read (ADR 0030 §3), which is what lets a person hand-write a grant. An envelope this
/// user cannot open reads as no secret and is left as it is (§4): replacing it would be a
/// rotation, and a rotation raises the version.
/// </para>
/// <para>
/// <b>A file that does not parse is never written over.</b> A hand edit can leave one — a
/// trailing comma, a version in quotes — and every grant in it would go with a write. It reads
/// as no grants, so every token for it is refused, and a create or revoke throws
/// <see cref="UnreadableMcpGrantFileException"/> until the person mends it. A record with a
/// blank id, and any member this release does not know, are written back as they were read.
/// </para>
/// <para>
/// Writes go to a temporary file beside the real one and replace it, under one lock, so a
/// reader never sees half a file.
/// </para>
/// </summary>
public sealed class FileMcpGrantStore : IMcpGrantStore
{
    /// <summary>The file's name in the app-data folder.</summary>
    public const string FileName = "mcp-grants.json";

    /// <summary>The characters a grant id is made of after its prefix: RFC 4648 base32,
    /// lower case, so an id reads unambiguously when a person copies it.</summary>
    private const string IdAlphabet = "abcdefghijklmnopqrstuvwxyz234567";

    private const int IdLength = 16;

    private const int SecretBytes = 32;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();

    public FileMcpGrantStore(string path, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public IReadOnlyList<McpGrant> List()
    {
        lock (_gate)
        {
            return [.. Load().Grants.Select(ToGrant)];
        }
    }

    /// <inheritdoc />
    public McpGrant? Find(string id)
    {
        lock (_gate)
        {
            return FindRecord(Load(), id) is { } record ? ToGrant(record) : null;
        }
    }

    /// <inheritdoc />
    /// <exception cref="UnreadableMcpGrantFileException">The file exists and does not parse.</exception>
    public McpGrant Create(string name, IReadOnlyList<string> scopes, string createdBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);

        // Only a scope a grant may hold (local ADR 0052 §4): devbook:decide, a misspelling or a
        // name no policy knows is refused rather than dropped, so the caller cannot believe it
        // was granted.
        if (scopes.FirstOrDefault(scope => !McpScopes.IsGrantable(scope)) is { } refused)
        {
            throw new ArgumentException($"No grant may hold '{refused}'.", nameof(scopes));
        }

        McpGrant grant;

        lock (_gate)
        {
            var file = LoadForWrite();
            var record = new GrantRecord
            {
                Id = NewId(),
                Name = name,
                Scopes = [.. scopes],
                Version = 1,
                CreatedBy = createdBy,
                CreatedAt = _clock.GetUtcNow(),
                Revoked = false,
                Secret = SecretEnvelope.Seal(NewSecret())
            };

            file.Records.Add(record);
            Save(file);
            grant = ToGrant(record);
        }

        Changed?.Invoke();
        return grant;
    }

    /// <inheritdoc />
    /// <exception cref="UnreadableMcpGrantFileException">The file exists and does not parse.</exception>
    public McpGrant? SetScopes(string id, IReadOnlyList<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        if (scopes.FirstOrDefault(scope => !McpScopes.IsGrantable(scope)) is { } refused)
        {
            throw new ArgumentException($"No grant may hold '{refused}'.", nameof(scopes));
        }

        McpGrant grant;

        lock (_gate)
        {
            var file = LoadForWrite();
            if (FindRecord(file, id) is not { Revoked: false } record) return null;

            record.Scopes = [.. scopes];
            record.Version++;
            Save(file);
            grant = ToGrant(record);
        }

        Changed?.Invoke();
        return grant;
    }

    /// <inheritdoc />
    /// <exception cref="UnreadableMcpGrantFileException">The file exists and does not parse.</exception>
    public McpGrant? Revoke(string id)
    {
        McpGrant grant;

        lock (_gate)
        {
            var file = LoadForWrite();
            if (FindRecord(file, id) is not { } record) return null;

            record.Revoked = true;
            record.Version++;
            Save(file);
            grant = ToGrant(record);
        }

        Changed?.Invoke();
        return grant;
    }

    /// <summary>The grant's secret, or null when there is no such grant or this user cannot
    /// open its envelope.</summary>
    internal string? OpenSecret(string id)
    {
        lock (_gate)
        {
            return FindRecord(Load(), id) is { } record ? SecretEnvelope.Open(record.Secret, out _) : null;
        }
    }

    private static GrantRecord? FindRecord(GrantFile file, string id) =>
        file.Grants.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));

    /// <summary>What a write starts from: the file as <see cref="Load"/> reads it, or a refusal
    /// when it exists and does not parse — writing then would replace every grant in it, and
    /// their secrets, with the one being written.</summary>
    private GrantFile LoadForWrite()
    {
        var file = Load();
        return file.Unreadable ? throw new UnreadableMcpGrantFileException(_path) : file;
    }

    /// <summary>
    /// The file, each grant's secret sealed: a missing or plaintext one is sealed and written
    /// back before this returns. A file that is absent is no grants; one that does not parse is
    /// no grants too, marked <see cref="GrantFile.Unreadable"/> and never written back.
    /// </summary>
    private GrantFile Load()
    {
        GrantFile file;

        try
        {
            if (!File.Exists(_path)) return new GrantFile();

            file = JsonSerializer.Deserialize<GrantFile>(File.ReadAllText(_path), JsonOptions) ?? new GrantFile { Unreadable = true };
        }
        catch (JsonException)
        {
            return new GrantFile { Unreadable = true };
        }

        // `"grants": null` parses, and the serializer then overrides the initializer with
        // null. It is not a list of grants, so it reads as a file that did not parse: no
        // grants, and never written over.
        if (file.Records is null) return new GrantFile { Unreadable = true };

        var resealed = false;

        foreach (var record in file.Grants)
        {
            record.Scopes ??= [];

            if (string.IsNullOrWhiteSpace(record.Secret))
            {
                record.Secret = SecretEnvelope.Seal(NewSecret());
                resealed = true;
                continue;
            }

            var secret = SecretEnvelope.Open(record.Secret, out var wasPlaintext);

            if (wasPlaintext)
            {
                record.Secret = SecretEnvelope.Seal(secret);
                resealed = true;
            }
        }

        if (resealed) Save(file);

        return file;
    }

    private void Save(GrantFile file)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(file, JsonOptions));
        File.Move(temporary, _path, overwrite: true);
    }

    private static McpGrant ToGrant(GrantRecord record) => new(
        record.Id!,
        record.Name ?? string.Empty,
        [.. record.Scopes ?? []],
        record.Version,
        record.CreatedBy ?? string.Empty,
        record.CreatedAt,
        record.Revoked);

    private static string NewId() =>
        McpGrantClaims.IdPrefix + new string(RandomNumberGenerator.GetItems<char>(IdAlphabet, IdLength));

    private static string NewSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The file's shape: an object rather than a bare array, so a later key sits
    /// beside the grants without a migration — and is written back as it was read, through
    /// <see cref="Unknown"/>, by a release that does not know it.</summary>
    private sealed class GrantFile
    {
        /// <summary>Every record as stored, a record with a blank id or a null entry included:
        /// a save writes back what it read, and only <see cref="Grants"/> leaves them out.</summary>
        [JsonPropertyName("grants")]
        public List<GrantRecord?> Records { get; set; } = [];

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Unknown { get; set; }

        /// <summary>The records that are grants: present, with an id.</summary>
        [JsonIgnore]
        public IEnumerable<GrantRecord> Grants =>
            Records.OfType<GrantRecord>().Where(record => !string.IsNullOrWhiteSpace(record.Id));

        /// <summary>Whether the file exists and did not parse.</summary>
        [JsonIgnore]
        public bool Unreadable { get; init; }
    }

    /// <summary>One grant as stored. Mutable, because a load reseals and a revoke edits in
    /// place before the file is written back.</summary>
    private sealed class GrantRecord
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public List<string>? Scopes { get; set; }

        /// <summary>1 when a hand-written record leaves it out, the version every grant
        /// starts at.</summary>
        public int Version { get; set; } = 1;

        public string? CreatedBy { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public bool Revoked { get; set; }

        public string? Secret { get; set; }

        /// <summary>Members a later release writes, kept so this one's save does not drop them.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Unknown { get; set; }
    }
}

/// <summary>
/// <c>mcp-grants.json</c> exists and does not parse, so the store refuses to write it: a write
/// would replace every grant in it with the one being written. An <see cref="IOException"/>,
/// so a caller that already treats a file it cannot use as a refusal treats this one so too.
/// The fix is the person's: mend the file, or move it aside.
/// </summary>
public sealed class UnreadableMcpGrantFileException : IOException
{
    public UnreadableMcpGrantFileException(string path)
        : base($"The MCP grants file {path} does not parse, so it is not written over. Mend it or move it aside.")
    {
        Path = path;
    }

    /// <summary>The file that does not parse.</summary>
    public string Path { get; }
}
