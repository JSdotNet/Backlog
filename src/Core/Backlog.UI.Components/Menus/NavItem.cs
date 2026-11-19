using Microsoft.AspNetCore.Components;

namespace Backlog.UI.Components.Menus;

/// <summary>One link in a <c>NavList</c> or a <c>TabBar</c>. Match mirrors NavLink's
/// own matching. Icon is an optional decorative glyph a <c>TabBar</c> draws above the
/// label; the label stays the link's name, so the glyph must be hidden from assistive
/// technology, as every glyph in <c>Icons</c> is by default. <c>NavList</c> draws
/// none. IconOnly makes a <c>TabBar</c> draw the glyph alone: the label is still the
/// link's name, kept for assistive technology and the tooltip but visually hidden. It
/// takes effect only with an Icon; without one the label shows as usual.</summary>
public sealed record NavItem(string Href, string Label, bool Match = false, RenderFragment? Icon = null, bool IconOnly = false);
