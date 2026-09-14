namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// A reader picked a different state for a technology in the atlas.
///
/// <para>The current status travels with the new one because writing is the
/// panel's, and the panel needs both: the write is a no-op when they match, and
/// the error it reports has to name the control that caused it.</para>
/// </summary>
/// <param name="ItemPath">The node's reference — <c>.tech/&lt;file&gt;.md#&lt;slug&gt;</c>.</param>
/// <param name="CurrentStatus">What the file says now.</param>
/// <param name="Status">What the reader chose.</param>
public sealed record TechnologyStatusChange(string ItemPath, string CurrentStatus, string Status);
