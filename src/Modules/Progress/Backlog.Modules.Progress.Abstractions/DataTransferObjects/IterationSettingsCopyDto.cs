namespace Backlog.Modules.Progress.Abstractions.DataTransferObjects;

/// <summary>
/// One copy of the replicated iteration settings document: the stored text, whole, and
/// the stamp it was stored at (local ADR 0036, §1).
/// <para>
/// A string and not a shape, for the reason the My Day preferences' copy gives: whatever
/// carries the document never reads it, and a copy written by a newer build keeps the
/// keys this one does not know. Only the store decides whether a copy is one it can read.
/// </para>
/// </summary>
/// <param name="Content">The document exactly as it is stored.</param>
/// <param name="UpdatedAt">When it was last written — the stamp last-write-wins is
/// decided on.</param>
public sealed record IterationSettingsCopyDto(string Content, DateTimeOffset UpdatedAt);
