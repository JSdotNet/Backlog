namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// Where Backlog keeps which way each devbook sync unit is levelled — the
/// direction map per repository that devbook's branch-true decision moved out of
/// the chapters' <c>sync</c> field.
/// <para>
/// Keyed by repository alias (<see cref="DevbookSubject.AliasKey"/>) and a block's
/// folder-qualified address — <c>domain/inbox/context.md</c>,
/// <c>domain/tasks/domain.md#task</c>, <c>arc42/building-blocks/sync.md</c> —
/// so the layout a clone or a branch has (<c>.devbook/</c> or the legacy
/// <c>.domain/</c>) never splits one block into two entries. A unit resolves
/// nearest wins over these entries, by <c>DevbookSync.ResolveUnit</c> in the
/// shared kernel; the store only keeps what is stated.
/// </para>
/// <para>
/// Nothing here is a Markdown write, so a direction is set on a branch snapshot
/// as readily as on a clone. Whether the repository is archived is the caller's
/// to refuse, as it is for a subject's state (local ADR 0029 §4).
/// </para>
/// <para>
/// The <see cref="Import"/> pair is the one-time hand-over from the Markdown: the
/// first read of a repository copies the values its chapters stated, and the mark
/// it leaves keeps a later read from copying them over a person's choice.
/// </para>
/// </summary>
public interface IDevbookSyncDirectionStore
{
    /// <summary>Raised after any write, on whichever thread wrote.</summary>
    event Action? Changed;

    /// <summary>Every direction stated for one repository, by address.</summary>
    IReadOnlyList<DevbookSyncStatement> List(string? repositoryAlias);

    /// <summary>The direction stated at one address, or null when none is —
    /// the block inherits.</summary>
    string? Get(string? repositoryAlias, string address);

    /// <summary>
    /// States a direction at an address, or clears it with a null or blank
    /// <paramref name="direction"/> so the block inherits. Answers the statement
    /// kept, or null when cleared.
    /// </summary>
    /// <param name="by">Who is setting it — the host's author, never a
    /// caller's.</param>
    /// <exception cref="ArgumentException">The direction is none of
    /// <c>DevbookSync.Directions</c>, or the address names no devbook
    /// folder.</exception>
    DevbookSyncStatement? Set(string? repositoryAlias, string address, string? direction, string by);

    /// <summary>Whether the repository's Markdown statements have been copied in
    /// already.</summary>
    bool IsImported(string? repositoryAlias);

    /// <summary>
    /// Copies the directions a repository's Markdown states into its map, once:
    /// only addresses the map does not hold yet, then the repository is marked
    /// imported. A no-op on a repository already marked, so a person's choice is
    /// never overwritten by what a chapter still says.
    /// </summary>
    void Import(string? repositoryAlias, IEnumerable<(string Address, string Direction)> stated);
}

/// <summary>One stated direction: the block's address, the direction, and who
/// set it when.</summary>
public sealed record DevbookSyncStatement(string Address, string Direction, string By, DateTimeOffset At)
{
    /// <summary>The author of a statement <see cref="IDevbookSyncDirectionStore.Import"/>
    /// copied in: the chapter stated it, no person here did.</summary>
    public const string ImportedBy = "markdown";
}
