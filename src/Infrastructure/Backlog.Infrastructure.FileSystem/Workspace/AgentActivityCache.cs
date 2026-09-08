using System.Text.Json;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The disk half of <see cref="IAgentActivityCache"/>: one small JSON file per parsed
/// transcript, under one flat folder.
/// <para>
/// It lives here rather than beside the port for the reason
/// <see cref="PullRequestDetailCache"/> does: the half phrased in the Sessions
/// context's own types stays in that module's abstractions and may not reach for this
/// one, while the half that decides where bytes land belongs with the workspace that
/// knows the root. The root arrives as a delegate for the same reason too — it is a
/// workspace location, and one read once at construction would not follow somebody who
/// moved it.
/// </para>
/// <para>
/// A file per transcript rather than one file for everything, so that writing one
/// entry can never lose another, and so that forgetting is a directory delete rather
/// than a rewrite. There is no per-agent folder: an entry is keyed on a full path, and
/// a directory level that only ever held "claude" and "copilot" would be structure
/// nothing reads.
/// </para>
/// </summary>
public sealed class AgentActivityCache(Func<string> cacheRoot) : IAgentActivityCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// The shape stored on disk. Bumped whenever a field is added, removed, or changes
    /// meaning — <b>and whenever the parse that produces one changes</b>.
    /// <para>
    /// Version rather than tolerant deserialization, for the reason
    /// <see cref="PullRequestDetailCache"/> states: the honest default for a field that
    /// was not there is not the type's default. An entry written before waits existed
    /// would deserialize to an empty wait list and read as "this session never waited
    /// on anybody", which is a claim about a transcript that nothing ever established.
    /// Reading it as a miss costs one parse and states nothing untrue.
    /// </para>
    /// <para>
    /// The second clause is the one that is easy to miss, and it is why this is at 2. The
    /// key is the file, the moment it was written, and the threshold — so a change to the
    /// <em>rules</em> invalidates nothing at all: the transcript has not moved and neither
    /// has the threshold, and every stale entry keeps being served as though it were
    /// right. Version 1 was written by a parse that admitted bookkeeping lines as
    /// activity, which deleted every wait it saw. Bumping is the whole of the remedy, and
    /// a parse changed without bumping is a wrong figure with nothing on screen to say so.
    /// </para>
    /// </summary>
    private const int Version = 2;

    private readonly Func<string> _cacheRoot = cacheRoot ?? throw new ArgumentNullException(nameof(cacheRoot));

    public AgentActivityEntry? TryRead(string path, DateTimeOffset writtenAt, TimeSpan idleAfter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            var entry = EntryPath(path);
            if (!File.Exists(entry)) return null;

            var stored = JsonSerializer.Deserialize<StoredActivity>(File.ReadAllText(entry), JsonOptions);

            // Three ways of being about something else, and all three are a miss rather
            // than a guess: a shape this version cannot read, a different write of the
            // same file, and a fold at a threshold nobody uses any more.
            if (stored is null
                || stored.Version != Version
                || stored.WrittenAtTicks != writtenAt.UtcTicks
                || stored.IdleAfterTicks != idleAfter.Ticks)
            {
                return null;
            }

            return new AgentActivityEntry(
                [.. stored.Runs.Select(run => new AgentActivityRun(run.From, run.To))],
                [.. stored.Waits.Select(wait => new AgentActivityWait(wait.From, wait.To))],
                TimeSpan.FromTicks(stored.IdleAfterTicks));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A half-written or unreadable entry is a miss. Throwing here would fail a
            // whole dashboard read over one corrupt file that costs a single parse to
            // replace.
            return null;
        }
    }

    public void Write(string path, DateTimeOffset writtenAt, AgentActivityEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(entry);

        var stored = EntryPath(path);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stored)!);

            File.WriteAllText(stored, JsonSerializer.Serialize(
                new StoredActivity
                {
                    Version = Version,
                    WrittenAtTicks = writtenAt.UtcTicks,
                    IdleAfterTicks = entry.IdleAfter.Ticks,
                    Runs = [.. entry.Runs.Select(run => new StoredInterval { From = run.StartedAt, To = run.EndedAt })],
                    Waits = [.. entry.Waits.Select(wait => new StoredInterval { From = wait.StartedAt, To = wait.EndedAt })]
                },
                JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An entry that could not be written is parsed again next time. Wasteful,
            // not wrong, and far better than failing a read that succeeded.
        }
    }

    public void Forget()
    {
        var root = _cacheRoot();

        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left behind rather than fatal. The worst outcome is that the figures are
            // still answered from disk, which is the state the person was already in.
        }
    }

    /// <summary>
    /// Where one transcript's entry goes: the session id, so the folder is something a
    /// person can look at and recognise, plus a digest of the whole path, so a session
    /// filed under two project slugs does not collapse into one entry that answers for
    /// whichever was parsed last.
    /// </summary>
    private string EntryPath(string path) =>
        Path.Combine(_cacheRoot(), CachePaths.Safe(Path.GetFileNameWithoutExtension(path), path) + ".json");

    /// <summary>The stored shape. Separate from <see cref="AgentActivityEntry"/> so
    /// that the file format is a decision this class makes rather than a consequence of
    /// a record somebody refactors.</summary>
    private sealed record StoredActivity
    {
        public int Version { get; init; }

        /// <summary>
        /// The transcript's last write, and the threshold it was folded at, both as
        /// ticks.
        /// <para>
        /// Ticks rather than an instant and a duration because these two are compared
        /// for exact equality and nothing else. A serializer's choice of how to spell a
        /// moment or a span is a round trip this class would then depend on being exact,
        /// and the failure would be silent: every entry a miss, every read slow, nothing
        /// on screen wrong.
        /// </para>
        /// </summary>
        public long WrittenAtTicks { get; init; }

        /// <inheritdoc cref="WrittenAtTicks"/>
        public long IdleAfterTicks { get; init; }

        /// <summary>The intervals, as instants rather than as ticks, because these are
        /// read back and never matched — and a cache file somebody opens to see what it
        /// says should say it.</summary>
        public StoredInterval[] Runs { get; init; } = [];

        /// <inheritdoc cref="Runs"/>
        public StoredInterval[] Waits { get; init; } = [];
    }

    private sealed record StoredInterval
    {
        public DateTimeOffset From { get; init; }

        public DateTimeOffset To { get; init; }
    }
}
