namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>
/// One copy of a replicated GitHub settings document: the stored text, whole, and
/// the stamp it was stored at (local ADR 0021).
/// <para>
/// A string and not a shape, as the roadmap's copy is (local ADR 0018): whatever
/// carries one never has to read it, and only the store that holds the document
/// decides whether a copy is one it can read.
/// </para>
/// </summary>
/// <param name="Content">The document exactly as it is stored.</param>
/// <param name="UpdatedAt">When it was last changed — the stamp last-write-wins is
/// decided on.</param>
public sealed record GitHubReplicaCopyDto(string Content, DateTimeOffset UpdatedAt);
