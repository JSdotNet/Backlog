namespace Backlog.Modules.Dashboard.Abstractions.Insights;

/// <summary>
/// The busiest one hour of a window: how many of something were running at once inside
/// it, and which hour that was on this machine's local clock.
/// </summary>
/// <remarks>
/// <para>
/// One record for both the sessions figure and the agents figure, on
/// <see cref="AssistantSessionRow"/>'s precedent — the same measure under two headings,
/// and two records would only mean two places for "what a peak is" to drift. The tile
/// names the population; this names the peak.
/// </para>
/// <para>
/// <b>An hour, not an instant.</b> The peak is read out of an hour cell of the same
/// sweep the grids are drawn from, and that sweep's answer is "the most at once at some
/// moment inside this hour". A <c>DateTimeOffset</c> here would claim a precision
/// nothing established, and it would invite the UTC formatter onto the one figure on
/// this surface that is local. <see cref="Day"/> and <see cref="Hour"/> are the same
/// pair <see cref="ActivityHour"/> names its own cell with, so a tile and the grid under
/// it point at the same cell in the same words.
/// </para>
/// <para>
/// <b>Null, never a zero-peak.</b> A window in which nothing ran has no busiest hour,
/// and inventing one would put a real day and hour next to a nothing — the scalar form
/// of the empty-versus-168-zeros rule <see cref="AssistantSessionsInsight.ActivityByHour"/>
/// already follows.
/// </para>
/// <para>
/// <b>The earliest hour wins a tie.</b> Two hours that both reached the peak are
/// separated by when they happened, not by whatever order a dictionary enumerated in.
/// "When it first got that busy" is a question with an answer; "one of the times it got
/// that busy" is a figure that moves between two refreshes of an unchanged profile —
/// the same defect <c>ClaudeTranscripts.Newest</c> carries a path tie-break to avoid.
/// </para>
/// </remarks>
/// <param name="Peak">The most that were running at one instant inside the hour.</param>
/// <param name="Day">Which day, as this machine's local clock dated it.</param>
/// <param name="Hour">0–23, local.</param>
public sealed record ConcurrencyPeak(int Peak, DateOnly Day, int Hour);
