namespace Backlog.Modules.Progress.Abstractions.DataTransferObjects;

/// <summary>
/// One copy of a replicated goal: its id, the title it shows under, the stored text whole,
/// and the stamp it was stored at, as the roadmap's plan travels (local ADR 0018). The text is a string and not a shape, so whatever
/// carries it never reads it and a copy a newer build wrote keeps the fields this one does
/// not know. <paramref name="Title"/> is for the sync log and never decides anything.
/// </summary>
/// <param name="Id">The goal's id — the document's id on the replica.</param>
/// <param name="Title">What the goal is called, for the sync log.</param>
/// <param name="Content">The goal exactly as it is stored.</param>
/// <param name="UpdatedAt">When it was last written — the stamp last-write-wins is decided on.</param>
public sealed record GoalReplicaCopyDto(Guid Id, string Title, string Content, DateTimeOffset UpdatedAt);
