namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// What one pass over a transcript established about it: the folder it ran in, the
/// branch it was on, and how many turns the person took.
/// <para>
/// <see cref="Turns"/> is null where the transcript stated nothing countable, on the
/// same terms <see cref="AgentSession.TurnCount"/> sets — a transcript holding only
/// queued prompts is a real thing. It is never null here because the read was
/// interrupted; an interrupted read is not written.
/// </para>
/// </summary>
public sealed record TranscriptFacts(string Folder, string? Branch, int? Turns)
{
    /// <summary>Where the session was run from — <c>claude-desktop</c>, <c>cli</c> —
    /// as the transcript's first line naming it spelled it, or null.</summary>
    public string? Entrypoint { get; init; }

    /// <summary>The pull requests the session linked, oldest first. Empty where it
    /// linked none.</summary>
    public IReadOnlyList<AgentPullRequest> PullRequests { get; init; } = [];

    /// <summary>The tokens the session spent per model. Empty where it answered nothing
    /// with a real model.</summary>
    public IReadOnlyList<AgentModelUsage> ModelUsage { get; init; } = [];
}

/// <summary>
/// Keeps what a full pass over a transcript established on disk, keyed on the file,
/// its length and the moment it was last written, so a session read opens only the
/// transcripts that have changed.
/// <para>
/// The sibling of <see cref="IAgentActivityCache"/>, for the other pass the session
/// surface makes over the same files. That one remembers when an agent was
/// producing; this one remembers the three facts the session <em>list</em> reads
/// out of a transcript, of which the turn count is the one that cannot be had from
/// any bounded prefix of the file. The list read was measured at about two hundred
/// milliseconds over a hundred transcripts and sixty-odd megabytes, on pane open and
/// on every refresh, and the reader's own note names this as the cheap way out: a
/// session that has not been written to since the last read cannot have taken
/// another turn.
/// </para>
/// <para>
/// Length is part of the key beside the write time, and the pair is stricter than
/// either alone. A transcript is appended to while its session runs, so its length
/// and its write time both move; a key on the write time alone would trust a
/// file-system with coarse timestamps to have noticed an append in the same
/// second, and a key on the length alone would trust that a rewrite kept the file
/// a different size. A finished transcript matches on both forever.
/// </para>
/// <para>
/// Declared here and implemented in <c>Backlog.Infrastructure.FileSystem</c>, on
/// the arrangement <see cref="IAgentActivityCache"/> defends one paragraph at a
/// time: a port a host composes, naming no implementation.
/// </para>
/// <para>
/// Synchronous and non-throwing: it sits inside a read a person is waiting on, and
/// an entry that cannot be read is a miss while one that cannot be written is
/// read again next time.
/// </para>
/// </summary>
public interface ITranscriptFactsCache
{
    /// <summary>What is stored for this file at this length and write time, or null
    /// when nothing is — including when what is stored was written for a different
    /// length or write time, cannot be read, or came from an older version of this
    /// app.</summary>
    TranscriptFacts? TryRead(string path, long length, DateTimeOffset writtenAt);

    /// <summary>Stores the facts a complete pass over the file established. Failure
    /// is not reported, because there is nothing a caller could usefully do about
    /// it.</summary>
    void Write(string path, long length, DateTimeOffset writtenAt, TranscriptFacts facts);

    /// <summary>Drops every stored entry, so the next read opens every transcript
    /// again.</summary>
    void Forget();
}
