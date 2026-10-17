using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Progress;

/// <summary>
/// Answers Progress's <see cref="IIterationSettings"/> from the device's
/// <see cref="ShellNavigationStore"/>, on the terms <see cref="ToolsViewPreferences"/>
/// answers the tools pane's: the length as the setting's own values —
/// <c>day</c>, <c>week</c>, <c>two-weeks</c> and <c>month</c>
/// (<c>.devbook/domain/progress/context.md#iteration-length</c>) — and the two-week start as
/// an ISO date. A value it does not know reads as Week, and a start that is no date as none.
/// <para>
/// Both choices are held as one snapshot, replaced whole on every change, so a reader on
/// another thread never sees a length from one change beside a start from another.
/// A host that composed no store still resolves the port: the choices are then held for the
/// life of this instance and forgotten on restart.
/// </para>
/// </summary>
public sealed class IterationSettings : IIterationSettings
{
    private readonly ShellNavigationStore? _store;
    private readonly Lock _gate = new();
    private volatile Snapshot _current;

    public IterationSettings(ShellNavigationStore? store)
    {
        _store = store;
        _current = store is null
            ? new Snapshot(IterationLength.Week, null)
            : new Snapshot(Parse(store.GoalsIterationLength), store.GoalsTwoWeekStart);
    }

    public event Action? Changed;

    public IterationLength Length => _current.Length;

    public DateOnly? TwoWeekStart => _current.TwoWeekStart;

    public void SetLength(IterationLength length, DateOnly today)
    {
        if (!Enum.IsDefined(length)) throw new ArgumentOutOfRangeException(nameof(length), length, "An iteration is a day, a week, two weeks or a month.");

        Change(current => current with
        {
            Length = length,
            TwoWeekStart = length == IterationLength.TwoWeeks && current.TwoWeekStart is null
                ? Iterations.MondayOf(today)
                : current.TwoWeekStart
        });
    }

    public void SetTwoWeekStart(DateOnly start) => Change(current => current with { TwoWeekStart = start });

    /// <summary>The value <paramref name="length"/> is stored as.</summary>
    public static string ToStored(IterationLength length) => length switch
    {
        IterationLength.Day => "day",
        IterationLength.Week => "week",
        IterationLength.TwoWeeks => "two-weeks",
        IterationLength.Month => "month",
        _ => throw new ArgumentOutOfRangeException(nameof(length), length, "An iteration is a day, a week, two weeks or a month.")
    };

    /// <summary>The stored value as a length: one of the setting's values, or — for a file
    /// written by an earlier build — an <see cref="IterationLength"/> name, both compared
    /// without regard to case. Anything else, a number included, reads as Week.</summary>
    public static IterationLength Parse(string? stored)
    {
        var value = stored?.Trim();
        if (string.IsNullOrEmpty(value)) return IterationLength.Week;

        foreach (var length in Enum.GetValues<IterationLength>())
        {
            if (string.Equals(value, ToStored(length), StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, length.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return length;
            }
        }

        return IterationLength.Week;
    }

    /// <summary>Applies one change under the gate, writing the store once, and raises
    /// <see cref="Changed"/> outside it when something moved.</summary>
    private void Change(Func<Snapshot, Snapshot> change)
    {
        bool changed;
        lock (_gate)
        {
            var before = _current;
            var after = change(before);
            changed = after != before;
            if (changed)
            {
                _store?.SetGoalsIteration(ToStored(after.Length), after.TwoWeekStart);
                _current = after;
            }
        }

        if (changed) Changed?.Invoke();
    }

    private sealed record Snapshot(IterationLength Length, DateOnly? TwoWeekStart);
}
