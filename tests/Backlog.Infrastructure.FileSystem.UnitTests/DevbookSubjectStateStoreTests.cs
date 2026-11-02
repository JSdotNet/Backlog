using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Devbook.Abstractions;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The workflow state of devbook subjects, on disk: one file per repository under
/// <c>devbook-subjects</c> in the storage folder, what survives a new store over
/// the same folder, what a push selects, and that a refused operation leaves the
/// file as it was.
/// <para>
/// Real temp directories rather than a filesystem abstraction, as
/// <see cref="DevbookAnnotationStoreTests"/> does: the file is what is under test.
/// </para>
/// </summary>
public sealed class DevbookSubjectStateStoreTests : IDisposable
{
    private const string Repository = "JSdotNet/Backlog";
    private const string Host = "DEV-TOWER";
    private const string Fingerprint = "sha256:2b11c532";

    private static readonly DevbookSubject Debt = DevbookSubject.Debt(".devbook/arc42/tdr/0001-archify.md");
    private static readonly DevbookSubject Change = DevbookSubject.Proposal("pay-by-card");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "devbook-subject-store-tests-" + Guid.NewGuid().ToString("N"));

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero));

    public DevbookSubjectStateStoreTests() => Directory.CreateDirectory(_root);

    private DevbookSubjectStateStore Store() => new(_root, _time);

    private string Folder => Path.Combine(_root, DevbookSubjectStateStore.FolderName);

    [Fact]
    public void A_state_survives_a_new_store_over_the_same_folder()
    {
        Store().SetState(Repository, Debt, "planned", Host);

        var state = Store().Get(Repository, Debt);

        Assert.NotNull(state);
        Assert.Equal("planned", state.State);
        Assert.Equal(Host, state.By);
        Assert.Equal(_time.GetUtcNow(), state.At);
        Assert.Equal(DevbookSubject.IdFor(Repository, Debt), state.Id);
    }

    [Fact]
    public void A_proposals_decisions_survive_a_new_store_with_their_fingerprints()
    {
        var store = Store();
        store.Approve(Repository, Change, Fingerprint, Host);
        _time.Advance(TimeSpan.FromMinutes(5));
        store.Accept(Repository, Change, Fingerprint, "OTHER-PC");

        var state = Store().Get(Repository, Change)!;

        Assert.Equal("accepted", state.State);
        Assert.Equal(new DevbookSubjectDecision(Host, _time.GetUtcNow().AddMinutes(-5), Fingerprint), state.Approval);
        Assert.Equal(new DevbookSubjectDecision("OTHER-PC", _time.GetUtcNow(), Fingerprint), state.Acceptance);
    }

    [Fact]
    public void Each_repository_keeps_its_own_file()
    {
        var store = Store();

        store.SetState(Repository, Debt, "planned", Host);
        store.SetState("JSdotNet/devbook", Debt, "identified", Host);

        Assert.Equal(2, Directory.GetFiles(Folder, "*.json").Length);
        Assert.Equal("planned", Assert.Single(store.List(Repository)).State);
        Assert.Equal("identified", Assert.Single(store.List("JSdotNet/devbook")).State);
    }

    [Fact]
    public void A_subject_with_no_state_reads_as_none()
    {
        Assert.Null(Store().Get(Repository, Change));
        Assert.Empty(Store().List(Repository));
    }

    [Fact]
    public void A_file_nobody_can_read_counts_as_empty()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "broken.json"), "{ not json");

        var store = Store();

        Assert.Empty(store.List(Repository));
        Assert.False(store.SetState(Repository, Debt, "planned", Host).IsRefused);
        Assert.Equal("planned", Store().Get(Repository, Debt)!.State);
    }

    [Fact]
    public void A_refused_operation_leaves_the_file_as_it_was_and_says_nothing()
    {
        var store = Store();
        store.SetState(Repository, Debt, "planned", Host);
        var file = Assert.Single(Directory.GetFiles(Folder, "*.json"));
        var before = File.ReadAllText(file);
        var raised = 0;
        store.Changed += () => raised++;
        _time.Advance(TimeSpan.FromMinutes(1));

        var outcome = store.SetState(Repository, Debt, "decided", Host);

        Assert.Equal(DevbookSubjectRefusal.BadState, outcome.Refused);
        Assert.Equal(before, File.ReadAllText(file));
        Assert.Equal(0, raised);
        Assert.Equal("planned", store.Get(Repository, Debt)!.State);
    }

    [Fact]
    public void A_refusal_on_a_subject_with_no_state_creates_no_file()
    {
        var outcome = Store().Accept(Repository, Change, Fingerprint, Host);

        Assert.Equal(DevbookSubjectRefusal.NotApproved, outcome.Refused);
        Assert.False(Directory.Exists(Folder) && Directory.GetFiles(Folder).Length > 0);
    }

    [Fact]
    public void A_write_is_announced()
    {
        var store = Store();
        var raised = 0;
        store.Changed += () => raised++;

        store.SetState(Repository, Debt, "planned", Host);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void A_replicated_state_keeps_the_stamps_it_arrived_with()
    {
        var elsewhere = new DateTimeOffset(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);
        var theirs = new DevbookSubjectState(
            DevbookSubject.IdFor(Repository, Debt), Repository, Debt, "in-progress", "OTHER-PC", elsewhere, null, null, elsewhere);

        Assert.True(Store().Apply(theirs));

        Assert.Equal(theirs with { RepositoryAlias = DevbookSubject.AliasKey(Repository) }, Store().Find(theirs.Id));
    }

    /// <summary>
    /// The newer-than check and the write are one step under the store's lock.
    /// Two steps — read, decide, write — let a state set here between the read and
    /// the write be overwritten by the older copy the pull was holding, and once
    /// the store holds that copy nothing would push the lost state again.
    /// </summary>
    [Fact]
    public void A_replicated_state_no_later_than_the_one_held_is_not_written()
    {
        var store = Store();
        store.SetState(Repository, Debt, "resolved", Host);
        var held = store.Get(Repository, Debt)!;
        var file = Assert.Single(Directory.GetFiles(Folder, "*.json"));
        var before = File.ReadAllText(file);
        var raised = 0;
        store.Changed += () => raised++;

        Assert.False(store.Apply(held with { State = "planned", UpdatedAt = held.UpdatedAt.AddMinutes(-1) }));
        Assert.False(store.Apply(held with { State = "planned" }));

        Assert.Equal("resolved", store.Get(Repository, Debt)!.State);
        Assert.Equal(before, File.ReadAllText(file));
        Assert.Equal(0, raised);
        Assert.True(store.Apply(held with { State = "planned", UpdatedAt = held.UpdatedAt.AddMinutes(1) }));
        Assert.Equal("planned", Store().Get(Repository, Debt)!.State);
    }

    /// <summary>The file map has always matched an alias without regard to case;
    /// the id has to as well, or one subject approved under one spelling and
    /// accepted under another would be two states in one file.</summary>
    [Fact]
    public void A_proposal_approved_under_one_spelling_of_the_alias_is_accepted_under_another()
    {
        var store = Store();

        store.Approve("JSdotNet/Backlog", Change, Fingerprint, Host);
        var accepted = store.Accept("jsdotnet/backlog", Change, Fingerprint, Host);

        Assert.False(accepted.IsRefused, accepted.Message);
        var state = Assert.Single(Store().List("JSDOTNET/Backlog"));
        Assert.Equal("accepted", state.State);
        Assert.Single(Directory.GetFiles(Folder, "*.json"));
    }

    /// <summary>JSON that parses but holds no list, or a hole in it, is a file
    /// with less in it — never a chapter that cannot open.</summary>
    [Theory]
    [InlineData("""{ "repositoryAlias": "jsdotnet/backlog", "subjects": null }""", 0)]
    [InlineData("""{ "repositoryAlias": "jsdotnet/backlog", "subjects": [ null ] }""", 0)]
    [InlineData("""{ "repositoryAlias": "jsdotnet/backlog", "subjects": [ null, { "id": "7d3c1f2a-9b8e-5c4d-8a6b-1e2f3a4b5c6d", "kind": "debt", "key": ".devbook/arc42/tdr/0001-archify.md", "state": "planned", "by": "OTHER-PC" } ] }""", 1)]
    public void A_file_with_no_list_or_a_hole_in_it_reads_as_what_it_holds(string json, int held)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "jsdotnet-backlog.json"), json);

        var store = Store();

        Assert.Equal(held, store.List(Repository).Count);
        Assert.Null(store.Get(Repository, Change));
    }

    /// <summary>The replica keeps whichever copy is stamped later, and stamps come
    /// from each device's clock — so a state set here on a copy that arrived from a
    /// faster clock still lands after it, or it would be refused as stale and lost
    /// in silence. The same rule the remark store keeps.</summary>
    [Fact]
    public void A_state_set_on_a_copy_from_a_faster_clock_still_lands_after_it()
    {
        var store = Store();
        var ahead = _time.GetUtcNow().AddSeconds(30);
        store.Apply(new DevbookSubjectState(
            DevbookSubject.IdFor(Repository, Debt), Repository, Debt, "planned", "OTHER-PC", ahead, null, null, ahead));

        store.SetState(Repository, Debt, "resolved", Host);

        Assert.True(store.Get(Repository, Debt)!.UpdatedAt > ahead);
    }

    [Fact]
    public void A_push_selects_what_changed_after_the_watermark_oldest_first()
    {
        var store = Store();
        var start = _time.GetUtcNow();
        store.SetState(Repository, Debt, "planned", Host);
        _time.Advance(TimeSpan.FromMinutes(1));
        store.SetState("JSdotNet/devbook", Change, "draft", Host);
        _time.Advance(TimeSpan.FromMinutes(1));
        store.Approve(Repository, Change, Fingerprint, Host);

        var changed = store.ListChangedSince(start);

        Assert.Equal(
            [("jsdotnet/devbook", "draft"), ("jsdotnet/backlog", "approved")],
            changed.Select(state => (state.RepositoryAlias, state.State)));
    }

    [Fact]
    public void A_moved_backlog_takes_the_subject_states_along() =>
        Assert.Contains(DevbookSubjectStateStore.FolderName, WorkspaceSettingsStore.OwnedRootFolders);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder left behind is not a test failure.
        }
    }
}
