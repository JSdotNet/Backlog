namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A folder source that can make one chapter file go away at an exact point in a
/// reload — the moment between the catalog read and the chapter read — by acting
/// on the resolve each of them makes.
/// <para>
/// It used to record the chapter as a status write found it, too, so a panel's
/// flush-before-status ordering could be proved. The product writes no status into
/// an arc42, domain or design file any more — a chapter's workflow state is the
/// tool's under devbook's branch-true decision — so that half went with the write.
/// </para>
/// </summary>
internal sealed class RecordingDevbookFolderSource(IDevbookFolderSource inner, string chapterPath) : IDevbookFolderSource
{
    private int _breakChapterCountdown = -1;

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

    public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) => inner.Folders(repositoryAlias);

    public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null)
    {
        if (_breakChapterCountdown >= 0)
        {
            // The catalog's resolve is the first one after arming and is let by; the
            // chapter's is the second, and by then the catalog has already read the
            // file, so removing it here strands the chapter read on the empty result
            // DevbookChapterContent.None answers with.
            if (_breakChapterCountdown == 0 && File.Exists(chapterPath)) File.Delete(chapterPath);
            _breakChapterCountdown--;
        }

        return inner.Resolve(key, repositoryAlias);
    }

    public void NotifyContentChanged() => inner.NotifyContentChanged();
}
