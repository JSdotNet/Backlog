namespace Backlog.Modules.Tasks.Abstractions;

/// <summary>
/// The shape of a repeat: how many <see cref="Unit"/>s apart the occurrences are,
/// and optionally the weekdays the repeat is restricted to. "Every weekday" is
/// interval 1, unit <see cref="RecurrenceUnit.Week"/>, weekdays Monday through
/// Friday; "every other week" is interval 2 with no weekday restriction.
/// <para>
/// It describes the shape only and deliberately says nothing about when the next
/// occurrence falls — that is a calculation from the entry's due date, and
/// keeping it out of here is what lets a repeat be read, written and compared
/// without a calendar in hand.
/// </para>
/// <para>
/// This lives in Abstractions rather than beside the aggregate because the parser
/// produces one, the DTO publishes one and the aggregate holds one. A value
/// object all three have to name is part of the published language.
/// </para>
/// </summary>
public sealed record Recurrence(int Interval, RecurrenceUnit Unit, IReadOnlyList<DayOfWeek>? Weekdays = null)
{
    /// <summary>
    /// Two repeats of the same shape are the same repeat. The compiler-generated
    /// equality would compare <see cref="Weekdays"/> by reference, which would
    /// make "every weekday" unequal to "every weekday" for no reason a reader of
    /// the model could name — the domain says equality here is by value, so the
    /// weekday set is compared as a set.
    /// </summary>
    public bool Equals(Recurrence? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Interval != other.Interval || Unit != other.Unit) return false;
        if (Weekdays is null || other.Weekdays is null) return Weekdays is null && other.Weekdays is null;

        return Weekdays.OrderBy(day => day).SequenceEqual(other.Weekdays.OrderBy(day => day));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Interval);
        hash.Add(Unit);
        foreach (var day in (Weekdays ?? []).OrderBy(day => day))
        {
            hash.Add(day);
        }

        return hash.ToHashCode();
    }
}

/// <summary>The period a repeat is counted in.</summary>
public enum RecurrenceUnit
{
    Day,
    Week,
    Month,
    Year
}
