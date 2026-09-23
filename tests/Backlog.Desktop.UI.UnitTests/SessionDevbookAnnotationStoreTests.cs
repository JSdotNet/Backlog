using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The fallback store, on the one thing it shares with the file-backed one: a
/// chapter is named the same way whichever of the two is composed.
/// <para>
/// Worth pinning here rather than only through a panel, because this store is
/// what the storybook and most of the panel tests render against, and a key that
/// only the desktop's real store canonicalized would make those two disagree
/// about what a remark is filed under.
/// </para>
/// </summary>
public sealed class SessionDevbookAnnotationStoreTests
{
    private const string Decision = ".arc42/adr/0001-decision.md";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 16, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_remark_on_a_relocated_folders_chapter_is_filed_under_the_conventional_name()
    {
        var store = new SessionDevbookAnnotationStore(_time, Arc42At("docs/arch"));

        var remark = store.Add("JSdotNet/Backlog", "docs/arch/adr/0001-decision.md", 0, "DEV-TOWER");

        Assert.Equal(Decision, remark.ChapterPath);
        Assert.Single(store.List("JSdotNet/Backlog", Decision));
    }

    /// <summary>Replication's write is canonicalized on arrival — that is what
    /// spares every device a forced push — and its stamps are not, because the
    /// merge has already decided this version wins on them.</summary>
    [Fact]
    public void A_remark_arriving_under_a_stale_spelling_is_filed_under_the_canonical_one()
    {
        var store = new SessionDevbookAnnotationStore(_time, Arc42At("D:/knowledge/arch"));

        var elsewhere = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);
        var replicated = new DevbookAnnotation(
            Guid.NewGuid(), "JSdotNet/Backlog", "arch/adr/0001-decision.md", 2, "From the laptop.", "DEV-LAPTOP", elsewhere, elsewhere);

        store.Apply(replicated);

        var listed = Assert.Single(store.List("JSdotNet/Backlog", Decision));
        Assert.Equal(replicated.Id, listed.Id);
        Assert.Equal(elsewhere, listed.UpdatedAt);
        Assert.Empty(store.ListChangedSince(elsewhere));
    }

    /// <summary>Composed with nothing to ask — the storybook, and every panel test
    /// that lets the panel build its own — it still reads the conventional
    /// folders, which is the right answer for a repository that has not moved
    /// one.</summary>
    [Fact]
    public void With_no_folder_source_the_conventional_folders_are_still_read()
    {
        var store = new SessionDevbookAnnotationStore(_time);

        store.Add("JSdotNet/Backlog", "arc42/adr/0001-decision.md", 0, "DEV-TOWER");

        Assert.Single(store.List("JSdotNet/Backlog", Decision));
    }

    /// <summary>
    /// What <see cref="SessionDevbookAnnotationStore.Add"/> hands back is what the
    /// store holds. The configuration is the one that caught the first draft:
    /// <c>.arc42</c> at <c>docs/arch</c> with <c>.tech</c> nested at
    /// <c>.arc42/adr</c>, where canonicalizing twice moved the chapter on a second
    /// time and left the panel holding a path the store did not have.
    /// </summary>
    [Fact]
    public void What_add_hands_back_is_what_the_store_holds()
    {
        var store = new SessionDevbookAnnotationStore(_time, Configured((".arc42", "docs/arch"), (".tech", ".arc42/adr")));

        var remark = store.Add("JSdotNet/Backlog", "docs/arch/adr/0001-decision.md", 0, "DEV-TOWER");

        Assert.Equal(Decision, remark.ChapterPath);
        Assert.Equal(remark.ChapterPath, store.Find(remark.Id)!.ChapterPath);
        Assert.Single(store.List("JSdotNet/Backlog", remark.ChapterPath));
    }

    private static IDevbookFolderSource Arc42At(string path) => Configured((".arc42", path));

    /// <summary>A machine with the named areas pointed somewhere other than their
    /// conventional folders.</summary>
    private static IDevbookFolderSource Configured(params (string Key, string Path)[] overrides) =>
        new StubFolderSource(
        [
            .. DevbookFolderSetting.Defaults().Select(folder =>
                overrides.FirstOrDefault(o => string.Equals(o.Key, folder.Key, StringComparison.OrdinalIgnoreCase)) is { Path: not null } match
                    ? folder with { Path = match.Path }
                    : folder)
        ]);

    private sealed class StubFolderSource(IReadOnlyList<DevbookFolderSetting> folders) : IDevbookFolderSource
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public void NotifyContentChanged()
        {
        }

        public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) => folders;

        public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null) =>
            DevbookFolderLocation.Unavailable(key, "Nothing here asks where the folder is.");
    }
}
