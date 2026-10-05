namespace Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

/// <summary>
/// One working day of the hours report a begun head opens (local ADR 0019, §6): the
/// actual hours the head shows, and the working stretches they add up from.
/// </summary>
/// <param name="Date">The working day, which runs from 04:00 local on this date to 04:00
/// the next morning.</param>
/// <param name="Worked">The time its stretches cover: the figure the day's head shows.</param>
/// <param name="Stretches">Its stretches, earliest first. Empty for a day nobody worked.</param>
public sealed record RoadmapWorkedDayDto(
    DateOnly Date,
    TimeSpan Worked,
    IReadOnlyList<RoadmapWorkedStretchDto> Stretches);

/// <summary>
/// One working stretch, or the part of one that falls on its day: from the first human
/// turn to the end of the agent's answer to the last, with the sessions it was worked in.
/// </summary>
/// <param name="Start">Where the stretch starts, at the local offset of the zone the day
/// is cut in.</param>
/// <param name="End">Where it ends, at the same offset.</param>
/// <param name="Turns">How many human turns the person made inside it.</param>
/// <param name="Sessions">The sessions with a turn or an answer inside it, each once.</param>
public sealed record RoadmapWorkedStretchDto(
    DateTimeOffset Start,
    DateTimeOffset End,
    int Turns,
    IReadOnlyList<RoadmapStretchSessionDto> Sessions)
{
    /// <summary>How long the stretch counts.</summary>
    public TimeSpan Length => End - Start;
}

/// <summary>
/// A session a stretch was worked in, as the report names it.
/// </summary>
/// <param name="Title">The session's title, or its id where no title is known.</param>
/// <param name="Repository">The repository the session ran in, or null where none is known.</param>
/// <param name="Environment">The machine the session ran on.</param>
public sealed record RoadmapStretchSessionDto(
    string Title,
    string? Repository,
    string Environment);
