namespace Backlog.Modules.Dashboard.Abstractions.Insights;

/// <summary>
/// One row of the sessions breakdown — a machine when every machine is in view, an
/// assistant when one machine is focused.
/// <para>
/// One row type for both, because the two are the same four measures under a different
/// heading, and two records would only mean two places for the arithmetic to drift.
/// The part names the column; the row carries the name.
/// </para>
/// </summary>
/// <param name="Key">
/// What makes this row one row: the machine's stable id, or the assistant's label once a
/// machine is focused. Beside <paramref name="Name"/> rather than instead of it, and for
/// the reason the whole feature exists — two machines can share a name, and a table keyed
/// on the name would hand a rendering component two siblings with one key. What that
/// costs is not a merged row but a dead surface, because Blazor's keyed diff throws.
/// </param>
/// <param name="Name">The machine or the assistant this row is about.</param>
/// <param name="Sessions">How many sessions the row covers.</param>
/// <param name="ActiveTime">How long an agent was producing in this row's sessions,
/// clipped to the window. Not how long the sessions were open: a session that sat idle
/// overnight contributes the minutes it worked and nothing else. Concurrent sessions in
/// the row are summed rather than merged, so this can exceed the window.</param>
/// <param name="LastActivityAt">The most recent moment anything moved in this row.</param>
public sealed record AssistantSessionRow(
    string Key,
    string Name,
    int Sessions,
    TimeSpan ActiveTime,
    DateTimeOffset? LastActivityAt)
{
    /// <summary>
    /// Time in this row's sessions in which the agent had stopped and the next thing to
    /// happen was a person.
    /// <para>
    /// Zero for a row whose assistant records no prompt boundary, and that is a limit of
    /// what that assistant writes down rather than a claim that nobody waited. A zero
    /// here beside a real figure on the row above is where a reader actually meets that
    /// limitation, which is why the column exists rather than the total alone.
    /// </para>
    /// <para>
    /// Beside the primary constructor rather than in it, on
    /// <see cref="AssistantSessionsInsight.SessionsPerWeek"/>'s precedent.
    /// </para>
    /// </summary>
    public TimeSpan Waiting { get; init; }
}

/// <summary>
/// What the assistants have been doing on the scoped machines, over the scoped window.
/// <para>
/// Every figure here is a floor rather than a total whenever <see cref="Capped"/> is
/// true or <see cref="Unreadable"/> is non-empty, and both travel with the figures for
/// exactly that reason: a capped number presented as a whole one is how a dashboard
/// quietly stops being trusted. <see cref="WithoutActivity"/> is the third of the same
/// kind — a session that left nothing to parse is real and is counted, but it cannot
/// contribute a duration, and saying so is the difference between an understated figure
/// and a dishonest one.
/// </para>
/// <para>
/// One thing on here does not follow the window: <see cref="ActivityByHour"/> is always
/// the last seven dated days, whichever period the reader selected. Its own
/// documentation says why, and the surface says it on screen.
/// </para>
/// </summary>
/// <param name="Sessions">How many sessions touched the window.</param>
/// <param name="ActiveTime">
/// Time an agent was actually producing inside the window, over the scoped machines.
/// Not the span of the sessions: a session left open overnight contributes the stretches
/// it worked and nothing else. Concurrent sessions are summed rather than merged — this
/// is agent-hours, so twelve agents running through one hour is twelve hours and the
/// figure is deliberately capable of exceeding the window.
/// <para>
/// The same total <see cref="ActivityByHour"/> sums to whenever the two cover the same
/// days, because it comes off the same sweep: the total <em>is</em> the integral of the
/// concurrency the grid shades. A second pass here would be a second place for a tile
/// and the grid under it to disagree.
/// </para>
/// </param>
/// <param name="LastActivityAt">The most recent activity in the scoped set, or null
/// when there was none.</param>
/// <param name="WithoutActivity">
/// How many of <paramref name="Sessions"/> left no parsable activity record and so added
/// nothing to <paramref name="ActiveTime"/>.
/// <para>
/// <b>This is a different fact from the one this parameter used to carry.</b> It was
/// <c>WithoutStart</c> — sessions whose start instant was never written down — back when
/// the active time was a session's file span and a missing start meant no span. That
/// arithmetic is gone. What is counted here is a session the activity source could not
/// describe at all: no transcript, an unparsable one, or one whose stretches all fell
/// outside the horizon. A reader diffing this record will see a rename and assume the
/// meaning carried over; it did not.
/// </para>
/// </param>
/// <param name="Capped">Whether the source stopped short of everything it holds.</param>
/// <param name="CapPerAssistant">How many sessions per assistant the source stopped at,
/// so the sentence admitting the cap can name the number instead of the surface keeping
/// a copy of it.</param>
/// <param name="Unreadable">The sources that could not be read, by name.</param>
/// <param name="Breakdown">Per machine when every machine is in view, per assistant
/// when one is focused. Ordered by sessions descending, then by name.</param>
public sealed record AssistantSessionsInsight(
    int Sessions,
    TimeSpan ActiveTime,
    DateTimeOffset? LastActivityAt,
    int WithoutActivity,
    bool Capped,
    int CapPerAssistant,
    IReadOnlyList<string> Unreadable,
    IReadOnlyList<AssistantSessionRow> Breakdown)
{
    /// <summary>
    /// How many sessions fell in each ISO week of the window, oldest first, one point
    /// per week whether anything happened in it or not.
    /// <para>
    /// A session is counted in the week it <em>last moved</em>. That is the only week
    /// every session has — the start is optional and no end is recorded at all — so
    /// any other rule would either invent an instant or drop the sessions missing one.
    /// The price is that a session running across a week boundary is one mark in the
    /// later week rather than a mark in each, and the surface says so beside the
    /// columns rather than leaving a reader to find the sum that does not add up.
    /// </para>
    /// <para>
    /// Beside the primary constructor rather than in it, the escape
    /// <c>ActivityPullRequest.ReviewTurnaround</c> and
    /// <c>GitHubReviewedPullRequest.ReviewTurnaround</c> already take: fixtures and
    /// adapters construct this record positionally, and a ninth parameter would break
    /// every one of them to add something none of them has an opinion about.
    /// </para>
    /// </summary>
    public IReadOnlyList<InsightPoint> SessionsPerWeek { get; init; } = [];

    /// <summary>
    /// Time inside the window in which a run had ended and the next thing to happen was
    /// a person — the queue this installation put on the reader's own attention.
    /// <para>
    /// Summed over concurrent sessions exactly as <see cref="ActiveTime"/> is, because
    /// two agents both blocked on you at once is two agent-hours of queue and that is
    /// what the measure is about.
    /// </para>
    /// <para>
    /// One of the two assistants cannot evidence this at all, so this is the other's
    /// alone. And a gap counts in full however long it ran: a session picked up after a
    /// weekend puts the whole weekend in here. That is deliberate — a prompt did
    /// eventually arrive, and clamping it would introduce a second arguable constant to
    /// go with the one that already ends a run — but it is the figure most likely to be
    /// argued with, so the surface states it rather than letting "waiting on you" imply
    /// you were sitting there.
    /// </para>
    /// <para>
    /// An init property rather than a ninth parameter, on
    /// <see cref="SessionsPerWeek"/>'s precedent.
    /// </para>
    /// </summary>
    public TimeSpan Waiting { get; init; }

    /// <summary>
    /// The 168 cells of the last seven dated local days, oldest day first and hour 00
    /// first within a day — or empty when the scoped set has no parsable activity at
    /// all.
    /// <para>
    /// Empty rather than 168 zeros in that case, so the part can decline to draw an axis
    /// it has nothing to put on — the rule <see cref="SessionsPerWeek"/> is already
    /// rendered under. When there is something to draw, though, every cell is present:
    /// an hour nobody worked is a zero and never a gap, and a heatmap draws those two
    /// differently on purpose.
    /// </para>
    /// <para>
    /// <b>This is the one figure here that does not follow the period control.</b> Move
    /// the reader from four weeks to twelve and <see cref="ActiveTime"/>,
    /// <see cref="Waiting"/>, <see cref="Sessions"/> and the breakdown all widen; this
    /// stays the same seven days. The part therefore mixes a windowed figure with a
    /// fixed one, which is a thing a surface may do only if it says so — and it says so
    /// on the grid itself rather than only in the note, because the label is what sits
    /// next to the thing that will not move.
    /// </para>
    /// </summary>
    public IReadOnlyList<ActivityHour> ActivityByHour { get; init; } = [];

    /// <summary>
    /// How many distinct sessions ran on each of the grid's days, oldest first, and empty
    /// exactly when <see cref="ActivityByHour"/> is.
    /// <para>
    /// Beside the hours rather than inside them because it is a different measure of a
    /// different thing. An hour's figure is "how many at once" and cannot be added up; a
    /// day's is "how many ran", which can. Deriving one from the other is the mistake this
    /// separation exists to prevent — summing a row of peaks counts a long session once
    /// per hour it spanned.
    /// </para>
    /// </summary>
    public IReadOnlyList<ActivityDay> ActivityByDay { get; init; } = [];

    /// <summary>
    /// The most sessions that were producing at one instant anywhere in the window, and
    /// the local hour that first happened in — or null when nothing produced.
    /// <para>
    /// Producing rather than open, so it is the first grid's measure and not the second's:
    /// a session stopped with nothing having prompted it yet is on the go and is not
    /// working, and the two figures are far apart on a real profile.
    /// </para>
    /// <para>
    /// Read out of the same sweep <see cref="ActivityByHour"/> is drawn from, never a
    /// second pass. The window's true maximum falls inside some hour and that hour's cell
    /// recorded it, so the tile and the grid cannot have measured differently — the rule
    /// <see cref="ActiveTime"/> is already summed under.
    /// </para>
    /// <para>
    /// <b>This follows the period control and the grid does not.</b> Twelve weeks reaches
    /// further back than seven days, so a peak named here may sit outside every row drawn
    /// below it. That is the part's sentence to say, and it says it.
    /// </para>
    /// </summary>
    public ConcurrencyPeak? MostSessionsAtOnce { get; init; }

    /// <summary>
    /// The most agents the sessions had spawned and running at one instant anywhere in the
    /// window, and the local hour that first happened in — or null when none was spawned.
    /// <para>
    /// Counted apart from the sessions and never with them. A subagent always overlaps the
    /// session that spawned it — that is what spawning means — so one sweep over both
    /// would report two of something that never existed, and no figure above this one
    /// includes them.
    /// </para>
    /// <para>
    /// Claude's alone, because Copilot spawns none. That is a fact about Copilot rather
    /// than a gap in the read, and it is the same admission the waiting figure already
    /// carries the other way round.
    /// </para>
    /// <para>
    /// Not comparable to <see cref="MostSessionsAtOnce"/> and not a share of it: one
    /// session can hold five of these at once, so neither bounds the other. The surface
    /// may set them side by side; it may not divide them.
    /// </para>
    /// </summary>
    public ConcurrencyPeak? MostAgentsAtOnce { get; init; }

    /// <summary>
    /// The gap that ends a run, as the source reported it. Carried so the footnote can
    /// name the source's number rather than keeping a second copy of it —
    /// <see cref="CapPerAssistant"/>'s precedent, and it matters more here: the answer
    /// moves under this constant, so a surface quoting a stale copy of it would be
    /// explaining one figure with another figure's threshold.
    /// </summary>
    public TimeSpan IdleAfter { get; init; }

    public static AssistantSessionsInsight Empty { get; } =
        new(0, TimeSpan.Zero, null, 0, false, 0, [], []) { SessionsPerWeek = [] };
}
