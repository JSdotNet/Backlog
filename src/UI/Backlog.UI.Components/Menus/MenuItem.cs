namespace Backlog.UI.Components.Menus;

/// <summary>
/// One row of a menu. A separator carries no label and cannot be activated, so
/// it is the same record with <see cref="Separator"/> set rather than a second
/// type the caller has to reason about.
/// </summary>
public sealed record MenuItem(
    string Id,
    string Label,
    string? Icon = null,
    bool Disabled = false,
    bool Destructive = false,
    bool Separator = false)
{
    public static MenuItem Divider(string id) => new(id, string.Empty, Separator: true);
}
