using Backlog.Infrastructure.Devbook;
using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The Brain as a devbook folder (local ADR 0026): one local folder keyed
/// <c>brain</c>, always editable, picked by the person and never defaulted.
/// </summary>
[Collection(BrainRootCollection.Name)]
public class BrainFolderSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "brain-folder-source-tests-" + Guid.NewGuid().ToString("N"));

    private string? _setting;

    private string Brain => Path.Combine(_root, "OneDrive", "Brain");

    private BrainFolderSource Source() => new(() => _setting);

    [Fact]
    public void No_folder_set_is_no_brain_and_no_default()
    {
        var location = Source().Resolve(BrainConventions.FolderKey);

        Assert.False(location.Available);
        Assert.Equal(BrainFolderSource.NoBrainFolder, location.Message);
        Assert.Null(location.FullPath);
        Assert.Null(Source().BrainRoot);
    }

    /// <summary>The pane's cheap look for a folder that went away: present only
    /// while a folder is set and is on disk.</summary>
    [Fact]
    public void The_brain_folder_is_present_only_while_set_and_on_disk()
    {
        Assert.False(Source().BrainFolderPresent());

        _setting = Brain;
        Assert.False(Source().BrainFolderPresent());

        Directory.CreateDirectory(Brain);
        Assert.True(Source().BrainFolderPresent());

        Directory.Delete(Brain);
        Assert.False(Source().BrainFolderPresent());
    }

    /// <summary>The Brain's folder setting has one definition, in
    /// <see cref="BrainConventions"/>, so the assistant tools — which cannot reference
    /// this project — and this source cannot describe it two ways.</summary>
    [Fact]
    public void The_brain_folder_is_the_one_its_conventions_define()
    {
        _setting = null;

        Assert.Same(BrainConventions.Folder, BrainFolderSource.Folder);
        Assert.Same(BrainConventions.Folder, Assert.Single(Source().Folders(null)));
        Assert.Equal(BrainConventions.ScopeLabel, BrainFolderSource.ScopeLabel);
        Assert.Equal(BrainConventions.Folder, Source().Resolve(BrainConventions.FolderKey).Folder);
    }

    [Fact]
    public void A_set_folder_resolves_local_and_always_editable()
    {
        Directory.CreateDirectory(Brain);
        _setting = Brain + Path.DirectorySeparatorChar;

        var location = Source().Resolve("Brain");

        Assert.True(location.Available);
        Assert.Equal(BrainConventions.FolderKey, location.Key);
        Assert.Equal(DevbookSourceKind.LocalFolder, location.Source);
        Assert.True(location.CanEdit);
        Assert.Equal(Brain, location.FullPath);
        Assert.Equal(Brain, location.RootPath);
        Assert.Equal(BrainFolderSource.ScopeLabel, location.ScopeLabel);
        Assert.Null(location.RepositoryAlias);
        Assert.Equal(Brain, location.Folder?.EffectivePath);
    }

    [Fact]
    public void A_resolved_brain_is_recorded_so_its_own_database_is_found()
    {
        Directory.CreateDirectory(Brain);
        _setting = Brain;

        Source().Resolve(BrainConventions.FolderKey);

        Assert.True(DevbookDatabaseLocation.IsBrainRoot(Brain));
        Assert.Equal(DevbookCorpusKind.Brain, DevbookDatabaseLocation.KindFor(Brain));
    }

    [Fact]
    public void A_missing_folder_says_where_it_looked()
    {
        _setting = Brain;

        var location = Source().Resolve(BrainConventions.FolderKey);

        Assert.False(location.Available);
        Assert.Contains(Brain, location.Message, StringComparison.Ordinal);
        Assert.Equal(Brain, location.FullPath);
    }

    [Fact]
    public void A_relative_folder_is_not_a_brain()
    {
        _setting = Path.Combine("relative", "Brain");

        var location = Source().Resolve(BrainConventions.FolderKey);

        Assert.False(location.Available);
        Assert.Null(Source().BrainRoot);
    }

    [Fact]
    public void A_moved_brain_leaves_its_old_folder_a_plain_folder()
    {
        var old = Path.Combine(_root, "Old");
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(Brain);

        _setting = old;
        Source().Resolve(BrainConventions.FolderKey);
        _setting = Brain;
        Source().Resolve(BrainConventions.FolderKey);

        Assert.True(DevbookDatabaseLocation.IsBrainRoot(Brain));
        Assert.False(DevbookDatabaseLocation.IsBrainRoot(old));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void The_brain_answers_the_unscoped_and_the_brain_scope(string? alias)
    {
        Assert.Equal([BrainConventions.FolderKey], Source().Folders(alias).Select(folder => folder.Key));
    }

    [Fact]
    public void A_repository_alias_is_not_the_brain()
    {
        Directory.CreateDirectory(Brain);
        _setting = Brain;

        Assert.Empty(Source().Folders("backlog"));
        Assert.False(Source().Resolve(BrainConventions.FolderKey, "backlog").Available);
    }

    [Fact]
    public void Another_key_is_not_a_brain_folder()
    {
        Directory.CreateDirectory(Brain);
        _setting = Brain;

        Assert.False(Source().Resolve(".arc42").Available);
    }

    [Fact]
    public async Task Preparing_a_local_brain_needs_nothing()
    {
        Directory.CreateDirectory(Brain);
        _setting = Brain;
        IDevbookFolderSource source = Source();

        var listing = await source.PrepareListingAsync(BrainConventions.FolderKey, cancellationToken: TestContext.Current.CancellationToken);
        var content = await source.PrepareContentAsync(BrainConventions.FolderKey, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(listing.Available);
        Assert.True(content.Available);
        Assert.Same(DevbookDiskFileTree.Instance, source.FileTree(listing));
    }

    [Fact]
    public void The_areas_come_from_the_brain_s_conventions()
    {
        Directory.CreateDirectory(Brain);
        File.WriteAllText(Path.Combine(Brain, BrainConventions.FileName), "## Areas\n\n- Work — `work`\n- Home — `home`\n");
        _setting = Brain;

        Assert.Equal(["work", "home"], Source().Areas().Select(area => area.Folder));

        _setting = null;
        Assert.Equal(BrainConventions.Para, Source().Areas());
    }

    /// <summary>A folder an agent made shows at once, once it holds a note; an
    /// ignored one does not, and Show hidden asks for it separately.</summary>
    [Fact]
    public void The_areas_are_the_declared_ones_then_the_folders_holding_a_note()
    {
        Directory.CreateDirectory(Path.Combine(Brain, "Meetings", "2026"));
        Directory.CreateDirectory(Path.Combine(Brain, "_templates"));
        Directory.CreateDirectory(Path.Combine(Brain, ".obsidian"));
        Directory.CreateDirectory(Path.Combine(Brain, "Demo"));
        File.WriteAllText(Path.Combine(Brain, BrainConventions.FileName), "## Areas\n\n- Inbox — `_inbox`\n\n## Ignore\n\n- `_templates`\n");
        File.WriteAllText(Path.Combine(Brain, "Meetings", "2026", "standup.md"), "# Standup\n");
        File.WriteAllText(Path.Combine(Brain, "_templates", "meeting.md"), "# Meeting\n");
        _setting = Brain;

        Assert.Equal(
            [new BrainArea("Inbox", "_inbox", 1), new BrainArea("Meetings", "Meetings", 2)],
            Source().Areas());
        Assert.Equal(
            [new BrainArea("Obsidian", ".obsidian", 1), new BrainArea("Templates", "_templates", 2)],
            Source().HiddenAreas());

        _setting = null;
        Assert.Empty(Source().HiddenAreas());
    }

    [Fact]
    public void Content_changed_reaches_open_panels()
    {
        var source = Source();
        var raised = 0;
        source.Changed += () => raised++;

        source.NotifyContentChanged();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void The_brain_is_offered_to_the_pane_through_its_own_port()
    {
        Directory.CreateDirectory(Brain);
        _setting = Brain;

        IBrainFolderSource source = Source();

        Assert.Equal(Brain, source.BrainRoot);
        Assert.Equal(BrainConventions.Para, source.Areas());
    }

    [Fact]
    public void A_onedrive_conflict_copy_is_listed_with_its_original()
    {
        var original = Write("projects/plan.md", "# Plan\n\nMine.\n");
        var copy = Write("projects/plan-LAPTOP.md", "# Plan\n\nTheirs.\n");
        Write("projects/plan-2.md", "# Plan two\n");
        Write("README.md", "# Brain\n");
        _setting = Brain;

        var pairs = Source().ConflictCopies();

        Assert.Equal([new BrainConflictCopy("projects/plan.md", "projects/plan-LAPTOP.md")], pairs);
        Assert.True(File.Exists(original));
        Assert.True(File.Exists(copy));
    }

    [Fact]
    public void Listing_conflict_copies_changes_neither_file()
    {
        var original = Write("plan.md", "# Plan\n\nMine.\n");
        var copy = Write("plan-LAPTOP.md", "# Plan\n\nTheirs.\n");
        var stamp = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(original, stamp);
        File.SetLastWriteTimeUtc(copy, stamp);
        var originalBytes = File.ReadAllBytes(original);
        var copyBytes = File.ReadAllBytes(copy);
        _setting = Brain;

        Assert.Single(Source().ConflictCopies());

        Assert.Equal(originalBytes, File.ReadAllBytes(original));
        Assert.Equal(copyBytes, File.ReadAllBytes(copy));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(original));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(copy));
        Assert.Equal(["plan-LAPTOP.md", "plan.md"], Directory.GetFiles(Brain).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_dot_folder_holds_no_conflict_copy()
    {
        Write(".obsidian/plan.md", "x");
        Write(".obsidian/plan-LAPTOP.md", "x");
        _setting = Brain;

        Assert.Empty(Source().ConflictCopies());
    }

    [Fact]
    public void An_unset_or_missing_brain_has_no_conflict_copies()
    {
        Assert.Empty(Source().ConflictCopies());

        _setting = Brain;
        Assert.Empty(Source().ConflictCopies());
    }

    private string Write(string relativePath, string text)
    {
        var file = Path.Combine(Brain, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        return file;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
