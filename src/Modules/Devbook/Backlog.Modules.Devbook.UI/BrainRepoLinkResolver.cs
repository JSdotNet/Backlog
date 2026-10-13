using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel.Devbook;
using Backlog.UI.Components.Devbook;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Whether a Brain note's <c>repo:</c> link can be followed on this machine
/// (local ADR 0027 §6), asked of the configured repositories through
/// <see cref="IDevbookFolderSource"/> — a clone or a branch snapshot, the same
/// answer either way, since the pane reads both.
/// <para>
/// A link resolves when its alias names a repository configured here whose
/// devbook shows the section the path lies in. Everything else is the dashed
/// chip, never an error: the same Brain is read on machines with different
/// repositories, and a link to one this machine has not got is ordinary. Asked
/// only when a note holds a <c>repo:</c> link, so a Brain note with none never
/// touches the repository source.
/// </para>
/// </summary>
public sealed class BrainRepoLinkResolver(IDevbookFolderSource folders)
{
    /// <summary>What an unresolved chip says when the alias is not configured
    /// here.</summary>
    public const string NotOnThisMachine = RepoLinkChip.NotOnThisMachine;

    /// <summary>What it says when the alias is, but the path names no chapter its
    /// devbook shows.</summary>
    public const string NotADevbookChapter = "not a chapter in its devbook";

    /// <summary>Reads and resolves one link as written, or null when the text is
    /// not a well-formed <c>repo:</c> link.</summary>
    public BrainRepoLinkResolution? Resolve(string? text) =>
        BrainRepoLink.Parse(text) is { } link ? Resolve(link) : null;

    public BrainRepoLinkResolution Resolve(BrainRepoLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        var configured = folders.Folders(link.Alias);
        if (configured.Count == 0) return new BrainRepoLinkResolution(link, false, NotOnThisMachine);

        var chapter = DevbookChapterLink.From(link.Reference);
        var shown = chapter is not null
            && DevbookAreaCatalog.VisibleAreas(configured).Any(area => string.Equals(area.Key, chapter.AreaKey, StringComparison.OrdinalIgnoreCase));

        return shown
            ? new BrainRepoLinkResolution(link, true, null)
            : new BrainRepoLinkResolution(link, false, NotADevbookChapter);
    }
}

/// <summary>A <c>repo:</c> link and whether it can be followed here.</summary>
/// <param name="Link">The link as read.</param>
/// <param name="Resolved">Whether this machine has the repository and the
/// chapter's section.</param>
/// <param name="Problem">Why it cannot be followed, in words; null when it
/// can.</param>
public sealed record BrainRepoLinkResolution(BrainRepoLink Link, bool Resolved, string? Problem)
{
    /// <summary>The chapter's short label — the file or heading name — for a chip
    /// with no words of the author's own.</summary>
    public string Label => DevbookReference.Parse(Link.Reference)?.Label ?? Link.Reference;
}
