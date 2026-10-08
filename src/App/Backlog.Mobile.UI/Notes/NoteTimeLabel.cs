using System.Globalization;

namespace Backlog.Mobile.UI.Notes;

/// <summary>
/// When a note last changed, as a row on the Notes tab says it: the clock for
/// today, "Yesterday", the weekday for the rest of the past week, then the date —
/// with its year once it is not this year's. The fewest words that still place
/// the note, read against the phone's own day.
/// </summary>
public static class NoteTimeLabel
{
    /// <summary>Both times in the phone's own zone, so "today" is the day the
    /// person is living.</summary>
    public static string For(DateTimeOffset at, DateTimeOffset now)
    {
        var day = DateOnly.FromDateTime(at.DateTime);
        var today = DateOnly.FromDateTime(now.DateTime);
        var daysAgo = today.DayNumber - day.DayNumber;

        // A stamp ahead of this phone's clock — another device's clock runs
        // fast — is still today's rather than a date in the future.
        if (daysAgo <= 0) return at.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (daysAgo == 1) return "Yesterday";
        if (daysAgo < 7) return at.ToString("ddd", CultureInfo.InvariantCulture);

        return day.Year == today.Year
            ? at.ToString("d MMM", CultureInfo.InvariantCulture)
            : at.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }
}
