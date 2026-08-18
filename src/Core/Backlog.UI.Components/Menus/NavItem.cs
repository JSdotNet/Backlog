namespace Backlog.UI.Components.Menus;

/// <summary>One link in a <c>NavList</c>. Match mirrors NavLink's own matching.</summary>
public sealed record NavItem(string Href, string Label, bool Match = false);
