using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Devbook.Chapters;

/// <summary>
/// The check every knowledge write makes before it touches a file: the folder
/// resolved, it has a path, and it is one this app may write to.
/// <para>
/// One helper rather than the same three <c>if</c>s at the top of every store
/// method that writes a file — a rating, a sync direction. That mattered more once
/// editability joined the list: a fourth condition repeated in each is a fourth
/// condition that gets forgotten in one of them, and the one it is forgotten in
/// silently writes into a branch snapshot.
/// </para>
/// <para>
/// It throws rather than returning a result, which is the habit these callers
/// already had. A panel that offers a status selector it should not have offered
/// is a bug in the panel, not an outcome to render — the panels hide the control
/// instead, and this is the backstop behind them.
/// </para>
/// </summary>
internal static class DevbookWriteGuard
{
    /// <summary>The folder path to write into.</summary>
    /// <exception cref="InvalidOperationException">The folder is unavailable, has
    /// no path, or is read-only.</exception>
    public static string WritablePath(this DevbookFolderLocation location, string areaLabel)
    {
        ArgumentNullException.ThrowIfNull(location);

        if (!location.Available) throw new InvalidOperationException(location.Message ?? $"{areaLabel} is unavailable.");
        if (location.FullPath is null) throw new InvalidOperationException($"{areaLabel} folder path is unavailable.");

        location.EnsureNotArchived(areaLabel);

        if (!location.CanEdit)
        {
            throw new InvalidOperationException(
                $"{areaLabel} is read-only because it was read from a branch snapshot. "
                + "Configure a local clone directory for this repository to make changes.");
        }

        return location.FullPath;
    }

    /// <summary>The archived half of <see cref="WritablePath"/>.</summary>
    /// <exception cref="InvalidOperationException">The repository is
    /// archived.</exception>
    public static void EnsureNotArchived(this DevbookFolderLocation location, string areaLabel)
    {
        ArgumentNullException.ThrowIfNull(location);

        if (location.RepositoryArchived) throw new InvalidOperationException(ArchivedMessage(areaLabel));
    }

    /// <summary>
    /// What an archived repository's refusal says — thrown by a file write here,
    /// and answered as an outcome by a write which changes no file: a subject's
    /// state, kept in the subject store.
    /// <para>
    /// A branch snapshot refuses a file write because the next fetch would discard
    /// it; a state in the store is never in the snapshot, so that refusal does not
    /// apply to it, as it never did to a remark. An archived repository is
    /// read-only all the way through (local ADR 0029 §4) — the store replicates,
    /// and an archived repository's state must not move on any device — so this
    /// one does, in the same words.
    /// </para>
    /// </summary>
    public static string ArchivedMessage(string areaLabel) =>
        $"{areaLabel} is read-only because its repository is archived. Unarchive it in Settings to make changes.";
}
