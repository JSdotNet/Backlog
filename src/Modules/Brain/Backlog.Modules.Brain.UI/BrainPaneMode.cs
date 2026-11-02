using Backlog.Desktop.UI.Devbook;
using Backlog.Modules.Brain.Abstractions;
using Backlog.Modules.Devbook.Abstractions;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Brain.UI;

/// <summary>
/// The Brain as a mode of the Devbook pane (local ADR 0040 §2): the Brain conforms
/// to the extension point Devbook publishes, and Devbook draws the pane around it.
/// The note panel, Capture, Show hidden, the status bodies, the From your Brain box
/// and the Brain's atlas are drawn here, with the markup and test ids they had on
/// the pane.
/// <para>
/// The optional Brain ports are resolved on every ask rather than injected, as the
/// pane resolved them: a host that composes no writer offers no Capture, one with
/// no Show hidden draws no switch. Every one of them is a singleton.
/// </para>
/// </summary>
internal sealed class BrainPaneMode(IServiceProvider services, IDevbookPersonalSource source) : IDevbookPaneMode
{
    public IDevbookPersonalSource Source { get; } = source ?? throw new ArgumentNullException(nameof(source));

    private IBrainFolderSource? Brain => services.GetService<IBrainFolderSource>();

    private IBrainShowHidden? ShowHidden => services.GetService<IBrainShowHidden>();

    private IBrainNoteWriter? Writer => services.GetService<IBrainNoteWriter>();

    private BrainAtlas Atlas => services.GetService<BrainAtlas>() ?? new BrainAtlas(services.GetRequiredService<IDevbookFolderSource>());

    public bool? IsSetUp => services.GetService<IBrainSettings>()?.IsSet;

    public bool ShowsHidden => ShowHidden?.Shown == true;

    public event Action? ShowsHiddenChanged
    {
        add
        {
            if (ShowHidden is { } showHidden) showHidden.Changed += value;
        }
        remove
        {
            if (ShowHidden is { } showHidden) showHidden.Changed -= value;
        }
    }

    public RenderFragment? MenuHeadingTrailing => ShowHidden is { } showHidden
        ? builder =>
        {
            builder.OpenComponent<BrainShowHiddenSwitch>(0);
            builder.AddComponentParameter(1, nameof(BrainShowHiddenSwitch.ShowHidden), showHidden);
            builder.CloseComponent();
        }
        : null;

    /// <summary>
    /// Capture, first on the Brain's side of the strip: getting a thought in is the
    /// thing a person most often comes to the Brain for, and a note is written into
    /// its inbox area whichever tab is open. Only on a Brain the pane can read, and
    /// only where the host composed the writer.
    /// </summary>
    public RenderFragment? StripAction(DevbookPaneModeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Readable || Writer is not { } writer) return null;

        // No receiver: the pane's OpenNoteAsync re-renders the pane itself.
        var onCaptured = new EventCallback<BrainNoteWrite>(null, (Func<BrainNoteWrite, Task>)(written => OnCapturedAsync(written, context)));

        return builder =>
        {
            builder.OpenComponent<BrainCaptureDialog>(0);
            builder.AddComponentParameter(1, nameof(BrainCaptureDialog.Writer), writer);
            builder.AddComponentParameter(2, nameof(BrainCaptureDialog.OnCaptured), onCaptured);
            builder.CloseComponent();
        };
    }

    /// <summary>A note was captured: the pane opens it, on its inbox area's tab.</summary>
    private async Task OnCapturedAsync(BrainNoteWrite written, DevbookPaneModeContext context)
    {
        if (Brain is not { } brain || written.RelativePath is not { } path) return;

        // The inbox the writer filed it in, worked out the way the writer works
        // it out. On the pool: the areas are a disk walk, the Ignore list a read.
        var inbox = await Task.Run(() => BrainNoteText.InboxArea(brain.Areas(), BrainConventions.ReadIgnored(brain.BrainRoot)));
        await context.OpenNoteAsync(inbox.Folder, path);
    }

    public RenderFragment RenderTab(DevbookPaneModeTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        return builder =>
        {
            builder.OpenComponent<BrainDevbookPanel>(0);
            builder.AddComponentParameter(1, nameof(BrainDevbookPanel.Brain), Source);
            builder.AddComponentParameter(2, nameof(BrainDevbookPanel.SelectedPath), tab.SelectedPath);
            builder.AddComponentParameter(3, nameof(BrainDevbookPanel.OnOpenRepositoryChapter), tab.OnOpenRepositoryChapter);
            builder.AddComponentParameter(4, nameof(BrainDevbookPanel.TaskTitleFor), tab.TaskTitleFor);
            builder.AddComponentParameter(5, nameof(BrainDevbookPanel.OnOpenTask), tab.OnOpenTask);
            builder.CloseComponent();
        };
    }

    public RenderFragment RenderStatus(DevbookPaneModeStatus status, string? message) => builder =>
    {
        builder.OpenComponent<BrainPaneStatus>(0);
        builder.AddComponentParameter(1, nameof(BrainPaneStatus.Status), status);
        builder.AddComponentParameter(2, nameof(BrainPaneStatus.Message), message);
        builder.CloseComponent();
    };

    /// <summary>The Brain notes linking to the chapter on screen, for this reader
    /// only (local ADR 0027 §6).</summary>
    public RenderFragment? BelowRepositoryChapter(string repositoryAlias, string chapterPath, EventCallback<string> onOpenNote) => builder =>
    {
        builder.OpenComponent<BrainBacklinksBox>(0);
        builder.AddComponentParameter(1, nameof(BrainBacklinksBox.RepositoryAlias), repositoryAlias);
        builder.AddComponentParameter(2, nameof(BrainBacklinksBox.ChapterPath), chapterPath);
        builder.AddComponentParameter(3, nameof(BrainBacklinksBox.OnOpenNote), onOpenNote);
        builder.CloseComponent();
    };

    /// <summary>Every link the Brain holds into the repository, for the Domain
    /// view's badges and boxes (local ADR 0045), in Devbook's own words.</summary>
    public async Task<IReadOnlyList<DevbookChapterNote>> ChapterNotesAsync(string repositoryAlias, CancellationToken cancellationToken = default)
    {
        if (services.GetService<IBrainBacklinks>() is not { } backlinks) return [];

        var links = await backlinks.ChapterLinksAsync(repositoryAlias, cancellationToken);
        return [.. links.Select(link => new DevbookChapterNote(link.ChapterPath, link.Slug, new DevbookNote(link.Note.NotePath, link.Note.Title, link.Note.Heading)))];
    }

    /// <summary>The From your Brain box over notes the Domain view already read.</summary>
    public RenderFragment? NotesBox(string repositoryAlias, IReadOnlyList<DevbookNote> notes, EventCallback<string> onOpenNote) => builder =>
    {
        builder.OpenComponent<BrainBacklinksBox>(0);
        builder.AddComponentParameter(1, nameof(BrainBacklinksBox.RepositoryAlias), repositoryAlias);
        builder.AddComponentParameter(2, nameof(BrainBacklinksBox.Links), (IReadOnlyList<BrainBacklink>)[.. notes.Select(note => new BrainBacklink(note.NotePath, note.Title, note.Heading))]);
        builder.AddComponentParameter(3, nameof(BrainBacklinksBox.OnOpenNote), onOpenNote);
        builder.CloseComponent();
    };

    public Task<DevbookAtlasGraph> ReadAtlasAsync(CancellationToken cancellationToken = default) =>
        Brain is { } brain
            ? Atlas.ReadBrainAsync(brain, cancellationToken)
            : Task.FromResult(DevbookAtlasGraph.Unavailable(DevbookAtlasScope.Brain, BrainAtlas.NoBrainFolder));

    public DevbookRepositoryChapter? RepositoryChapterFor(string raw) =>
        BrainRepoLink.Parse(raw) is { } link ? new DevbookRepositoryChapter(link.Alias, link.Reference) : null;
}
