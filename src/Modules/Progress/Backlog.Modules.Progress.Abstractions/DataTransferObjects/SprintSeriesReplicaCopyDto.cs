namespace Backlog.Modules.Progress.Abstractions.DataTransferObjects;

/// <summary>
/// One copy of a replicated sprint series: its id, the name it shows under, the stored
/// text whole, and the stamp it was stored at.
/// <para>
/// The text is a string and not a shape, as the roadmap's copy is: whatever carries it
/// never has to read it, and a copy written by a newer build keeps the fields this one
/// does not know. Only the store that holds the series decides whether a copy is one it
/// can read. <paramref name="Name"/> is for a person reading the sync log, and never
/// decides anything.
/// </para>
/// </summary>
/// <param name="Id">The series' id — the document's id on the replica.</param>
/// <param name="Name">What the series is called, for the sync log.</param>
/// <param name="Content">The series exactly as it is stored.</param>
/// <param name="UpdatedAt">When it was last written — the stamp last-write-wins is
/// decided on.</param>
public sealed record SprintSeriesReplicaCopyDto(Guid Id, string Name, string Content, DateTimeOffset UpdatedAt);
