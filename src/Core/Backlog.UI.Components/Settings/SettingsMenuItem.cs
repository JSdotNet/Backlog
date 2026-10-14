namespace Backlog.UI.Components.Settings;

/// <summary>
/// One page in a <see cref="SettingsMenuGroup"/>: a link to the page's own
/// address, so choosing it navigates and Back returns to the page before.
/// </summary>
/// <param name="Id">The page's id. Written onto the link as <c>data-page</c>, so a
/// host or a test can find the link without reading its label.</param>
/// <param name="Label">The words on the link.</param>
/// <param name="Href">The page's address, as the host routes it.</param>
/// <param name="Current">Whether this is the page on screen. The current link
/// carries <c>aria-current="page"</c>.</param>
/// <param name="Badge">A short value shown on the right of the item - a count or
/// a state. Null or blank draws nothing, not an empty pill.</param>
public sealed record SettingsMenuItem(string Id, string Label, string Href, bool Current = false, string? Badge = null);
