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
/// <param name="Cards">The cards on the page a search matched, each listed under
/// the page as a link of its own. Null or empty while nothing is searched.</param>
public sealed record SettingsMenuItem(
    string Id,
    string Label,
    string Href,
    bool Current = false,
    string? Badge = null,
    IReadOnlyList<SettingsMenuCardLink>? Cards = null);

/// <summary>
/// A card a settings search matched, listed under its page in the menu: a link
/// to the page that brings the card into view.
/// </summary>
/// <param name="Label">The card's title.</param>
/// <param name="Href">The page's address with the card named in its fragment.</param>
public sealed record SettingsMenuCardLink(string Label, string Href);
