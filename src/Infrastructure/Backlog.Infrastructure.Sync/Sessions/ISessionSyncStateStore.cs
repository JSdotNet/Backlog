namespace Backlog.Infrastructure.Sync.Sessions;

/// <summary>
/// How far this device has got in each direction of the session exchange.
/// <para>
/// <paramref name="PushWatermark"/> is the highest <c>LastActivityAt</c> this
/// device has had accepted, so the next push asks for what moved after it.
/// <strong>Advanced to what was accepted and never to "now"</strong>: a session
/// that took a turn while a batch was in flight carries a stamp between the two,
/// and a watermark set to now would step straight over it. The record would
/// never be selected again, the far side would go on showing the session as it
/// was before that turn, and nothing would fail to say so. It is the same trap
/// <see cref="ITaskSyncStateStore"/> describes, and it is worth stating twice
/// because the two loops are written separately and either could be edited
/// without the other.
/// </para>
/// <para>
/// It is one instant rather than one per agent. A session's stamp is a wall
/// clock reading and not an agent-scoped sequence, so a single instant selects
/// correctly across both readers; a watermark per agent would be two numbers
/// answering one question, and three the day a third assistant appears.
/// </para>
/// <para>
/// <paramref name="PullCursor"/> is the service's own cursor, opaque here. Null
/// means "from the beginning", which is what a freshly paired device sends and
/// what it sends again after the service says the cursor has expired.
/// </para>
/// </summary>
public sealed record SessionSyncState(DateTimeOffset PushWatermark, string? PullCursor);

/// <summary>
/// Where this device keeps its session-replication progress between runs.
/// <para>
/// The same shape as <see cref="ITaskSyncStateStore"/>, and deliberately
/// <strong>not the same store</strong>. One file carrying both exchanges'
/// progress is the tidier-looking arrangement, and it fails the one requirement
/// that matters here: a person's task sync must not stop because session sync
/// did.
/// </para>
/// <para>
/// Two things would make it stop. The exchanges run on independent timers, so
/// both would be doing read-modify-write — <c>Save(Current with { ... })</c> —
/// against one file from two thread-pool threads, and the loser's write would
/// put back the value it read before the winner saved, silently rewinding a
/// watermark that had already advanced. And a file that cannot be read is
/// answered with "got nowhere" by design, so a half-written session save would
/// reset the task watermark too and re-push the whole machine. Neither failure
/// announces itself, which is exactly the class of failure .arc42/adr/0005
/// exists to remove.
/// </para>
/// <para>
/// What one file would have bought is one host registration instead of two, and
/// <c>SyncClientRegistration.AddSessionSyncStores</c> gives that back without the
/// coupling: the host names one folder and gets both files inside it.
/// </para>
/// <para>
/// There is no <c>Clear</c>, for the reason <see cref="ITaskSyncStateStore"/>
/// gives: forgetting the cursor is <c>Save(state with { PullCursor = null })</c>
/// and forgetting the watermark is
/// <c>Save(state with { PushWatermark = DateTimeOffset.MinValue })</c>. Both are
/// safe. A record sent a second time lands on the document it already wrote —
/// the replica keys a session document on the machine id, the agent and the
/// session id — so re-pushing costs bandwidth and nothing else.
/// </para>
/// </summary>
public interface ISessionSyncStateStore
{
    /// <summary>Where this device has got to, or a state that has got nowhere —
    /// which is also what a store answers when the file it reads turns out to be
    /// unreadable. Starting over is the only recovery from a progress marker
    /// that cannot be read, and starting over is safe here.</summary>
    SessionSyncState Current { get; }

    /// <summary>Records the state, replacing what was there, and raises
    /// <see cref="Changed"/>. A store that cannot record it throws and leaves
    /// <see cref="Current"/> alone: a watermark that moved in memory but not on
    /// disk would skip everything between the two on the next run.</summary>
    void Save(SessionSyncState state);

    /// <summary>Where the progress is kept, for a settings screen to show.</summary>
    string StorePath { get; }

    /// <summary>Raised after <see cref="Save"/>.</summary>
    event Action? Changed;
}
