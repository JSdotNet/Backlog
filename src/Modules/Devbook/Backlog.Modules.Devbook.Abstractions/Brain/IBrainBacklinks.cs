namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// The Brain notes that link to one repository chapter (local ADR 0027 §6): the
/// reverse of a <see cref="BrainRepoLink"/>, read from the Brain's own database
/// in the owner's app storage and never written anywhere — so the repository
/// chapter can show its owner <b>From your Brain</b> while nothing reaches the
/// repository.
/// <para>
/// A port of its own because the database is the Brain's, kept by the
/// infrastructure that builds it; the Devbook pane only asks. With no Brain set,
/// or no database built for it yet, the answer is empty — the same answer as a
/// Brain that links to nothing, which is all a reader of the chapter needs.
/// </para>
/// </summary>
public interface IBrainBacklinks
{
    /// <summary>
    /// The notes linking to the chapter file at <paramref name="chapterPath"/> of
    /// the repository <paramref name="repositoryAlias"/>, one per note, in title
    /// order. The path is repository-relative, in either devbook layout's
    /// spelling; a link to any heading of the file counts.
    /// </summary>
    Task<IReadOnlyList<BrainBacklink>> LinksToAsync(string repositoryAlias, string chapterPath, CancellationToken cancellationToken = default);
}

/// <summary>One Brain note linking to a repository chapter.</summary>
/// <param name="NotePath">The note's path in the Brain, as the menu names it —
/// <c>projects/plan.md</c>.</param>
/// <param name="Title">The note's title.</param>
/// <param name="Heading">The chapter the link sits in — a heading carrying a
/// <c>meta</c> block, since only those are nodes of the Brain's graph — when the
/// note links from no place outside every chapter: not from its own
/// <c>related</c>, and not from prose under a plain heading or none.</param>
public sealed record BrainBacklink(string NotePath, string Title, string? Heading);
