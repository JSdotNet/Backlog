namespace Backlog.Modules.Tasks.Abstractions;

/// <summary>
/// Whether a task is in the reader's My Day — the one rule the desktop and the phone
/// both read (local ADR 0032, §5; <c>.devbook/domain/tasks/domain.md#my-day</c>).
/// <para>
/// A task is in today's My Day while its <c>myday:</c> stamp is today. With Clear My
/// Day overnight off, a task with an earlier stamp carries over until it is finished:
/// it stays while unfinished, and on the day it is finished it stays, ticked. A stamp
/// for a later day is never in today's list, and neither is a task with no stamp.
/// </para>
/// <para>
/// Read time only. The switch changes how a stamp is read and never writes one, so
/// turning it back on restores the overnight list at once. Pure and clock-free: the
/// caller says what today is, so the two heads agree whenever they agree on the date.
/// </para>
/// </summary>
public static class MyDay
{
    /// <summary>Whether a task stamped <paramref name="stamp"/>, finished on
    /// <paramref name="completedOn"/> (<c>null</c> while unfinished), is in the My Day
    /// of <paramref name="today"/>.</summary>
    /// <param name="clearsOvernight">The person's Clear My Day overnight switch; on
    /// is the default and today's rule.</param>
    public static bool Includes(DateOnly? stamp, DateOnly? completedOn, DateOnly today, bool clearsOvernight)
    {
        if (stamp is not { } day) return false;
        if (day == today) return true;
        if (day > today || clearsOvernight) return false;

        // Carried over: in while unfinished, and ticked on the day it was finished.
        return completedOn is null || completedOn == today;
    }

    /// <summary>
    /// Whether the task was planned for <paramref name="today"/> itself — the stamp is
    /// today, not carried from an earlier day. An agenda slot belongs to the stamp's
    /// day (<c>.devbook/domain/tasks/domain.md#agenda-time</c>), so only such a task
    /// shows one; a carried task is in My Day without a slot.
    /// </summary>
    public static bool IsPlannedFor(DateOnly? stamp, DateOnly today) => stamp == today;
}
