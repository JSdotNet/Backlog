namespace Backlog.Storage;

/// <summary>
/// Lightweight, derived projection of a <see cref="Backlog.Domain.BacklogEntry"/>
/// used for fast listing without loading the full markdown body. Persisted in the
/// JSON index.
/// </summary>
public sealed record BacklogEntrySummary(
    Guid Id,
    string Title,
    string Type,
    string Status,
    string Priority,
    int CompletedSubItems,
    int TotalSubItems,
    DateTimeOffset CreatedAt);
