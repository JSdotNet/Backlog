namespace Backlog.UI.Components.Tasks;

/// <summary>
/// One column of a <see cref="TaskBoard"/>, as the host has already grouped it.
/// <para>
/// The board does not group. Which columns exist, what they are called and which
/// cards sit in each is the host's vocabulary — a status, a plan, a repository —
/// and the board only lays them out and adds them up. That keeps the component
/// free of any one product's lifecycle, the way <see cref="TaskCard"/> is free of
/// its priority words.
/// </para>
/// </summary>
/// <param name="Key">Unique on the board, and what a drop and an add report.</param>
/// <param name="Title">The heading the column is read by.</param>
/// <param name="Cards">The cards in the column, in the order they are drawn.</param>
/// <param name="DotCssClass">A class for the small mark before the heading — the
/// host's colour for a status or a priority. Null draws the neutral dot.</param>
/// <param name="CanAdd">Whether the column ends in a "+ New entry" control that
/// creates an entry in it.</param>
public sealed record TaskBoardColumn(
    string Key,
    string Title,
    IReadOnlyList<TaskBoardCard> Cards,
    string? DotCssClass = null,
    bool CanAdd = false);

/// <summary>
/// One card on a <see cref="TaskBoard"/>: the <see cref="TaskRow"/> a list draws,
/// plus the facts <see cref="TaskCard"/> takes beside it.
/// </summary>
public sealed record TaskBoardCard(
    TaskRow Task,
    TaskChainStatus? Status = null,
    string? Priority = null,
    string? Repository = null,
    int? Effort = null,
    bool Overdue = false,
    string? Finished = null,
    string? CssClass = null);

/// <summary>
/// What dropping a card on a column would do, as the host's rule answers it.
/// </summary>
/// <param name="Allowed">Whether the drop is written.</param>
/// <param name="Text">For an allowed drop, what it does ("Drop to start"); for a
/// refused one, why not ("Ready can't move straight to Done — start it
/// first"). Drawn in the column while the card is held over the board.</param>
/// <param name="Detail">For an allowed drop, an optional second line under
/// <paramref name="Text"/> saying what else the drop writes ("Moves to In progress
/// and stamps Started"). Null draws the one line. Ignored on a refused drop, whose
/// <paramref name="Text"/> already is the whole reason.</param>
public sealed record TaskBoardDropRule(bool Allowed, string Text, string? Detail = null);

/// <summary>A card dropped on a column the host's rule allowed.</summary>
public sealed record TaskBoardDrop(string TaskId, string ColumnKey);
