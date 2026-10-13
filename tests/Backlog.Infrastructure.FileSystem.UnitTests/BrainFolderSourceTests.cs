using Backlog.Infrastructure.Devbook;
using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The Brain as a knowledge folder (local ADR 0026): one local folder keyed
/// <c>brain</c>, always editable, picked by the person and never defaulted.
/// </summary>
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

    [Fact]
    public void Content_changed_reaches_open_panels()
    {
        var source = Source();
        var raised = 0;
        source.Changed += () => raised++;

        source.NotifyContentChanged();

        Assert.Equal(1, raised);
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
