namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Records what a chapter file said at the moment a knowledge store went looking
/// for its folder — which is the moment before it writes a status into it.
/// <para>
/// The three panels that put a status selector beside an open editing surface owe
/// the editor a flush before that write, and the settled file cannot answer
/// whether they paid it: the writer's status merge repairs either ordering, so both
/// orders can leave the same bytes behind. What does answer it is the file as the
/// status write found it. A body that had already been typed means the flush went
/// first, and every store resolves its folder immediately before it writes, so that
/// call is the observation point.
/// </para>
/// <para>
/// Armed rather than always recording, because the same folder is resolved to load
/// with as well. Arming happens right before the gesture under test, so the first
/// snapshot after it is the one that belongs to that gesture.
/// </para>
/// </summary>
internal sealed class RecordingKnowledgeFolderSource(IKnowledgeFolderSource inner, string chapterPath) : IKnowledgeFolderSource
{
    private bool _armed;
    private int _breakChapterCountdown = -1;

    /// <summary>The chapter as it was when the first resolve after arming happened,
    /// or null when nothing resolved at all.</summary>
    internal string? ChapterWhenStatusWasWritten { get; private set; }

    internal void ArmStatusWriteSnapshot() => _armed = true;

    /// <summary>
    /// Arms the momentary-unreadable window the read-only fallback exists for: a
    /// same-path reload whose catalog read still lists the open chapter, but whose
    /// chapter read a moment later does not.
    /// <para>
    /// Every reload builds the catalog before it reads the one open chapter, and
    /// both reads reach for the folder through this source — the catalog's resolve
    /// first, the chapter's second. Removing the file on that second resolve, after
    /// the catalog has already read it and before the chapter read reaches for it,
    /// is a file locked, deleted or made unreadable between the two reads with none
    /// of the race that would make it flaky.
    /// </para>
    /// </summary>
    internal void BreakChapterOnNextReload() => _breakChapterCountdown = 1;

    public event Action? Changed
    {
        add => inner.Changed += value;
        remove => inner.Changed -= value;
    }

    public string StorageDirectory => inner.StorageDirectory;

    public IReadOnlyList<KnowledgeFolderSetting> Folders(string? repositoryAlias) => inner.Folders(repositoryAlias);

    public KnowledgeFolderLocation Resolve(string key, string? repositoryAlias = null)
    {
        if (_armed && ChapterWhenStatusWasWritten is null && File.Exists(chapterPath))
        {
            ChapterWhenStatusWasWritten = File.ReadAllText(chapterPath);
        }

        if (_breakChapterCountdown >= 0)
        {
            // The catalog's resolve is the first one after arming and is let by; the
            // chapter's is the second, and by then the catalog has already read the
            // file, so removing it here strands the chapter read on the empty result
            // KnowledgeChapterContent.None answers with.
            if (_breakChapterCountdown == 0 && File.Exists(chapterPath)) File.Delete(chapterPath);
            _breakChapterCountdown--;
        }

        return inner.Resolve(key, repositoryAlias);
    }

    public void NotifyContentChanged() => inner.NotifyContentChanged();
}
