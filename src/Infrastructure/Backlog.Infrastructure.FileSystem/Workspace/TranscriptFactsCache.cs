using System.Text.Json;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The disk half of <see cref="ITranscriptFactsCache"/>: one small JSON file per
/// transcript, in a folder of its own under the same root as
/// <see cref="AgentActivityCache"/>.
/// <para>
/// The same root, because both are parses of this machine's own transcripts keyed
/// on those files' paths and write times — meaningless on any other device, and
/// forgotten together when the activity cache is. Its own folder inside it, so the
/// two layouts can be versioned apart and a file name never collides with the
/// activity entry for the same transcript.
/// </para>
/// <para>
/// The key is checked on read rather than folded into the file name: an entry is
/// named for the transcript alone and carries the length and write time it was
/// written for, so a transcript that grew replaces its own entry instead of
/// leaving one stale file per append behind it.
/// </para>
/// <para>
/// Non-throwing on every path, like every cache beside it: a corrupt or unreadable
/// entry is a miss that costs one pass over the file, and one that could not be
/// written costs the same pass again next time.
/// </para>
/// </summary>
public sealed class TranscriptFactsCache(Func<string> cacheRoot) : ITranscriptFactsCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private const int Version = 1;

    private const string FolderName = "facts";

    private readonly Func<string> _cacheRoot = cacheRoot ?? throw new ArgumentNullException(nameof(cacheRoot));

    public TranscriptFacts? TryRead(string path, long length, DateTimeOffset writtenAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            var entry = EntryPath(path);
            if (!File.Exists(entry)) return null;

            var stored = JsonSerializer.Deserialize<StoredFacts>(File.ReadAllText(entry), JsonOptions);

            // A shape this version cannot read, or a different write of the same
            // file: a miss rather than a guess, either way.
            if (stored is null
                || stored.Version != Version
                || stored.Length != length
                || stored.WrittenAtTicks != writtenAt.UtcTicks)
            {
                return null;
            }

            return new TranscriptFacts(stored.Folder ?? string.Empty, stored.Branch, stored.Turns);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A half-written or unreadable entry is a miss. Throwing here would fail a
            // whole session read over one file that costs a single pass to replace.
            return null;
        }
    }

    public void Write(string path, long length, DateTimeOffset writtenAt, TranscriptFacts facts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(facts);

        var entry = EntryPath(path);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(entry)!);

            File.WriteAllText(entry, JsonSerializer.Serialize(
                new StoredFacts
                {
                    Version = Version,
                    Length = length,
                    WrittenAtTicks = writtenAt.UtcTicks,
                    Folder = facts.Folder,
                    Branch = facts.Branch,
                    Turns = facts.Turns
                },
                JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An entry that could not be written is read again next time. Wasteful,
            // not wrong, and far better than failing a read that succeeded.
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
            // Left behind rather than fatal. The worst outcome is that the list is
            // still answered from disk, which is the state the person was already in.
        }
    }

    private string EntryPath(string path) =>
        Path.Combine(_cacheRoot(), FolderName, CachePaths.Safe(Path.GetFileNameWithoutExtension(path), path) + ".json");

    private sealed record StoredFacts
    {
        public int Version { get; init; }

        public long Length { get; init; }

        public long WrittenAtTicks { get; init; }

        public string? Folder { get; init; }

        public string? Branch { get; init; }

        public int? Turns { get; init; }
    }
}
