using Backlog.UI.Components.Devbook;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The screen's half of <see cref="DevbookSyncReading"/>. The reading moved into
/// Backlog.Infrastructure.Devbook with the writer it is built on (issue #738), and
/// the one member that built a UI type stayed here, so the adapter names no
/// component library type and the panels' <c>sync.Scope(...)</c> calls read as they
/// did.
/// </summary>
public static class DevbookSyncReadingScopes
{
    /// <summary>
    /// The scope a pane cascades over the open document: the reading answers what
    /// each record asks, and <paramref name="write"/> — null where the folder cannot
    /// be written — is handed the item path a status write would use, the bare
    /// document path for its own block and <c>path#slug</c> for a chapter, with the
    /// direction or null to inherit.
    /// </summary>
    public static DevbookSyncScope Scope(this DevbookSyncReading reading, string documentPath, Func<string, string?, Task>? write)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return new(reading.Describe, write is null
            ? null
            : (level, heading, direction) => write(reading.ItemPath(documentPath, level, heading), direction));
    }
}
