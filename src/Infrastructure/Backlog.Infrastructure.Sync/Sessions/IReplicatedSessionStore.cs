using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.Sessions;

/// <summary>
/// What this device has been told about other environments' sessions, and how
/// much of it there was.
/// </summary>
/// <param name="Entries">The records kept, newest activity first.</param>
/// <param name="Dropped">How many distinct sessions the cap has discarded over
/// this store's life. Kept so the surface can still say the list is partial:
/// <c>AgentSessionCatalog.Discovered</c> means "how many existed, before the cap",
/// and a store that only counted what it had left would report a truncated list
/// as the whole history — which is the one thing a capped list must not do.</param>
public sealed record ReplicatedSessions(IReadOnlyList<SessionRecordEntry> Entries, int Dropped)
{
    public static ReplicatedSessions Empty { get; } = new([], 0);
}

/// <summary>
/// Where the records pulled from the owner's feed are kept.
/// <para>
/// <strong>They have to persist, and the cursor is why.</strong> A pull saves its
/// cursor after every page, so the feed's position has already moved past a
/// record by the time anything reads it; a cache that lived in memory would be
/// empty on the next launch and the service would never send those records again.
/// The pane would show a machine's sessions until the app was closed and then
/// silently stop, which reads as the other machine having gone quiet.
/// </para>
/// <para>
/// It also has to answer without a network call. The pane reads on every refresh
/// and the Dashboard reads through the same port; an implementation that fetched
/// would make opening a list a request, and an offline laptop would show nothing
/// where it should show the last thing it was told.
/// </para>
/// <para>
/// A port rather than a concrete file store because a test needs to hand records
/// in and read them back without a directory, and because the shipped heads
/// choose where their own per-user state lives — the same bargain
/// <see cref="ISessionSyncStateStore"/> makes.
/// </para>
/// </summary>
public interface IReplicatedSessionStore
{
    /// <summary>What is held now, or <see cref="ReplicatedSessions.Empty"/> when
    /// nothing is — including when what is on disk cannot be read. Losing the
    /// cache is a real loss, since the cursor has moved past those records, but it
    /// is a loss the person recovers from by asking for a fresh pull rather than
    /// one anything here can repair by throwing.</summary>
    ReplicatedSessions Current { get; }

    /// <summary>
    /// Takes a page's worth of records, replacing any it already holds for the
    /// same session, and raises <see cref="Changed"/>.
    /// <para>
    /// Replacing rather than appending, because the feed is a log of readings and
    /// not of events: a session that took another turn is pushed again with a
    /// later stamp, and .arc42/adr/0005 §Session records says a session that moves
    /// gets a later record rather than an edit. Two readings of one session are
    /// two rows of the same thing, and a store that kept both would show the
    /// session twice with the older row claiming an activity time that has been
    /// superseded.
    /// </para>
    /// <para>
    /// Identity is the machine id, the agent and the session id together — never
    /// the session id alone. <c>.domain/sessions/naming.md#session-identity</c>
    /// puts a session's identity at the agent plus the id that agent issued
    /// because two agents may issue the same string, and the machine leads it
    /// because two environments may too.
    /// </para>
    /// </summary>
    void Save(IReadOnlyList<SessionRecordEntry> entries);

    /// <summary>Where the records are kept, for a settings screen to show.</summary>
    string StorePath { get; }

    /// <summary>Raised after <see cref="Save"/>, so a surface holding a rendered
    /// list can redraw when a cycle brings something new. It arrives on whatever
    /// thread ran the cycle, which is a thread-pool thread: a renderer subscribing
    /// to it has to marshal.</summary>
    event Action? Changed;
}

/// <summary>How much of another environment's history this device keeps.</summary>
public static class ReplicatedSessionLimits
{
    /// <summary>
    /// The most recent this many sessions per environment per agent.
    /// <para>
    /// <see cref="AgentSessionLimits.PerAgent"/>'s number and its reasoning,
    /// applied one level down. That cap is per agent because a single cap over a
    /// merged list would be filled almost entirely by whichever agent wrote most
    /// recently, squeezing the other out of a surface whose whole point is showing
    /// both; the same argument applies to environments, so a chatty machine cannot
    /// crowd out a quiet one. Per environment <em>and</em> per agent is therefore
    /// the only split that leaves every combination visible.
    /// </para>
    /// <para>
    /// It bounds a file this device writes on every page of every pull, which is
    /// the other reason it exists: a fleet's whole history is not something a
    /// laptop should be rewriting every five minutes.
    /// </para>
    /// </summary>
    public const int PerEnvironmentPerAgent = AgentSessionLimits.PerAgent;
}
