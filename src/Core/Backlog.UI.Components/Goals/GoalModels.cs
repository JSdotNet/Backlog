using System.Globalization;

namespace Backlog.UI.Components.Goals;

/// <summary>
/// One goal the <see cref="GoalPicker"/> offers, in plain values: the host reads its
/// goals and their counts, the picker lists them and hands back <see cref="Id"/>.
/// </summary>
/// <param name="Done">Items of the goal done.</param>
/// <param name="Total">Items the goal holds.</param>
/// <param name="DueDate">The day the goal should be reached by; null for none, which is due
/// at the end of the iteration.</param>
public sealed record GoalOption(Guid Id, string Title, int Done, int Total, DateOnly? DueDate)
{
    /// <summary>The quiet line under the title: <c>3/8 · due 14 Nov</c>, or
    /// <c>3/8 · due end of iteration</c> without a due date — the picker reads no iteration,
    /// so it names no day.</summary>
    public string Meta => string.Create(
        CultureInfo.InvariantCulture,
        $"{Done}/{Total} · due {(DueDate is { } due ? due.ToString("d MMM", CultureInfo.InvariantCulture) : "end of iteration")}");
}

/// <summary>A goal set from the picker's "New goal…": its title and due date, or null for
/// none. The host creates it and attaches the subject to it.</summary>
public sealed record GoalDraft(string Title, DateOnly? DueDate);
