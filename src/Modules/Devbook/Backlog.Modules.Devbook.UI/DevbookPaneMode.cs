using Backlog.Modules.Devbook.Abstractions;

using Microsoft.AspNetCore.Components;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// A devbook the pane can show besides a repository's, supplied by another context
/// (local ADR 0040 §2: the supplier publishes, the other context conforms). Devbook
/// draws the pane, its tabs, menu, search and atlas frame; the mode supplies the
/// source and the parts that are its own. Resolved, not injected: a host that
/// composes none draws no such mode.
/// </summary>
public interface IDevbookPaneMode
{
    /// <summary>The devbook the mode shows.</summary>
    IDevbookPersonalSource Source { get; }

    /// <summary>Whether the person has chosen the folder: false draws
    /// <see cref="RenderStatus"/> with <see cref="DevbookPaneModeStatus.NotSetUp"/>;
    /// null when the host cannot say.</summary>
    bool? IsSetUp { get; }

    /// <summary>Whether the tabs include <see cref="IDevbookPersonalSource.HiddenTabs"/>.</summary>
    bool ShowsHidden { get; }

    /// <summary>Raised, on whichever thread moved it, when
    /// <see cref="ShowsHidden"/> changes.</summary>
    event Action? ShowsHiddenChanged;

    /// <summary>The control at the end of the menu heading
    /// (<see cref="DevbookMenuTreeView.HeadingTrailing"/>); null draws none.</summary>
    RenderFragment? MenuHeadingTrailing { get; }

    /// <summary>The strip's first action while the mode is shown; null draws none.</summary>
    RenderFragment? StripAction(DevbookPaneModeContext context);

    /// <summary>One tab's panel.</summary>
    RenderFragment RenderTab(DevbookPaneModeTab tab);

    /// <summary>The body while there is no tab to show.</summary>
    RenderFragment RenderStatus(DevbookPaneModeStatus status, string? message);

    /// <summary>Under a repository chapter: this devbook's notes that link to it;
    /// null draws none.</summary>
    RenderFragment? BelowRepositoryChapter(string repositoryAlias, string chapterPath, EventCallback<string> onOpenNote);

    /// <summary>Every link this devbook's notes hold into the repository
    /// <paramref name="repositoryAlias"/>, one per note, chapter file and heading —
    /// what the Domain view reads once to show each unit, feature and term the
    /// notes linking to it (local ADR 0045). None by default.</summary>
    Task<IReadOnlyList<DevbookChapterNote>> ChapterNotesAsync(string repositoryAlias, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DevbookChapterNote>>([]);

    /// <summary>The box listing <paramref name="notes"/>, already read through
    /// <see cref="ChapterNotesAsync"/>, under a Domain view page; null draws none,
    /// as it does by default.</summary>
    RenderFragment? NotesBox(string repositoryAlias, IReadOnlyList<DevbookNote> notes, EventCallback<string> onOpenNote) => null;

    /// <summary>The atlas over this devbook.</summary>
    Task<DevbookAtlasGraph> ReadAtlasAsync(CancellationToken cancellationToken = default);

    /// <summary>The repository chapter a raw cross-corpus link in the atlas names,
    /// or null.</summary>
    DevbookRepositoryChapter? RepositoryChapterFor(string raw);
}

/// <summary>One link from a note of the mode's devbook into a repository chapter
/// (<see cref="IDevbookPaneMode.ChapterNotesAsync"/>).</summary>
/// <param name="ChapterPath">The chapter file, repository-relative, as the note
/// writes it, in either devbook layout's spelling.</param>
/// <param name="Slug">The heading linked to; null for the whole file.</param>
/// <param name="Note">The note that links.</param>
public sealed record DevbookChapterNote(string ChapterPath, string? Slug, DevbookNote Note);

/// <summary>A note of the mode's devbook as a Domain view page lists it.</summary>
/// <param name="NotePath">Its path from the devbook's root.</param>
/// <param name="Title">Its title.</param>
/// <param name="Heading">The note heading the link sits under; null for the note
/// as a whole.</param>
public sealed record DevbookNote(string NotePath, string Title, string? Heading);

/// <summary>What the pane hands <see cref="IDevbookPaneMode.StripAction"/>.</summary>
/// <param name="Readable">Whether the pane can read the source now.</param>
/// <param name="OpenNoteAsync">Selects the tab whose folder is the first argument
/// and the note at the second (its path from the source's root), closes search,
/// reloads the menu and re-renders the pane.</param>
public sealed record DevbookPaneModeContext(bool Readable, Func<string, string, Task> OpenNoteAsync);

/// <summary>What the pane hands <see cref="IDevbookPaneMode.RenderTab"/>.</summary>
/// <param name="SelectedPath">The note the tab has open, from the source's root;
/// null for none.</param>
/// <param name="OnOpenRepositoryChapter">Raised when the reader follows a link into
/// a repository chapter.</param>
/// <param name="TaskTitleFor">The title of a task by id, or null when the backlog
/// holds none.</param>
/// <param name="OnOpenTask">Raised with a task id the reader follows.</param>
public sealed record DevbookPaneModeTab(
    string? SelectedPath,
    EventCallback<DevbookRepositoryChapter> OnOpenRepositoryChapter,
    Func<string, string?>? TaskTitleFor,
    EventCallback<string> OnOpenTask);

/// <summary>Why <see cref="IDevbookPaneMode.RenderStatus"/> is asked.</summary>
public enum DevbookPaneModeStatus
{
    /// <summary>The tabs are still being read.</summary>
    Reading,

    /// <summary>No folder is chosen yet.</summary>
    NotSetUp,

    /// <summary>A folder is set and cannot be read.</summary>
    Unavailable,
}
