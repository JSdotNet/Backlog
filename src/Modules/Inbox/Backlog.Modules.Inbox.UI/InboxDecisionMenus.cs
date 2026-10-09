using System.Globalization;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.UI.Components.Menus;

namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// The two menus a decision opens — "Move to list" and "Defer until" — and
/// what choosing from them does. Both the column's detail and triage offer
/// them, and one item can only be filed or put aside one way, so the choices
/// are written here once rather than in each.
/// </summary>
internal static class InboxDecisionMenus
{
    /// <summary>The "Move to list" choice that puts an item back in the unfiled inbox.</summary>
    public const string InboxTarget = "inbox";

    /// <summary>The "Defer until" choice with no review date.</summary>
    public const string DeferUndated = "none";

    /// <summary>Every list but the one the item is in, with its group's name
    /// in front so two lists called "Reading" in two groups can be told apart,
    /// and the inbox itself when the item is filed somewhere.</summary>
    public static IReadOnlyList<MenuItem> MoveTargets(InboxDesktopState state, InboxItemDto item)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(item);

        var items = new List<MenuItem>();

        if (item.ListId is not null)
        {
            items.Add(new MenuItem(InboxTarget, "Inbox", "✉"));
        }

        var lists = state.Lists
            .Where(list => list.Id != item.ListId)
            .OrderBy(list => list.GroupId is { } groupId ? state.FindGroup(groupId)?.Order ?? int.MaxValue : -1)
            .ThenBy(list => list.Order);

        foreach (var list in lists)
        {
            var group = list.GroupId is { } groupId ? state.FindGroup(groupId)?.Name : null;
            var label = group is null ? list.Name : $"{group} / {list.Name}";
            items.Add(new MenuItem(InboxDesktopState.ListNavId(list.Id), label));
        }

        return items;
    }

    /// <summary>The review dates a triage decision reaches for, counted from
    /// <paramref name="today"/>, and "Without a date".</summary>
    public static IReadOnlyList<MenuItem> DeferChoices(DateOnly today)
    {
        return
        [
            DeferChoice("Tomorrow", today.AddDays(1)),
            DeferChoice("Next week", today.AddDays(7)),
            DeferChoice("Next month", today.AddMonths(1)),
            new MenuItem(DeferUndated, "Without a date")
        ];

        static MenuItem DeferChoice(string label, DateOnly date) =>
            new(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                $"{label} · {date.ToString("ddd d MMM", CultureInfo.InvariantCulture)}");
    }

    /// <summary>Files the selected item where <paramref name="target"/> says.</summary>
    public static Task MoveAsync(InboxDesktopState state, MenuItem target)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(target);

        if (target.Id == InboxTarget) return state.MoveToListAsync(null);

        return InboxDesktopState.TryParseListId(target.Id, out var listId)
            ? state.MoveToListAsync(listId)
            : Task.CompletedTask;
    }

    /// <summary>Defers the selected item to the date <paramref name="choice"/>
    /// names, or with none.</summary>
    public static Task DeferAsync(InboxDesktopState state, MenuItem choice)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(choice);

        DateOnly? until = choice.Id != DeferUndated
            && DateOnly.TryParseExact(choice.Id, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;

        return state.DeferAsync(until);
    }
}
