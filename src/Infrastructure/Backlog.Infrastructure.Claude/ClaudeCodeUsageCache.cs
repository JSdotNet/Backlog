using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Backlog.Infrastructure.Claude;

/// <summary>
/// The disk half of <see cref="IClaudeCodeUsageCache"/>: one small JSON file per
/// account per actor per day, under the spend cache root a host names.
/// <para>
/// One file per day rather than one per account, because a day is what is
/// written — a dashboard read that settled three new days writes three files and
/// touches nothing else, where a per-account file would be rewritten whole for
/// every one of them and could lose the other two to a torn write.
/// </para>
/// <para>
/// The actor names a folder through a digest rather than as itself. It is an
/// email address, and while a file name could carry one, an address on disk in
/// a cache folder is a small leak for no gain — nothing ever needs to read it
/// back out of the path.
/// </para>
/// <para>
/// Non-throwing on every path, like the caches beside it: a corrupt or unreadable
/// day is fetched, and one that could not be written is fetched again next time.
/// </para>
/// </summary>
public sealed class ClaudeCodeUsageCache(Func<string> cacheRoot) : IClaudeCodeUsageCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private const int Version = 1;

    private const string FolderName = "claude";

    private readonly Func<string> _cacheRoot = cacheRoot ?? throw new ArgumentNullException(nameof(cacheRoot));

    public ClaudeCodeSettledDay? TryRead(ClaudeAccount account, string actor, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        try
        {
            var path = EntryPath(account, actor, day);
            if (!File.Exists(path)) return null;

            var stored = JsonSerializer.Deserialize<StoredDay>(File.ReadAllText(path), JsonOptions);

            if (stored is null || stored.Version != Version) return null;

            return new ClaudeCodeSettledDay([.. stored.Models.Select(model => new ClaudeCodeModelUsage(
                model.Model,
                new ClaudeTokenUsage(
                    model.InputTokens,
                    model.OutputTokens,
                    model.CacheCreationInputTokens,
                    model.CacheReadInputTokens),
                model.EstimatedCost,
                model.Currency ?? "USD"))]);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A half-written or unreadable day is a miss that costs one call.
            return null;
        }
    }

    public void Write(ClaudeAccount account, string actor, DateOnly day, ClaudeCodeSettledDay usage)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentNullException.ThrowIfNull(usage);

        var path = EntryPath(account, actor, day);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            File.WriteAllText(path, JsonSerializer.Serialize(
                new StoredDay
                {
                    Version = Version,
                    Models = [.. usage.Models.Select(model => new StoredModel
                    {
                        Model = model.Model,
                        InputTokens = model.Tokens.InputTokens,
                        OutputTokens = model.Tokens.OutputTokens,
                        CacheCreationInputTokens = model.Tokens.CacheCreationInputTokens,
                        CacheReadInputTokens = model.Tokens.CacheReadInputTokens,
                        EstimatedCost = model.EstimatedCost,
                        Currency = model.Currency
                    })]
                },
                JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A day that could not be written is fetched again next time.
        }
    }

    public void Forget()
    {
        var root = Path.Combine(_cacheRoot(), FolderName);

        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left behind rather than fatal.
        }
    }

    private string EntryPath(ClaudeAccount account, string actor, DateOnly day) =>
        Path.Combine(
            _cacheRoot(),
            FolderName,
            Safe(account.Id),
            Digest(actor.Trim().ToLowerInvariant()),
            day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json");

    /// <summary>The id is generated as hex and is already a file name; this only
    /// guards against a settings file somebody edited by hand.</summary>
    private static string Safe(string name)
    {
        var readable = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            readable.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '-');
        }

        var trimmed = readable.ToString().Trim('-');

        return trimmed.Length == 0 ? Digest(name) : trimmed;
    }

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();

    private sealed record StoredDay
    {
        public int Version { get; init; }

        public StoredModel[] Models { get; init; } = [];
    }

    private sealed record StoredModel
    {
        public string? Model { get; init; }

        public long InputTokens { get; init; }

        public long OutputTokens { get; init; }

        public long CacheCreationInputTokens { get; init; }

        public long CacheReadInputTokens { get; init; }

        public decimal EstimatedCost { get; init; }

        public string? Currency { get; init; }
    }
}
