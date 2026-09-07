namespace Backlog.Infrastructure.Sync;

/// <summary>
/// How far this device has got in each direction.
/// <para>
/// <paramref name="PushWatermark"/> is the highest <c>UpdatedAt</c> this device
/// has had accepted, so the next push asks for what changed after it. Advanced
/// to what was accepted and never to "now": a task saved while a batch was in
/// flight has a stamp between the two, and a watermark set to now would step
/// straight over it and lose that edit for good.
/// </para>
/// <para>
/// <paramref name="PullCursor"/> is the service's own cursor, opaque here. Null
/// means "from the beginning", which is what a freshly paired device sends and
/// what it sends again after the service says the cursor has expired.
/// </para>
/// </summary>
public sealed record TaskSyncState(DateTimeOffset PushWatermark, string? PullCursor);

/// <summary>
/// Where this device keeps its replication progress between runs.
/// <para>
/// Deliberately the same shape as <see cref="IDeviceCredentialStore"/> — read
/// once on construction, replaced whole by <see cref="Save"/>,
/// <see cref="Changed"/> for whatever holds a derived value, and a
/// <see cref="StorePath"/> a settings screen can show — so a host wires the two
/// the same way and neither is the odd one out to configure.
/// </para>
/// <para>
/// There is still no <c>Clear</c>, and it is <see cref="Save"/> that makes one
/// unnecessary rather than nothing needing what it would do. Forgetting the
/// cursor is <c>Save(state with { PullCursor = null })</c> — what a client does
/// when the service retires its cursor, and what
/// <c>TaskSyncWorker.RehydrateFromScratch</c> does to fill a machine that was
/// emptied or restored back up from the feed. Forgetting the watermark is
/// <c>Save(state with { PushWatermark = DateTimeOffset.MinValue })</c>, which
/// re-pushes every task on the machine — <c>TaskSyncWorker.RepublishEverything</c>,
/// which exists so a replica can be treated as disposable: deleted, recreated,
/// and filled again from a device that still holds the data.
/// </para>
/// <para>
/// Re-pushing costs bandwidth and nothing else. The replica upserts whole
/// documents under last-write-wins, so a document sent a second time lands in
/// the state it is already in; that is also why the push loop can advance its
/// watermark per batch and resend one batch's worth after a failure without
/// anybody having to think about it.
/// </para>
/// </summary>
public interface ITaskSyncStateStore
{
    /// <summary>Where this device has got to, or a state that has got nowhere —
    /// which is also what a store answers when the file it reads turns out to be
    /// unreadable. Starting over is the only recovery from a progress marker
    /// that cannot be read, and starting over is safe: pushing from the
    /// beginning re-sends documents the replica already has, and a whole-document
    /// upsert has nothing to do differently the second time.</summary>
    TaskSyncState Current { get; }

    /// <summary>Records the state, replacing what was there, and raises
    /// <see cref="Changed"/>. A store that cannot record it throws and leaves
    /// <see cref="Current"/> alone — the same contract
    /// <see cref="IDeviceCredentialStore.Save"/> keeps, and for a related
    /// reason: a watermark that moved in memory but not on disk would skip
    /// everything between the two on the next run.</summary>
    void Save(TaskSyncState state);

    /// <summary>Where the progress is kept, for a settings screen to show.</summary>
    string StorePath { get; }

    /// <summary>Raised after <see cref="Save"/>.</summary>
    event Action? Changed;
}
