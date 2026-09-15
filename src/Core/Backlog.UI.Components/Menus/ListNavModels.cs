namespace Backlog.UI.Components.Menus;

/// <summary>
/// One row of a <see cref="ListNav"/>: a list the reader can open, and how much
/// is in it. Callers keep their own richer type and map onto this, the way they
/// do for <see cref="TreeNode"/>, so the navigation stays free of any one app's
/// vocabulary; <see cref="Id"/> is how they find the original again.
/// </summary>
/// <param name="Id">Unique across every entry <em>and</em> every group in one
/// navigation. The two share a namespace because selection, rename and the
/// keyboard all address a row by id alone, and a group and a list with the same
/// id would be one row the component could not tell apart.</param>
/// <param name="Count">Always drawn, even at zero. A count that disappears when
/// it is nothing makes a row with no badge ambiguous — empty, or uncounted? —
/// and the stylesheet can quieten a zero without the markup hiding it.</param>
/// <param name="Icon">A glyph before the label, or nothing. Text rather than a
/// component, on <see cref="MenuItem"/>'s terms: what glyph a list wears is the
/// host's vocabulary, and the navigation only places it.</param>
public sealed record ListNavEntry(string Id, string Label, int Count, string? Icon = null);

/// <summary>A fold of entries under one heading.</summary>
public sealed record ListNavGroup(string Id, string Label, IReadOnlyList<ListNavEntry> Entries);

/// <summary>What a context menu was asked for on: the empty area of the
/// navigation, a group's heading, or one entry.</summary>
public enum ListNavTargetKind
{
    Root,
    Group,
    Entry
}

/// <summary>Where a context menu was opened. <see cref="Id"/> is null for the
/// root, which has no id to give, and the group's or entry's otherwise.</summary>
public sealed record ListNavTarget(ListNavTargetKind Kind, string? Id)
{
    public static ListNavTarget Root { get; } = new(ListNavTargetKind.Root, null);

    public static ListNavTarget Group(string id) => new(ListNavTargetKind.Group, id);

    public static ListNavTarget Entry(string id) => new(ListNavTargetKind.Entry, id);
}
