namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// A folder change whose disk work is done and whose repoint is still to come:
/// what <see cref="WorkspaceSettingsStore.PrepareMoveRoot"/> and
/// <see cref="WorkspaceSettingsStore.PrepareUseRoot"/> hand to
/// <see cref="WorkspaceSettingsStore.CommitRoot"/>.
/// <para>
/// Two steps rather than one call because they belong on different threads. The
/// copy can take as long as the backlog is big and touches nothing the app reads,
/// so it runs off the UI thread; the repoint raises
/// <see cref="WorkspaceSettingsStore.RootChanged"/>, whose listeners replace
/// state a renderer reads, so it runs where they expect to be called.
/// </para>
/// </summary>
/// <param name="Source">The folder the app was in when the preparation began.</param>
/// <param name="Target">The folder to point at, or null when there is nothing to
/// repoint - a refusal, or the folder the app is already in.</param>
/// <param name="Outcome">What the change comes to once committed: a refusal as
/// it stands, or the success the repoint completes.</param>
public sealed record PreparedRoot(string Source, string? Target, RootMove Outcome)
{
    /// <summary>A preparation with nothing left to commit.</summary>
    internal static PreparedRoot Done(string source, RootMove outcome) => new(source, null, outcome);

    /// <summary>The source's fingerprint from before the copy, for a move: the
    /// commit refuses when the source has been written since.</summary>
    internal WorkspaceSettingsStore.SourceMarker? Marker { get; init; }

    /// <summary>What the copy created in the target, in creation order, so a
    /// refused commit can take it back out.</summary>
    internal IReadOnlyList<string> Written { get; init; } = [];
}
