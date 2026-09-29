namespace Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

/// <summary>
/// One copy of a replicated roadmap document: the stored text, whole, and the
/// stamp it was stored at (local ADR 0018).
/// <para>
/// A string and not a shape, on purpose. The plan and the pace travel as opaque
/// documents, so whatever carries one never has to read it, and a copy written
/// by a newer build keeps the fields this one does not know. Only the store that
/// holds the document decides whether a copy is one it can read.
/// </para>
/// </summary>
/// <param name="Content">The document exactly as it is stored.</param>
/// <param name="UpdatedAt">When it was last written — the stamp last-write-wins
/// is decided on.</param>
public sealed record RoadmapReplicaCopyDto(string Content, DateTimeOffset UpdatedAt);
