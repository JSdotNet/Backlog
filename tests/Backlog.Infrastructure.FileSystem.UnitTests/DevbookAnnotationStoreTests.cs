using System.Text.Json;

using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Devbook.Abstractions;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// A person's remarks on Devbook chapters, on disk: one file per repository
/// under the storage folder, what survives a new store over the same folder,
/// what a push selects, and what a deletion leaves behind.
/// <para>
/// Real temp directories rather than a filesystem abstraction, for the reason
/// <see cref="WorkingHoursSettingsStoreTests"/> gives: the file is what is
/// under test.
/// </para>
/// </summary>
public class DevbookAnnotationStoreTests : IDisposable
{
    private const string Repository = "JSdotNet/Backlog";
    private const string Chapter = ".domain/devbook/features.md";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "devbook-annotation-store-tests-" + Guid.NewGuid().ToString("N"));

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 16, 9, 0, 0, TimeSpan.Zero));

    public DevbookAnnotationStoreTests() => Directory.CreateDirectory(_root);

    private DevbookAnnotationStore Store() => new(_root, _time);

    [Fact]
    public void A_fresh_remark_is_a_draft_on_the_block_it_was_asked_for()
    {
        var store = Store();

        var remark = store.Add(Repository, Chapter, 2, "DEV-TOWER");

        var listed = Assert.Single(store.List(Repository, Chapter));
        Assert.Equal(remark.Id, listed.Id);
        Assert.Equal(2, listed.BlockIndex);
        Assert.True(listed.IsDraft);
        Assert.Equal("DEV-TOWER", listed.Author);
        Assert.Equal(_time.GetUtcNow(), listed.CreatedAt);
    }

    /// <summary>
    /// The replica keeps whichever copy carries the later stamp, and stamps
    /// come from each device's own clock. A remark that arrived from a machine
    /// whose clock runs ahead therefore carries a stamp this machine's clock
    /// has not reached yet — and an edit stamped "now" here would be older
    /// than the copy it edits, refused as stale, and lost in silence. So a
    /// local change always lands after the stamp it changes, clock or no clock.
    /// </summary>
    [Fact]
    public void An_edit_of_a_remark_that_arrived_from_a_faster_clock_still_lands_after_it()
    {
        var store = Store();
        var ahead = _time.GetUtcNow().AddSeconds(30);
        var theirs = new DevbookAnnotation(Guid.NewGuid(), Repository, Chapter, 2, "Theirs", "OTHER-PC", ahead, ahead);
        store.Apply(theirs);

        store.Edit(theirs.Id, "Mine, a moment later on a slower clock.");

        var edited = Assert.Single(store.List(Repository, Chapter));
        Assert.True(edited.UpdatedAt > ahead);
        Assert.Contains(store.ListChangedSince(ahead), remark => remark.Id == theirs.Id);
    }

    /// <inheritdoc cref="An_edit_of_a_remark_that_arrived_from_a_faster_clock_still_lands_after_it"/>
    [Fact]
    public void A_deletion_of_a_remark_that_arrived_from_a_faster_clock_still_lands_after_it()
    {
        var store = Store();
        var ahead = _time.GetUtcNow().AddSeconds(30);
        var theirs = new DevbookAnnotation(Guid.NewGuid(), Repository, Chapter, 2, "Theirs", "OTHER-PC", ahead, ahead);
        store.Apply(theirs);

        store.Delete(theirs.Id);

        var tombstone = Assert.Single(store.ListChangedSince(ahead));
        Assert.Equal(theirs.Id, tombstone.Id);
        Assert.NotNull(tombstone.DeletedAt);
        Assert.True(tombstone.UpdatedAt > ahead);
    }

    [Fact]
    public void A_remark_survives_a_new_store_over_the_same_folder()
    {
        var first = Store();
        var remark = first.Add(Repository, Chapter, 2, "DEV-TOWER");
        first.Edit(remark.Id, "Say which team owns this.");

        // A second store over the same root is what the app is after a restart,
        // and what the panel is after the Router remounted the pane and a new
        // panel asked the same singleton — either way, the file answers.
        var second = Store();

        var listed = Assert.Single(second.List(Repository, Chapter));
        Assert.Equal("Say which team owns this.", listed.Body);
    }

    [Fact]
    public void Each_repository_gets_its_own_file_under_the_storage_folder()
    {
        var store = Store();

        store.Add(Repository, Chapter, 0, "DEV-TOWER");
        store.Add("JSdotNet/Other", Chapter, 0, "DEV-TOWER");

        var folder = Path.Combine(_root, DevbookAnnotationStore.FolderName);
        var files = Directory.GetFiles(folder, "*.json");

        Assert.Equal(2, files.Length);
        Assert.Contains(files, file => Path.GetFileName(file).StartsWith("JSdotNet-Backlog-", StringComparison.Ordinal));
        Assert.Contains(files, file => Path.GetFileName(file).StartsWith("JSdotNet-Other-", StringComparison.Ordinal));

        // And each file names its repository, so a person opening one knows
        // whose remarks they are looking at.
        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            Assert.False(string.IsNullOrEmpty(document.RootElement.GetProperty("repositoryAlias").GetString()));
        }
    }

    [Fact]
    public void Remarks_on_another_chapter_are_not_listed_for_this_one()
    {
        var store = Store();

        store.Add(Repository, Chapter, 0, "DEV-TOWER");
        store.Add(Repository, ".domain/devbook/domain.md", 0, "DEV-TOWER");

        Assert.Single(store.List(Repository, Chapter));
        Assert.Single(store.List(Repository, ".domain/devbook/domain.md"));
    }

    [Fact]
    public void An_unscoped_chapter_is_filed_under_the_empty_alias_every_time()
    {
        var store = Store();

        store.Add(null, Chapter, 0, "DEV-TOWER");

        Assert.Single(store.List(null, Chapter));
        Assert.Single(store.List(string.Empty, Chapter));
        Assert.Single(store.List("   ", Chapter));
    }

    [Fact]
    public void Editing_and_resolving_stamp_the_remark_for_the_next_push()
    {
        var store = Store();
        var remark = store.Add(Repository, Chapter, 0, "DEV-TOWER");
        var created = _time.GetUtcNow();

        _time.Advance(TimeSpan.FromMinutes(1));
        store.Edit(remark.Id, "Worth flagging before this ships.");

        _time.Advance(TimeSpan.FromMinutes(1));
        store.SetResolved(remark.Id, true);

        var stored = Assert.Single(store.List(Repository, Chapter));
        Assert.Equal("Worth flagging before this ships.", stored.Body);
        Assert.True(stored.Resolved);
        Assert.Equal(created, stored.CreatedAt);
        Assert.Equal(created.AddMinutes(2), stored.UpdatedAt);
    }

    [Fact]
    public void A_push_selects_what_changed_after_the_watermark_and_never_a_draft()
    {
        var store = Store();
        var draft = store.Add(Repository, Chapter, 0, "DEV-TOWER");

        _time.Advance(TimeSpan.FromMinutes(1));
        var written = store.Add(Repository, Chapter, 1, "DEV-TOWER");
        store.Edit(written.Id, "Written.");

        // The watermark a push leaves is the stamp of the last document it sent,
        // never the clock — and an edit in the same tick as its own creation is
        // stamped one tick past it, so the two are not the same instant here.
        var watermark = store.Find(written.Id)!.UpdatedAt;

        _time.Advance(TimeSpan.FromMinutes(1));
        var later = store.Add(Repository, Chapter, 2, "DEV-TOWER");
        store.Edit(later.Id, "Later.");

        var everything = store.ListChangedSince(DateTimeOffset.MinValue);
        Assert.DoesNotContain(everything, annotation => annotation.Id == draft.Id);
        Assert.Equal([written.Id, later.Id], everything.Select(annotation => annotation.Id));

        var since = store.ListChangedSince(watermark);
        Assert.Equal([later.Id], since.Select(annotation => annotation.Id));
    }

    [Fact]
    public void Deleting_a_draft_removes_it_outright()
    {
        var store = Store();
        var draft = store.Add(Repository, Chapter, 0, "DEV-TOWER");

        store.Delete(draft.Id);

        Assert.Empty(store.List(Repository, Chapter));
        Assert.Null(store.Find(draft.Id));
        Assert.Empty(store.ListChangedSince(DateTimeOffset.MinValue));
    }

    [Fact]
    public void Deleting_a_written_remark_leaves_a_tombstone_for_the_push()
    {
        var store = Store();
        var remark = store.Add(Repository, Chapter, 0, "DEV-TOWER");
        store.Edit(remark.Id, "Written.");

        _time.Advance(TimeSpan.FromMinutes(1));
        store.Delete(remark.Id);

        // Gone from the chapter, still in the store, and selected by a push.
        Assert.Empty(store.List(Repository, Chapter));

        var tombstone = Assert.Single(store.ListChangedSince(DateTimeOffset.MinValue));
        Assert.Equal(remark.Id, tombstone.Id);
        Assert.Equal(_time.GetUtcNow(), tombstone.DeletedAt);
        Assert.Equal(_time.GetUtcNow(), tombstone.UpdatedAt);

        // And it stays a tombstone: a second delete, an edit or a resolve
        // against it changes nothing.
        store.Delete(remark.Id);
        store.Edit(remark.Id, "Back again?");
        Assert.Equal("Written.", store.Find(remark.Id)!.Body);
    }

    [Fact]
    public void Applying_a_replicated_remark_writes_it_stamps_and_all()
    {
        var store = Store();
        var elsewhere = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var replicated = new DevbookAnnotation(
            Guid.NewGuid(), Repository, Chapter, 3, "From the laptop.", "DEV-LAPTOP", elsewhere, elsewhere.AddMinutes(5));

        store.Apply(replicated);

        var listed = Assert.Single(store.List(Repository, Chapter));
        Assert.Equal(replicated, listed);

        // The stamps are the other device's, so a push after them does not
        // send the remark back where it came from.
        Assert.Empty(store.ListChangedSince(elsewhere.AddMinutes(5)));
    }

    [Fact]
    public void Every_write_says_so()
    {
        var store = Store();
        var raised = 0;
        store.Changed += () => raised++;

        var remark = store.Add(Repository, Chapter, 0, "DEV-TOWER");
        store.Edit(remark.Id, "Written.");
        store.SetResolved(remark.Id, true);
        store.Delete(remark.Id);

        Assert.Equal(4, raised);
    }

    [Fact]
    public void A_file_nobody_can_read_reads_as_empty_rather_than_failing()
    {
        var folder = Path.Combine(_root, DevbookAnnotationStore.FolderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "broken.json"), "{ not json");

        var store = Store();

        Assert.Empty(store.List(Repository, Chapter));

        // And a write to a repository still lands in that repository's file.
        store.Add(Repository, Chapter, 0, "DEV-TOWER");
        Assert.Single(Store().List(Repository, Chapter));
    }

    [Fact]
    public void The_store_follows_the_root_it_is_pointed_at()
    {
        var other = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(other);
        var root = _root;
        var store = new DevbookAnnotationStore(() => root, _time);

        store.Add(Repository, Chapter, 0, "DEV-TOWER");
        Assert.Single(store.List(Repository, Chapter));

        // The backlog moved. The remarks in the old root stay there — a move
        // copies the owned folders, and that is WorkspaceSettingsStore's to do,
        // not this store's — and the store now reads the new place.
        root = other;
        Assert.Empty(store.List(Repository, Chapter));
        Assert.Equal(Path.Combine(other, DevbookAnnotationStore.FolderName), store.Directory);
    }

    [Fact]
    public void The_annotation_folder_is_one_a_move_carries()
    {
        Assert.Contains(DevbookAnnotationStore.FolderName, WorkspaceSettingsStore.OwnedRootFolders);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
