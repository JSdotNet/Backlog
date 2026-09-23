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

    /// <summary>The same chapter, spelled the two ways a machine with
    /// <c>.arc42</c> pointed at <c>docs/arch</c> and a machine with the
    /// conventional folder each arrive at it by.</summary>
    private const string Decision = ".arc42/adr/0001-decision.md";

    private const string RelocatedDecision = "docs/arch/adr/0001-decision.md";

    public DevbookAnnotationStoreTests() => Directory.CreateDirectory(_root);

    /// <param name="folders">Where this machine has each knowledge folder
    /// pointed. Omitted — which is what every test predating the canonical
    /// chapter key does — the store reads the conventional folders, and the
    /// remarks those tests file are already named by them.</param>
    private DevbookAnnotationStore Store(IDevbookFolderSource? folders = null) => new(_root, _time, folders);

    /// <summary>A machine with <c>.arc42</c> pointed somewhere other than the
    /// conventional folder. Only the folder list is answered; nothing in this
    /// store resolves a folder to a place on disk.</summary>
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

    /// <summary>
    /// The defect this key exists to prevent. A remark written while this machine
    /// has <c>.arc42</c> pointed at <c>docs/arch</c> is filed under the
    /// conventional name, because that is the only name a second device — which
    /// may have the folder somewhere else again — can come looking with.
    /// </summary>
    [Fact]
    public void A_remark_on_a_relocated_folders_chapter_is_filed_under_the_conventional_name()
    {
        var store = Store(Arc42At("docs/arch"));

        var remark = store.Add(Repository, RelocatedDecision, 0, "DEV-TOWER");

        Assert.Equal(Decision, remark.ChapterPath);
        Assert.Single(store.List(Repository, Decision));
    }

    /// <summary>The question is canonicalized as well as the stored key, so the
    /// panel may go on asking in whatever spelling its area store hands it —
    /// which on this machine is the relocated one — and still be asking about the
    /// one chapter.</summary>
    [Fact]
    public void A_remark_is_found_by_either_spelling_of_the_chapter_it_is_on()
    {
        var store = Store(Arc42At("docs/arch"));

        store.Add(Repository, Decision, 0, "DEV-TOWER");

        Assert.Single(store.List(Repository, Decision));
        Assert.Single(store.List(Repository, RelocatedDecision));
    }

    /// <summary>
    /// What makes the key self-healing, and why nothing has to be pushed to fix a
    /// device that once wrote the old spelling: a remark arriving from another
    /// machine is filed under the name this one reads by, with every stamp
    /// exactly as it arrived. No stamp touched means nothing crosses the push
    /// watermark, so there is no storm and no refused re-offer.
    /// </summary>
    [Fact]
    public void A_remark_arriving_under_a_stale_spelling_is_filed_under_the_canonical_one()
    {
        var store = Store(Arc42At("docs/arch"));

        var elsewhere = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);
        var replicated = new DevbookAnnotation(
            Guid.NewGuid(), Repository, "arch/adr/0001-decision.md", 3, "From the laptop.", "DEV-LAPTOP", elsewhere, elsewhere);

        store.Apply(replicated);

        var listed = Assert.Single(store.List(Repository, Decision));
        Assert.Equal(replicated.Id, listed.Id);
        Assert.Equal(Decision, listed.ChapterPath);
        Assert.Equal(elsewhere, listed.CreatedAt);
        Assert.Equal(elsewhere, listed.UpdatedAt);
        Assert.Empty(store.ListChangedSince(elsewhere));
    }

    /// <summary>
    /// Remarks already on disk under the old spelling. They take the canonical
    /// name when the store reads them, in memory and nowhere else: no stamp
    /// moves, so no row crosses the push watermark, and the file itself is left
    /// alone until an ordinary write rewrites it whole. A startup that rewrote
    /// every repository file would be exactly the destructive automatic
    /// migration guideline 0014 rules out, and it would buy nothing.
    /// </summary>
    [Fact]
    public void Remarks_already_on_disk_take_the_canonical_name_when_the_store_reads_them()
    {
        // Written by the store as it behaved before the key was pinned: the
        // relocated spelling, straight through.
        var before = Store();
        var written = before.Add(Repository, RelocatedDecision, 1, "DEV-TOWER");
        before.Edit(written.Id, "Why this one and not the other?");
        var stamped = before.Find(written.Id)!;
        Assert.Equal(RelocatedDecision, stamped.ChapterPath);

        var store = Store(Arc42At("docs/arch"));

        var listed = Assert.Single(store.List(Repository, Decision));
        Assert.Equal(written.Id, listed.Id);
        Assert.Equal("Why this one and not the other?", listed.Body);

        // Not one stamp moved, so nothing is offered to a push that was not
        // already being offered.
        Assert.Equal(stamped.CreatedAt, listed.CreatedAt);
        Assert.Equal(stamped.UpdatedAt, listed.UpdatedAt);
        Assert.Null(listed.DeletedAt);
        Assert.Empty(store.ListChangedSince(stamped.UpdatedAt));

        // And nothing was written back: reading is not a migration pass.
        var file = Assert.Single(Directory.EnumerateFiles(store.Directory, "*.json"));
        Assert.Contains(RelocatedDecision, File.ReadAllText(file), StringComparison.Ordinal);
    }

    /// <summary>
    /// The case canonicalizing creates: two remarks filed under two spellings of
    /// one chapter now answer to one name. They are separate remarks with
    /// separate ids and both have to survive — a key that merged or dropped one
    /// would be losing the person's writing to fix their addressing.
    /// </summary>
    [Fact]
    public void Two_remarks_filed_under_two_spellings_of_one_chapter_both_survive()
    {
        var before = Store();
        var relocated = before.Add(Repository, RelocatedDecision, 0, "DEV-TOWER");
        before.Edit(relocated.Id, "Written before the folder moved back.");
        var conventional = before.Add(Repository, Decision, 1, "DEV-LAPTOP");
        before.Edit(conventional.Id, "Written on the other machine.");

        var store = Store(Arc42At("docs/arch"));

        var listed = store.List(Repository, Decision);
        Assert.Equal(2, listed.Count);
        Assert.Contains(listed, annotation => annotation.Id == relocated.Id);
        Assert.Contains(listed, annotation => annotation.Id == conventional.Id);
    }

    /// <summary>
    /// What <see cref="DevbookAnnotationStore.Add"/> hands back is what the store
    /// holds, under every configuration.
    /// <para>
    /// This is the one the first draft got wrong, and it is worth stating as a
    /// property rather than as a case. <c>Add</c> canonicalized the path and
    /// returned that, then the write canonicalized again — harmless for most
    /// layouts, but with <c>.arc42</c> at <c>docs/arch</c> and <c>.tech</c> at
    /// <c>.arc42/adr</c> the second pass moved the chapter on again, so the panel
    /// held <c>.arc42/adr/…</c> while the store held <c>.tech/…</c> and the
    /// remark vanished from the next refresh. One canonicalization, at one place,
    /// and the returned value read back out of storage rather than constructed
    /// beside it.
    /// </para>
    /// </summary>
    [Fact]
    public void What_add_hands_back_is_what_the_store_holds()
    {
        var store = Store(Configured((".arc42", "docs/arch"), (".tech", ".arc42/adr")));

        var remark = store.Add(Repository, RelocatedDecision, 0, "DEV-TOWER");

        Assert.Equal(Decision, remark.ChapterPath);
        Assert.Equal(remark.ChapterPath, store.Find(remark.Id)!.ChapterPath);

        var listed = Assert.Single(store.List(Repository, remark.ChapterPath));
        Assert.Equal(remark.Id, listed.Id);
        Assert.Equal(remark.ChapterPath, listed.ChapterPath);
    }

    /// <summary>The same property for the write replication makes: nothing is
    /// canonicalized on the way in and again on the way down.</summary>
    [Fact]
    public void An_applied_remark_is_canonicalized_once_under_a_nested_configuration()
    {
        var store = Store(Configured((".arc42", "docs/arch"), (".tech", ".arc42/adr")));

        var elsewhere = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);
        var id = Guid.NewGuid();
        store.Apply(new DevbookAnnotation(
            id, Repository, RelocatedDecision, 0, "From the laptop.", "DEV-LAPTOP", elsewhere, elsewhere));

        Assert.Equal(Decision, store.Find(id)!.ChapterPath);
        Assert.Single(store.List(Repository, Decision));
    }

    /// <summary>A tombstone stays dead. Canonicalizing touches the address and
    /// nothing else, so a deleted remark is not re-offered to the reader by
    /// having been renamed.</summary>
    [Fact]
    public void A_deleted_remark_stays_deleted_under_its_canonical_name()
    {
        var before = Store();
        var remark = before.Add(Repository, RelocatedDecision, 0, "DEV-TOWER");
        before.Edit(remark.Id, "Struck out later.");
        before.Delete(remark.Id);
        var tombstone = before.Find(remark.Id)!;

        var store = Store(Arc42At("docs/arch"));

        Assert.Empty(store.List(Repository, Decision));
        Assert.Empty(store.List(Repository, RelocatedDecision));

        var found = store.Find(remark.Id);
        Assert.NotNull(found);
        Assert.Equal(Decision, found.ChapterPath);
        Assert.Equal(tombstone.DeletedAt, found.DeletedAt);
        Assert.Equal(tombstone.UpdatedAt, found.UpdatedAt);
        Assert.Empty(store.ListChangedSince(tombstone.UpdatedAt));
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
