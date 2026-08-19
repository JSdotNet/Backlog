namespace Backlog.UI.Components.Data;

/// <summary>
/// One column of a <c>DataTable</c>: its heading, and any class the heading cell
/// should carry.
/// <para>
/// The class is on the column rather than only on the cells because a heading is
/// the one cell the caller does not write — the row template writes the
/// <c>td</c>s, the component writes the <c>th</c>s — so without this there is no
/// way to make a heading agree with the column under it.
/// </para>
/// </summary>
public sealed record DataTableColumn(string Header, string? CssClass = null);

/// <summary>
/// One section of a <c>DataTable</c>.
/// <para>
/// <c>Name</c> is null for a table that is not grouped, which is what lets the
/// component render sections unconditionally: an ungrouped table is one nameless
/// section, not a different shape, so nothing downstream has to branch on whether
/// grouping is on.
/// </para>
/// <para>
/// Sections and rows render in the order given. Sorting and grouping are the
/// caller's, because the useful order depends on the question being asked, and a
/// component that re-sorted what it was handed would quietly disagree with the
/// control the reader used to ask for that order.
/// </para>
/// </summary>
public sealed record DataTableSection<TItem>(string? Name, IReadOnlyList<TItem> Items);
