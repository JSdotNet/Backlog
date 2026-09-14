using Backlog.Modules.Knowledge.Abstractions;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// The check every knowledge write makes before it touches a file: the folder
/// resolved, it has a path, and it is one this app may write to.
/// <para>
/// One helper rather than the same three <c>if</c>s at the top of eight methods
/// — the four areas each update and clear a status. That mattered more once
/// editability joined the list: a fourth condition repeated eight times is a
/// fourth condition that gets forgotten in one of them, and the one it is
/// forgotten in silently writes into a branch snapshot.
/// </para>
/// <para>
/// It throws rather than returning a result, which is the habit these callers
/// already had. A panel that offers a status selector it should not have offered
/// is a bug in the panel, not an outcome to render — the panels hide the control
/// instead, and this is the backstop behind them.
/// </para>
/// </summary>
internal static class KnowledgeWriteGuard
{
    /// <summary>The folder path to write into.</summary>
    /// <exception cref="InvalidOperationException">The folder is unavailable, has
    /// no path, or is read-only.</exception>
    public static string WritablePath(this KnowledgeFolderLocation location, string areaLabel)
    {
        ArgumentNullException.ThrowIfNull(location);

        if (!location.Available) throw new InvalidOperationException(location.Message ?? $"{areaLabel} is unavailable.");
        if (location.FullPath is null) throw new InvalidOperationException($"{areaLabel} folder path is unavailable.");

        if (!location.CanEdit)
        {
            throw new InvalidOperationException(
                $"{areaLabel} is read-only because it was read from a branch snapshot. "
                + "Configure a local clone directory for this repository to make changes.");
        }

        return location.FullPath;
    }
}
