namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// PORT — what a task's Devbook references point at right now, and what a person
/// could point one at.
/// <para>
/// A task stores its references as text (<see cref="DataTransferObjects.TaskItemDto.DevbookReferences"/>)
/// and never asks whether they exist: a reference outlives a rename of the chapter
/// it names, and showing it as broken is the reader's answer to that. This is the
/// reader. Tasks does not own the Devbook and a Tasks screen may not reference
/// Devbook's published surface
/// (<c>ModuleBoundaryTests.A_module_ui_asks_only_its_own_modules_published_surface</c>),
/// so it asks here and an infrastructure adapter that can see both contexts
/// answers — the shape <see cref="IRoadmapTagSource"/> and
/// <see cref="IRepositoryDirectory"/> take for the same reason.
/// </para>
/// <para>
/// Neither method throws for a reference it cannot place. A malformed or missing
/// one is an ordinary answer with a <see cref="DevbookReferenceState"/> saying so,
/// because a list of chips with one broken link in it should still draw.
/// </para>
/// </summary>
public interface IDevbookReferenceResolver
{
    /// <summary>
    /// One answer per reference, in the order given, for the repository named by
    /// <paramref name="repositoryAlias"/>. With no repository every answer is
    /// <see cref="DevbookReferenceState.Unverified"/>: there is no devbook to ask.
    /// </summary>
    Task<IReadOnlyList<ResolvedDevbookReference>> ResolveAsync(
        string? repositoryAlias,
        IReadOnlyList<string> references,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every page and chapter a reference could name in that repository, for a
    /// picker: each page (level 0) followed by its chapters (their heading level),
    /// in reading order. Empty when there is no repository or no devbook database
    /// to list from — a picker still accepts a typed <c>path#anchor</c>.
    /// </summary>
    Task<IReadOnlyList<DevbookReferenceTarget>> ListTargetsAsync(
        string? repositoryAlias,
        CancellationToken cancellationToken = default);
}

/// <summary>What one stored reference points at.</summary>
/// <param name="Reference">The reference exactly as it was asked about.</param>
/// <param name="Path">Its page, repository-relative with <c>/</c> separators — the
/// reference itself when it could not be read.</param>
/// <param name="Anchor">Its heading slug, or null for a whole page.</param>
/// <param name="State">What was found.</param>
/// <param name="Title">The heading's text for a chapter, else the page's title,
/// else the reference — always something a chip can show.</param>
/// <param name="Status">The chapter's <c>meta</c> status for a
/// <see cref="DevbookReferenceState.Chapter"/>; null otherwise.</param>
/// <param name="Folder">The devbook folder it lies in (<c>domain</c>,
/// <c>arc42</c>, …), or null when it lies in none.</param>
public sealed record ResolvedDevbookReference(
    string Reference,
    string Path,
    string? Anchor,
    DevbookReferenceState State,
    string Title,
    string? Status,
    string? Folder)
{
    /// <summary>Whether the reference names something that is not there, or
    /// somewhere no devbook reaches — the three states a reader shows as broken.
    /// <see cref="DevbookReferenceState.Unverified"/> is not broken: nobody could
    /// look.</summary>
    public bool IsBroken => State is DevbookReferenceState.UnknownHeading
        or DevbookReferenceState.UnknownPage
        or DevbookReferenceState.OutsideDevbook;
}

/// <summary>One page or chapter a reference may name.</summary>
/// <param name="Reference">What a task would store to point at it: the path for
/// a page, <c>path#slug</c> for a chapter.</param>
/// <param name="Title">The page title or heading text.</param>
/// <param name="Folder">The devbook folder it lies in (<c>domain</c>,
/// <c>arc42</c>, …), for grouping.</param>
/// <param name="Level">0 for a page; the heading level for a chapter.</param>
/// <param name="Status">The chapter's <c>meta</c> status; null for a page.</param>
public sealed record DevbookReferenceTarget(
    string Reference,
    string Title,
    string Folder,
    int Level,
    string? Status);

/// <summary>What a Devbook reference turned out to point at.</summary>
public enum DevbookReferenceState
{
    /// <summary>A heading of the page, found by its slug.</summary>
    Chapter,

    /// <summary>The page, with no anchor asked for.</summary>
    Page,

    /// <summary>The page is there and no heading of it has that slug.</summary>
    UnknownHeading,

    /// <summary>No such page in the devbook folder the path names — or a value
    /// that is not a reference at all.</summary>
    UnknownPage,

    /// <summary>The path lies under none of the repository's devbook
    /// folders.</summary>
    OutsideDevbook,

    /// <summary>Nobody could look: no repository, or a folder that is not
    /// readable right now.</summary>
    Unverified
}

/// <summary>The wire spelling of a <see cref="DevbookReferenceState"/>, shared by
/// every channel that puts one on a page or a payload.</summary>
public static class DevbookReferenceStates
{
    public const string Chapter = "chapter";
    public const string Page = "page";
    public const string UnknownHeading = "unknown-heading";
    public const string UnknownPage = "unknown-page";
    public const string OutsideDevbook = "outside-devbook";
    public const string Unverified = "unverified";

    public static string ToWire(this DevbookReferenceState state) =>
        state switch
        {
            DevbookReferenceState.Chapter => Chapter,
            DevbookReferenceState.Page => Page,
            DevbookReferenceState.UnknownHeading => UnknownHeading,
            DevbookReferenceState.UnknownPage => UnknownPage,
            DevbookReferenceState.OutsideDevbook => OutsideDevbook,
            DevbookReferenceState.Unverified => Unverified,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Not a Devbook reference state.")
        };
}
