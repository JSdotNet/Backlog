using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Modules.Tasks.Services;

/// <summary>
/// Produces the next occurrence of a repeating entry.
/// <para>
/// A domain service rather than a method on the aggregate because spawning an
/// occurrence creates a <em>second</em> aggregate instance, which an aggregate
/// cannot do to itself: the completed entry stays completed and keeps the record
/// of what was done, and what follows is a new entry with its own lifecycle.
/// </para>
/// <para>
/// It is called synchronously by the use case that completes the entry, and
/// deliberately not wired as an event-triggered policy. This context publishes no
/// domain events yet — they are documented in <c>.domain/tasks/domain.md</c> and
/// carried by nothing — and ADR 0006 already rejected putting a mediator behind
/// these handlers. Nothing about the spawn waits on that machinery: what an event
/// would add is a consumer being told, not the successor existing.
/// </para>
/// </summary>
internal static class RecurrencePolicy
{
    /// <summary>
    /// The successor of a completed occurrence.
    /// <para>
    /// What carries over is what the repeat is <em>of</em>: title, content, type,
    /// priority, area, tags, repo ids, dependencies and the recurrence itself,
    /// plus a <see cref="TaskItem.RecurrenceSourceId"/> pointing back at the
    /// occurrence it followed so a series can be traced. What does not carry over
    /// is everything that was about the occurrence rather than the repeat: the new
    /// entry starts at <see cref="EntryStatus.Ready"/> with its sub-items reset to
    /// pending, and with no projections, no usage history, no reminder that has
    /// already fired and no My Day stamp.
    /// </para>
    /// </summary>
    public static TaskItem NextOccurrence(TaskItem completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        if (completed.Recurrence is null)
            throw new InvalidOperationException("Only an entry that carries a recurrence has a next occurrence.");

        var successor = new TaskItem(
            Guid.NewGuid(),
            completed.Title,
            completed.ContentMd,
            completed.Type,
            EntryStatus.Ready,
            completed.Priority,
            completed.RepoIds,
            completed.Tags,
            sourceInboxId: null,
            createdAt: DateTimeOffset.UtcNow,
            recurrenceSourceId: completed.Id);

        successor.SetArea(completed.Area);
        successor.SetRecurrence(completed.Recurrence);
        successor.SetDependsOn(completed.DependsOn);
        successor.SetDueOn(NextDueOn(completed.DueOn, completed.Recurrence));

        // The rank comes across so the next occurrence lands where the person put
        // the last one. Sharing a rank with the entry it followed is fine: equal
        // ranks fall back to recency, which puts the live occurrence above the
        // finished one.
        successor.SetOrder(completed.Order);

        // Sub-items come across as the steps of the repeat, not as the state of
        // the occurrence — a checklist finished last week is the checklist to do
        // again, so AddSubItem's default pending status is exactly right and
        // nothing here copies the done flags.
        foreach (var subItem in completed.SubItems.OrderBy(item => item.Order))
        {
            successor.AddSubItem(subItem.Title, subItem.Notes);
        }

        return successor;
    }

    /// <summary>
    /// The date the repeat next falls due, anchored to <paramref name="anchor"/> —
    /// the completed occurrence's own due date rather than the day it was actually
    /// finished. That is the whole point of anchoring: a weekly entry finished
    /// three days late still falls due on its original weekday, so lateness does
    /// not drift a schedule.
    /// <para>
    /// An occurrence with no due date has no anchor, and the successor gets none
    /// either. Substituting today's date would make the schedule depend on the
    /// moment somebody happened to tick the entry off, which is the drift this
    /// method exists to avoid.
    /// </para>
    /// </summary>
    public static DateOnly? NextDueOn(DateOnly? anchor, Recurrence recurrence)
    {
        ArgumentNullException.ThrowIfNull(recurrence);
        if (anchor is not { } from) return null;

        // A weekday-restricted repeat answers "which of these days comes next",
        // and the interval has nothing to add to that: the grammar cannot express
        // an interval and a weekday set at once, and if one ever reaches here the
        // set is the more specific statement of the two.
        if (recurrence.Weekdays is { Count: > 0 } weekdays)
        {
            var allowed = weekdays.Distinct().ToHashSet();
            var candidate = from.AddDays(1);
            for (var step = 0; step < 7; step++, candidate = candidate.AddDays(1))
            {
                if (allowed.Contains(candidate.DayOfWeek)) return candidate;
            }

            return from.AddDays(1);
        }

        var interval = Math.Max(recurrence.Interval, 1);

        // AddMonths and AddYears clamp to the end of the shorter month, so the
        // 31st repeating monthly lands on the 28th in February and stays a date
        // that exists. Clamping is what the BCL does and what a calendar app does;
        // the alternative is throwing at the end of January.
        return recurrence.Unit switch
        {
            RecurrenceUnit.Day => from.AddDays(interval),
            RecurrenceUnit.Week => from.AddDays(7 * interval),
            RecurrenceUnit.Month => from.AddMonths(interval),
            RecurrenceUnit.Year => from.AddYears(interval),
            _ => from.AddDays(interval)
        };
    }
}
