namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Which chapter the Devbook pane has open right now, for whoever needs to know
/// without being the pane.
/// </summary>
/// <remarks>
/// <para>
/// The pane keeps the selection itself — one remembered chapter per section, in
/// a dictionary that is the pane's own and resets when the pane does — and that
/// stays exactly as it was. What this adds is a mirror of the one chapter that
/// is actually on screen, written after every render and cleared when the pane
/// goes, so <see cref="DevbookAiContentSource"/> can pin it without reaching
/// into a component's private fields. A pane that published its whole
/// dictionary would be lifting its behaviour along with its state, and the
/// behaviour is not what anybody else wanted.
/// </para>
/// <para>
/// Plain properties rather than an event: the reader of this asks at the moment
/// a question is sent, and nothing needs to be told when the chapter moves.
/// </para>
/// </remarks>
public sealed class DevbookOpenChapter
{
    /// <summary>The repository whose devbook the pane is reading, or null while
    /// no pane is on screen.</summary>
    public string? RepositoryAlias { get; private set; }

    /// <summary>The section the open chapter belongs to, as the area catalog
    /// keys it — <c>arc42</c>, <c>domain</c> — or null.</summary>
    public string? AreaKey { get; private set; }

    /// <summary>The chapter beneath that section's folder, as the menu spells it,
    /// or null when the section shows no single chapter — the Technology graph,
    /// a context overview, the atlas.</summary>
    public string? ChapterPath { get; private set; }

    /// <summary>Whether there is a chapter to pin at all.</summary>
    public bool HasChapter => !string.IsNullOrWhiteSpace(AreaKey) && !string.IsNullOrWhiteSpace(ChapterPath);

    /// <summary>What the pane is showing after this render. Called by the pane
    /// and nothing else.</summary>
    public void Set(string? repositoryAlias, string? areaKey, string? chapterPath)
    {
        RepositoryAlias = string.IsNullOrWhiteSpace(repositoryAlias) ? null : repositoryAlias;
        AreaKey = string.IsNullOrWhiteSpace(areaKey) ? null : areaKey;
        ChapterPath = string.IsNullOrWhiteSpace(chapterPath) ? null : chapterPath;
    }

    /// <summary>The pane has gone. A chapter left here after that would pin
    /// something the reader can no longer see.</summary>
    public void Clear() => Set(null, null, null);
}
