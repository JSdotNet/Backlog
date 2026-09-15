namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// What came of asking <see cref="WorkspaceSettingsStore.TryMoveRoot"/> to take
/// the backlog to another folder.
/// <para>
/// Not a plain error string like the store's other answers, because the screen
/// has two true sentences to choose between when it succeeds: the backlog was
/// copied and the old folder still has its copy, or there was nothing yet to
/// copy and the app was simply pointed. Telling somebody their old folder keeps
/// a copy it does not have would be its own small lie.
/// </para>
/// <para>
/// <see cref="Error"/> can accompany a success: the move happened but the
/// choice could not be saved for next launch, which is worth a sentence and is
/// not undone by anything.
/// </para>
/// </summary>
public sealed record RootMove(bool Moved, bool CopiedData, string? Error)
{
    /// <summary>The database and the rest of the folder were copied and the app now reads
    /// from the new folder; the old folder was left as it was.</summary>
    public static RootMove Copied { get; } = new(true, true, null);

    /// <summary>There was no backlog yet to carry, so the app was pointed at the
    /// folder and nothing was copied.</summary>
    public static RootMove Pointed { get; } = new(true, false, null);

    /// <summary>Nothing changed, for the reason given.</summary>
    public static RootMove Failed(string error) => new(false, false, error);
}
