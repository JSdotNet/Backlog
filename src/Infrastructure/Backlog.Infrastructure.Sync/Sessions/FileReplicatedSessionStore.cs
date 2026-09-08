using System.Text.Json;

using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.Sessions;

/// <summary>
/// The records pulled from the owner's feed, as one JSON file beside the session
/// sync state.
/// <para>
/// Beside the progress rather than inside it: the cursor is saved after every
/// page and is two scalars, while this is up to
/// <see cref="ReplicatedSessionLimits.PerEnvironmentPerAgent"/> records per
/// environment per agent. Writing the whole cache every time a cursor moved would
/// be work done to change nothing, and the two have different reasons to be
/// unreadable.
/// </para>
/// <para>
/// Plaintext, like both stores beside it, because nothing in it is a secret —
/// it is the metadata .arc42/adr/0005 §Session records permits to leave a machine
/// in the first place, which is the whole basis on which it travelled here. It
/// carries no working folder, no title and no transcript, because those never
/// crossed the wire.
/// </para>
/// <para>
/// The whole file is rewritten on every save rather than appended to. A record is
/// a reading rather than an event — a session that moves is pushed again — so the
/// file is a set keyed on the machine, the agent and the session id, and a set is
/// not something an append can maintain. At the cap this file is a few hundred
/// small records, so rewriting it is cheaper than the pull that produced it.
/// </para>
/// <para>
/// A file that cannot be read reads as empty rather than as an error, the same
/// way the two stores beside it do. That loses records the cursor has already
/// moved past, which is a real loss and not one this class can repair: the
/// recovery is a person asking for the feed to be read again, and refusing to
/// start would leave them unable to.
/// </para>
/// </summary>
public sealed class FileReplicatedSessionStore : IReplicatedSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Guards the read-modify-write in <see cref="Save"/>. One pull
    /// applies its pages in order on one thread, but a settings screen asking for
    /// a fresh cycle while the timer is mid-page is two — and a lost merge here
    /// would drop records the cursor has already passed.</summary>
    private readonly Lock _gate = new();

    private readonly string _path;

    public FileReplicatedSessionStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Current = Read();
    }

    public event Action? Changed;

    public ReplicatedSessions Current { get; private set; }

    public string StorePath => _path;

    public void Save(IReadOnlyList<SessionRecordEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0) return;

        lock (_gate)
        {
            var merged = Merge(Current, entries);

            // Written before adopted, for the reason the two stores beside this
            // one use that order: a write that fails throws and leaves Current as
            // it was, so the process does not go on serving a list the next
            // launch will not find.
            File.WriteAllText(_path, JsonSerializer.Serialize(merged, JsonOptions));

            Current = merged;
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// The held set with a page folded into it: newer readings replace older ones
    /// for the same session, and each environment's each agent is cut back to the
    /// cap.
    /// <para>
    /// Static and pure so the merge — which is where a record is silently lost or
    /// silently duplicated — can be asserted without a directory.
    /// </para>
    /// <para>
    /// A later reading of a session replaces an earlier one on
    /// <see cref="SessionRecordEntry.ServerTimestamp"/> rather than on
    /// <see cref="SessionRecord.LastActivityAt"/>. The store's stamp is the one
    /// ordering both devices agree on; the activity time comes off the writing
    /// machine's clock, and two machines with skewed clocks would otherwise
    /// disagree about which of two readings was the later one. Records are
    /// single-writer, so this is an ordering question and never a conflict.
    /// </para>
    /// </summary>
    internal static ReplicatedSessions Merge(ReplicatedSessions current, IReadOnlyList<SessionRecordEntry> arriving)
    {
        var byIdentity = new Dictionary<(Guid Machine, string Agent, string Session), SessionRecordEntry>();

        foreach (var entry in current.Entries.Concat(arriving))
        {
            var key = Identity(entry);

            if (!byIdentity.TryGetValue(key, out var held) || entry.ServerTimestamp >= held.ServerTimestamp)
            {
                byIdentity[key] = entry;
            }
        }

        var kept = new List<SessionRecordEntry>();
        var dropped = 0;

        foreach (var group in byIdentity.Values.GroupBy(entry => (entry.MachineId, entry.Record.AgentKind)))
        {
            var ordered = group
                .OrderByDescending(entry => entry.Record.LastActivityAt)
                .ToList();

            kept.AddRange(ordered.Take(ReplicatedSessionLimits.PerEnvironmentPerAgent));
            dropped += Math.Max(0, ordered.Count - ReplicatedSessionLimits.PerEnvironmentPerAgent);
        }

        return new ReplicatedSessions(
            [.. kept.OrderByDescending(entry => entry.Record.LastActivityAt)],
            // Carried forward and added to rather than recomputed. What the cap
            // discarded on an earlier run is gone from this file, so the only
            // record of it having existed is this number.
            current.Dropped + dropped);
    }

    /// <summary>The three-part identity a session record has.
    /// <c>.domain/sessions/naming.md#session-identity</c> puts it at the agent plus
    /// the id that agent issued, because two agents may issue the same string; the
    /// machine leads it because two environments may too.</summary>
    private static (Guid, string, string) Identity(SessionRecordEntry entry) =>
        (entry.MachineId, entry.Record.AgentKind, entry.Record.SessionId);

    private ReplicatedSessions Read()
    {
        try
        {
            if (!File.Exists(_path)) return ReplicatedSessions.Empty;

            return JsonSerializer.Deserialize<ReplicatedSessions>(File.ReadAllText(_path), JsonOptions)
                ?? ReplicatedSessions.Empty;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException)
        {
            return ReplicatedSessions.Empty;
        }
    }
}
