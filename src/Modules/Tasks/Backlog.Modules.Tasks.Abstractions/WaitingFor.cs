namespace Backlog.Modules.Tasks.Abstractions;

/// <summary>
/// Who a task waits on, and when to chase them: its waiting-for
/// (<c>.devbook/domain/tasks/domain.md#waiting-for</c>, local ADR 0054 §4).
/// <para>
/// <see cref="Who"/> is free text naming a person or a party, and
/// <see cref="FollowUpOn"/> an optional calendar day. Entry text writes them as
/// <c>waiting:&lt;who&gt;</c> and <c>followup:&lt;YYYY-MM-DD&gt;</c>; a follow-up
/// date has no meaning without someone to follow up with, so there is no
/// waiting-for made of a date alone.
/// </para>
/// <para>
/// Not an Impediment, and it raises none. In Abstractions rather than beside the
/// aggregate for the reason <see cref="AgendaTime"/> is: the parser produces one,
/// the DTO publishes one and the aggregate holds one.
/// </para>
/// </summary>
public sealed record WaitingFor
{
    /// <param name="who">Who the task waits on. Trimmed; blank is refused, because a
    /// waiting-for that names nobody is no waiting-for — clearing is null.</param>
    /// <param name="followUpOn">The day to chase them, or null for no date.</param>
    public WaitingFor(string who, DateOnly? followUpOn = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(who);

        // A backtick would end the token it is written in, so it can never have
        // come from the text and is refused rather than written into a line it
        // would break.
        if (who.Contains('`', StringComparison.Ordinal))
        {
            throw new ArgumentException("Who a task waits on cannot contain a backtick.", nameof(who));
        }

        Who = who.Trim().ReplaceLineEndings(" ");
        FollowUpOn = followUpOn;
    }

    /// <summary>Who the task waits on, as written.</summary>
    public string Who { get; }

    /// <summary>The day to follow up, or null when none was set.</summary>
    public DateOnly? FollowUpOn { get; }

    /// <summary>A waiting-for from its two stored values, or null when no one is
    /// named — a blank or missing <paramref name="who"/> is no waiting-for, whatever
    /// date sits beside it. What a store, a replica document and a picker read
    /// through, so a hand-edited value costs that value rather than the task.</summary>
    public static WaitingFor? From(string? who, DateOnly? followUpOn) =>
        string.IsNullOrWhiteSpace(who) || who.Contains('`', StringComparison.Ordinal)
            ? null
            : new WaitingFor(who, followUpOn);

    /// <summary>Whether the follow-up date has passed on <paramref name="today"/>: the
    /// day after it, and every day since. Read by comparison, the way a passed
    /// reminder reads overdue, so nothing has to sweep the backlog. A waiting-for
    /// with no date is never overdue.</summary>
    public bool IsFollowUpOverdue(DateOnly today) => FollowUpOn is { } followUp && followUp < today;
}
