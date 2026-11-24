namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// What <see cref="RepositoryAliasFollower"/> reads off the store beside the moves
/// themselves (local ADR 0060 §3): a cursor per local store over the registry's alias
/// moves, whether the moves in memory are the ones on disk, and whether a save in
/// progress is a moving edit. The cursors live in the per-user <c>github.json</c>,
/// never in the registry, because every device keeps its own.
/// </summary>
public sealed partial class GitHubSettingsStore
{
    /// <summary>Serializes the writes of both files and the composed value they are
    /// written from, so a cursor marked on another thread never writes a
    /// <c>github.json</c> older than a save that ran meanwhile.</summary>
    private readonly Lock _writeLock = new();

    /// <summary>Per local store key, the <see cref="AliasMove.At"/> of the last move
    /// group applied to it.</summary>
    private Dictionary<string, DateTimeOffset> _appliedThrough = new(StringComparer.Ordinal);

    /// <summary>The alias moves as the registry file last held them: read, or
    /// written.</summary>
    private List<AliasMove> _persistedAliasMoves = [];

    /// <summary>The thread a moving save is running on, or 0.</summary>
    private int _savingMovesOnThread;

    /// <summary>
    /// The <see cref="AliasMove.At"/> of the last move group this device applied to
    /// the local store <paramref name="storeKey"/> names, or null when that store has
    /// no cursor yet. A null cursor is not "apply everything": a store that holds
    /// data under the current aliases would have a historical swap undone, so the
    /// follower decides from what the store holds where it starts.
    /// </summary>
    public DateTimeOffset? AliasMovesAppliedThrough(string storeKey)
    {
        ArgumentNullException.ThrowIfNull(storeKey);

        lock (_writeLock) return _appliedThrough.TryGetValue(storeKey, out var at) ? at : null;
    }

    /// <summary>
    /// Records that every move group up to and including <paramref name="at"/> was
    /// applied to the local store <paramref name="storeKey"/> names, and writes it to
    /// the per-user file from the value current at the time of writing. It only ever
    /// moves forward: a time at or before the cursor changes nothing. Not a change to
    /// the configuration, so <see cref="Changed"/> is not raised.
    /// </summary>
    /// <returns>An error message when the file could not be written; the cursor still
    /// moves for this session.</returns>
    public string? MarkAliasMovesApplied(string storeKey, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(storeKey);

        lock (_writeLock)
        {
            if (_appliedThrough.TryGetValue(storeKey, out var through) && at <= through) return null;

            _appliedThrough[storeKey] = at;
            return WriteLocal(Current);
        }
    }

    /// <summary>Whether the alias moves in <see cref="GitHubSettings.AliasMoves"/>
    /// include one the registry file does not hold, because the save that recorded it
    /// failed. Nothing should be re-keyed to an alias the next start may not
    /// know.</summary>
    internal bool HasUnsavedAliasMoves
    {
        get
        {
            lock (_writeLock) return !_aliasMoves.SequenceEqual(_persistedAliasMoves);
        }
    }

    /// <summary>Whether the <see cref="Changed"/> being raised on this thread comes
    /// from an edit that moves aliases, whose moves follow through
    /// <see cref="AliasesMoved"/> once its save held.</summary>
    internal bool IsSavingAliasMovesOnThisThread =>
        _savingMovesOnThread == Environment.CurrentManagedThreadId;
}
