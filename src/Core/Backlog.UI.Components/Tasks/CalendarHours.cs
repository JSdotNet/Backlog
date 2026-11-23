using System.Globalization;

namespace Backlog.UI.Components.Tasks;

/// <summary>The hours arithmetic and wording, apart from the markup so it can be
/// pinned down on its own.</summary>
public static class CalendarHours
{
    /// <summary>The most one block can hold: the whole day.</summary>
    public const decimal MaxHours = 24m;

    /// <summary>The figures an hours field suggests: the blocks a working day is
    /// usually cut into. Suggestions only — any figure <see cref="Parse"/> keeps can be
    /// typed.</summary>
    public static IReadOnlyList<decimal> Presets { get; } = [0.5m, 1m, 2m, 3m, 4m, 6m, 8m];

    /// <summary>The presets as a <see cref="Inputs.Combobox"/>'s options: the field takes
    /// the invariant figure <see cref="Edit"/> writes, and the list reads "0.5h" in the
    /// reader's culture at the time of the call.</summary>
    public static IReadOnlyList<Selects.SelectorOption> PresetOptions() =>
        [.. Presets.Select(hours => new Selects.SelectorOption(Edit(hours), Format(hours)))];

    /// <summary>"3h", "2.5h", "0.75h" — the shortest figure that is exact, in the
    /// reader's culture.</summary>
    public static string Format(decimal hours) =>
        string.Create(CultureInfo.CurrentCulture, $"{hours.ToString("0.##", CultureInfo.CurrentCulture)}h");

    /// <summary>The figure an hours field starts from: "3", "2.5". Invariant, because a
    /// number field's value is written with a point whatever the reader's culture, and
    /// one written with a comma reads as empty.</summary>
    public static string Edit(decimal hours) => hours.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// The hours a typed figure means, or null when it means none to keep: not a number,
    /// zero or less, or more than <see cref="MaxHours"/>. Read invariant first — a number
    /// field hands its value over with a point whatever the reader's culture — and then
    /// in the reader's culture, so "2.5" and, in a culture that writes it so, "2,5" are
    /// both two and a half. No group separators, so "1.5" is never fifteen anywhere. An
    /// "h" after the number is allowed. Rounded to quarters of an hour, as the task keeps
    /// it.
    /// </summary>
    public static decimal? Parse(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;

        var text = typed.Trim();
        if (text.EndsWith('h') || text.EndsWith('H')) text = text[..^1].TrimEnd();

        const NumberStyles figure = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;
        if (!decimal.TryParse(text, figure, CultureInfo.InvariantCulture, out var hours)
            && !decimal.TryParse(text, figure, CultureInfo.CurrentCulture, out hours))
        {
            return null;
        }

        // Quarters of an hour, as the task keeps them, so a figure that would round to
        // nothing is refused here rather than by the store.
        var quarters = Math.Round(hours * 4m, MidpointRounding.AwayFromZero) / 4m;
        return quarters is > 0m and <= MaxHours ? quarters : null;
    }
}

/// <summary>One block of a task's planned hours as the detail panel lists it: the day
/// and how many hours.</summary>
public sealed record PlannedHoursDay(DateOnly On, decimal Hours);

/// <summary>Hours set or changed on a day, or — for a null <see cref="Hours"/> — the
/// day's block taken away.</summary>
public sealed record PlannedHoursDayChange(DateOnly On, decimal? Hours);
