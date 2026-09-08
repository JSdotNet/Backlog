namespace Backlog.Modules.Dashboard.Abstractions.Insights;

/// <summary>
/// One cell of the activity grid: one real hour of one real day, on this machine's
/// local clock.
/// <para>
/// A dated day rather than a day of the week, and that is the decision worth stating
/// because the obvious alternative was taken and then rejected. Folding every Tuesday
/// in the window onto one row would make the grid a <em>profile</em> — "when can I
/// generally get agents running" — and it would let the grid follow the period control
/// like everything else on the surface. What it would stop being is a record of what
/// actually happened, and that is what was asked for. So a row is Wednesday the 2nd,
/// not Wednesdays, and a cell is an hour that happened once.
/// </para>
/// <para>
/// <b>The grid is the last seven dated days and does not move with the period
/// control.</b> Every other figure in the sessions part widens when the reader asks for
/// twelve weeks instead of four; this one does not, because seven days of dated rows is
/// the grid and twelve weeks of them would be eighty-four. That asymmetry is chosen
/// rather than overlooked, and on this surface an unstated refusal is the exact failure
/// mode the part is built to avoid — so it is said next to the grid, not only in the
/// part's note.
/// </para>
/// <para>
/// There is no cross-week aggregation anywhere in here. Every figure below is simply
/// what happened in that hour, which is why none of them needs a sentence explaining
/// which of two aggregations produced it.
/// </para>
/// </summary>
/// <param name="Day">Which day, as this machine's local clock dated it. Local rather
/// than UTC because the question is about the reader's own working day, and it is the
/// only figure on this surface drawn that way.</param>
/// <param name="Hour">0–23, local.</param>
/// <param name="PeakSessions">The most sessions that were producing at one instant in
/// this hour. A maximum, not a mean and not a total.</param>
/// <param name="Active">Agent-active time in this hour, summed over concurrent
/// sessions. Two agents for the whole hour is two hours, so a cell can exceed the hour
/// it describes — that is what makes it agent-hours rather than wall clock.</param>
/// <param name="Waiting">Time in this hour in which a run had ended and the next thing
/// to happen was a person. Summed the same way, and one assistant's alone.</param>
/// <param name="OpenSessions">
/// The most sessions that were on the go at one instant in this hour — producing, or
/// stopped with nothing having prompted them yet.
/// <para>
/// Always at least <paramref name="PeakSessions"/>, and usually more: a session waiting
/// on a prompt is open and is not producing. The gap between the two is the point of
/// carrying both.
/// </para>
/// <para>
/// A session goes quiet without ever resuming — abandoned, or the window simply closed —
/// and is <em>not</em> open through that silence. Nothing records an end, so counting to
/// the last thing that happened would keep an abandoned window open for days, which is
/// the span-shaped overstatement this whole part exists to have stopped making.
/// </para>
/// </param>
/// <param name="InWorkingHours">
/// Whether any part of this hour falls inside the reader's own working hours for that day.
/// <para>
/// Carried rather than worked out where it is drawn, because it is no longer only a
/// shade: the day counts beside the grid are split on the same question, and two places
/// deciding what "in hours" means is two places for the outline and the count under it to
/// disagree.
/// </para>
/// </param>
public sealed record ActivityHour(
    DateOnly Day,
    int Hour,
    int PeakSessions,
    TimeSpan Active,
    TimeSpan Waiting,
    int OpenSessions,
    bool InWorkingHours);

/// <summary>
/// One dated day of the activity grid, and how many distinct sessions ran in it.
/// <para>
/// A count, and deliberately not the sum of the day's twenty-four
/// <see cref="ActivityHour.PeakSessions"/>. Those cannot be added: a session that ran
/// from nine to five is at its peak in eight of them and adding the row would report it
/// eight times. This counts each session once on each day it was producing in, so a
/// session that ran past midnight is one session on both days and neither day is wrong.
/// </para>
/// <para>
/// It answers a different question from every other figure on this grid — "how many did
/// I run" rather than "how many at once" — which is why it sits in its own column with
/// its own heading rather than at the end of a row of peaks.
/// </para>
/// </summary>
/// <param name="Day">Which day, as this machine's local clock dated it.</param>
/// <param name="Sessions">Distinct sessions that were producing at some point in the
/// day. Counted per assistant-session identity, so two agents in one session is one
/// session and the same session on two days is one on each.</param>
/// <param name="SessionsInWorkingHours">Those of them that produced during the reader's
/// working hours for that day.</param>
/// <param name="SessionsOutsideWorkingHours">Those of them that produced outside those
/// hours.</param>
/// <remarks>
/// <para>
/// <b>The two parts do not sum to the whole, and cannot.</b> A session that began at four
/// and ran until eight produced both inside the working day and outside it, so it is
/// counted in both — one session, two answers. Making them add up would mean picking a
/// side for it, and every rule for picking one (where it started, where it spent longest)
/// is an invention this record has no grounds for. The surface says so beside the
/// columns rather than leaving a reader to find that three plus four is five.
/// </para>
/// </remarks>
public sealed record ActivityDay(
    DateOnly Day,
    int Sessions,
    int SessionsInWorkingHours,
    int SessionsOutsideWorkingHours);
