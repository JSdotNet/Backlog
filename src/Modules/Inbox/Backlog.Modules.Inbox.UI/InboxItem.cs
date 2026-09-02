namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// One thing waiting to be triaged, in the Inbox's own words.
/// <para>
/// This is the published contract the Inbox pane renders and the only shape a
/// supplying context has to produce — see <c>.domain/context-map.md</c>, where
/// Inbox sits upstream of Tasks and Second Brain. Keeping it a
/// plain record is what stops the pane from reaching into a backlog row for a
/// title.
/// </para>
/// </summary>
/// <param name="Key">Opaque identity handed back on open, so the supplying
/// context can find the item again without the Inbox knowing what it is.</param>
/// <param name="Title">What the item says it is, or empty when nobody has said
/// yet.</param>
/// <param name="Area">Where it is filed, or null for unfiled.</param>
public sealed record InboxItem(string Key, string Title, string? Area);
