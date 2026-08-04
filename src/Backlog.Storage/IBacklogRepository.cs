using Backlog.Domain;

namespace Backlog.Storage;

/// <summary>
/// Local-first persistence for <see cref="BacklogEntry"/> aggregates. Markdown is
/// the canonical source of truth; a JSON index holds derived summaries for fast
/// listing.
/// </summary>
public interface IBacklogRepository
{
    /// <summary>Creates or updates the canonical markdown file and refreshes the index.</summary>
    Task SaveAsync(BacklogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Loads a full aggregate from its canonical markdown, or null if missing.</summary>
    Task<BacklogEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Deletes the markdown file and removes it from the index.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns derived summaries for all persisted entries from the JSON index.</summary>
    Task<IReadOnlyList<BacklogEntrySummary>> ListAsync(CancellationToken cancellationToken = default);
}
