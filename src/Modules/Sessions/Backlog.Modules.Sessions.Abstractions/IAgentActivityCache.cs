namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// Keeps the parsed runs of a transcript on disk, keyed on the file, the moment it
/// was last written and the threshold it was folded at, so an activity read parses
/// only what has changed.
/// <para>
/// The key is the triple, not the path: a finished transcript never changes, so it is
/// parsed once ever and read from here forever after, while the one transcript being
/// appended to right now misses on every refresh — which is exactly the file whose
/// answer must not be stale.
/// </para>
/// <para>
/// The threshold is part of the key rather than part of a version number, and that is
/// the difference worth stating. <see cref="AgentActivityEntry.IdleAfter"/> is a
/// judgement the parser made, not a shape this file has; moving it invalidates every
/// entry by comparison, where a <c>Version</c> bump would only do so if somebody
/// remembered to make one.
/// </para>
/// <para>
/// The port is declared here and implemented in
/// <c>Backlog.Infrastructure.FileSystem</c>, the arrangement
/// <c>IPullRequestDetailCache</c> and <c>IKnowledgeSnapshotCache</c> already live on
/// either side of and for the same reason: a contract phrased in
/// <see cref="AgentActivityRun"/>s belongs where that type is, while the half that
/// decides where bytes land on disk belongs with the workspace that owns the root.
/// </para>
/// <para>
/// A cache port in a module's published abstractions is worth one sentence of defence,
/// because it looks like an implementation detail escaping. It is not: it sits here on
/// the same footing <see cref="IAgentSessionSource"/> does — a port a host composes,
/// naming no implementation, leaving this project reference-free BCL.
/// </para>
/// <para>
/// Synchronous and non-throwing on purpose. It sits inside a read a person is waiting
/// on; an entry that cannot be read is a miss and an entry that cannot be written is
/// parsed again next time — wasteful, not wrong.
/// </para>
/// </summary>
public interface IAgentActivityCache
{
    /// <summary>What is stored for this file as written at that instant and folded at
    /// that threshold, or null when nothing is — including when what is stored cannot
    /// be read, was written by an older version of this app, describes a different
    /// write of the same file, or was folded at a threshold nobody uses any
    /// more.</summary>
    AgentActivityEntry? TryRead(string path, DateTimeOffset writtenAt, TimeSpan idleAfter);

    /// <summary>Stores one parsed transcript. Failure is not reported, because there
    /// is nothing a caller could usefully do about it.</summary>
    void Write(string path, DateTimeOffset writtenAt, AgentActivityEntry entry);

    /// <summary>Drops everything, so the next read parses every transcript again. The
    /// way back when a figure is not believed.</summary>
    void Forget();
}

/// <summary>
/// One transcript's parsed activity, as the cache holds it: the runs and waits the
/// fold produced over the whole file, before any horizon is applied.
/// <para>
/// Whole-file rather than windowed, because the horizon moves and the file does not.
/// An entry stored against a four-week read has to be usable by a twelve-week one, and
/// clipping is the cheap half of the work.
/// </para>
/// </summary>
public sealed record AgentActivityEntry(
    IReadOnlyList<AgentActivityRun> Runs,
    IReadOnlyList<AgentActivityWait> Waits,
    TimeSpan IdleAfter);
