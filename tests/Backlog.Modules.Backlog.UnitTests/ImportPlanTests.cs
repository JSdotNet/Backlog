using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.Abstractions;
using Backlog.Modules.Backlog.Abstractions.DataTransferObjects;
using Backlog.Modules.Backlog.DomainModels;
using Backlog.Modules.Backlog.Features.ImportPlan;

namespace Backlog.Modules.Backlog.UnitTests;

/// <summary>
/// Per ADR 0004, Import is a use case over the ordinary entry-text grammar: a
/// plan is a block of text naming more than one entry, split and parsed exactly
/// as a hand-typed multi-entry paste would be, with two-pass dependency
/// resolution and upsert-by-<c>(import_plan_id, import_item_id)</c> on top.
/// These tests drive <see cref="ImportPlanCommandHandler"/> directly against an
/// in-memory repository, the same style <c>RecurringTaskTests</c> uses.
/// </summary>
public sealed class ImportPlanTests
{
    [Fact]
    public async Task A_multi_entry_document_creates_one_entry_per_prompt()
    {
        var store = new InMemoryBacklogRepository();

        const string plan =
            "# First prompt\n`prompt` `#myplan`\n\nDo the first thing.\n\n"
            + "# Second prompt\n`prompt` `#myplan`\n\nDo the second thing.\n";

        var result = await Import(store, plan);

        Assert.Equal(2, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(2, store.Entries.Count);
        Assert.Equal(["First prompt", "Second prompt"], result.Entries.Select(e => e.Title));
    }

    /// <summary>
    /// Neither prompt has a real id when the document is parsed — see ADR 0004's
    /// two-pass rule. The first prompt names the second as a dependency before
    /// the second has been created, so the dependency can only resolve once both
    /// entries exist.
    /// </summary>
    [Fact]
    public async Task A_dependency_on_a_sibling_not_yet_created_resolves_to_its_real_id()
    {
        var store = new InMemoryBacklogRepository();

        const string plan =
            "# First prompt\n`prompt` `#myplan` `id:first` `after:second`\n\n"
            + "# Second prompt\n`prompt` `#myplan` `id:second`\n";

        var result = await Import(store, plan);

        var first = result.Entries.Single(e => e.Title == "First prompt");
        var second = result.Entries.Single(e => e.Title == "Second prompt");

        // The literal local id `second` must not survive into the stored
        // dependency — it has to have become the sibling's real backlog_item_id.
        Assert.Equal([second.Id.ToString()], first.DependsOn!);
    }

    /// <summary>A dependency naming a value nothing in the batch or the store
    /// claims as its <c>id:</c> is left exactly as written — the general
    /// `after:` rule outside Import, unchanged.</summary>
    [Fact]
    public async Task A_dependency_naming_no_local_id_is_left_as_a_real_id()
    {
        var store = new InMemoryBacklogRepository();

        var result = await Import(store, "# Only prompt\n`prompt` `after:some-existing-real-id`\n");

        Assert.Equal(["some-existing-real-id"], Assert.Single(result.Entries).DependsOn!);
    }

    [Fact]
    public async Task The_shared_tag_every_entry_carries_becomes_the_import_plan_id()
    {
        var store = new InMemoryBacklogRepository();

        const string plan =
            "# First prompt\n`prompt` `#myplan` `#other`\n\n"
            + "# Second prompt\n`prompt` `#myplan`\n";

        var result = await Import(store, plan);

        Assert.All(result.Entries, e => Assert.Equal("myplan", store.Entries[e.Id].ImportPlanId));
    }

    /// <summary>No tag is common to every entry, so there is nothing to derive a
    /// plan id from — both entries still import, per ADR 0004's accepted
    /// limitation, they simply cannot be matched by a later re-import.</summary>
    [Fact]
    public async Task No_shared_tag_still_imports_but_carries_no_plan_id()
    {
        var store = new InMemoryBacklogRepository();

        const string plan =
            "# First prompt\n`prompt` `#alpha`\n\n"
            + "# Second prompt\n`prompt` `#beta`\n";

        var result = await Import(store, plan);

        Assert.Equal(2, result.Created);
        Assert.All(result.Entries, e => Assert.Null(store.Entries[e.Id].ImportPlanId));
    }

    [Fact]
    public async Task Reimporting_the_same_plan_and_id_updates_the_existing_entry_in_place()
    {
        var store = new InMemoryBacklogRepository();

        var first = await Import(store, "# Draft title\n`prompt` `#myplan` `id:step-one`\n\nOriginal body.\n");
        var firstId = Assert.Single(first.Entries).Id;

        var second = await Import(store, "# Revised title\n`prompt` `#myplan` `id:step-one`\n\nRevised body.\n");

        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Updated);
        Assert.Equal(0, second.Skipped);

        // Updated in place, not duplicated.
        Assert.Single(store.Entries);
        var updated = Assert.Single(second.Entries);
        Assert.Equal(firstId, updated.Id);
        Assert.Equal("Revised title", updated.Title);
        Assert.Equal("Revised body.", updated.Body.Trim());
    }

    /// <summary>A later plan version does not reopen finished work — the same
    /// principle Occurrence Spawning applies to a completed recurring
    /// entry.</summary>
    [Fact]
    public async Task Reimporting_against_a_done_entry_leaves_it_untouched_and_counts_it_skipped()
    {
        var store = new InMemoryBacklogRepository();

        var first = await Import(store, "# Draft title\n`prompt` `#myplan` `id:step-one` `!ready`\n\nOriginal body.\n");
        var firstId = Assert.Single(first.Entries).Id;
        store.Entries[firstId].SetStatus(EntryStatus.Done);

        var second = await Import(store, "# Revised title\n`prompt` `#myplan` `id:step-one`\n\nRevised body.\n");

        Assert.Equal(0, second.Created);
        Assert.Equal(0, second.Updated);
        Assert.Equal(1, second.Skipped);
        Assert.Empty(second.Entries);

        // Untouched: still the original title and body.
        var untouched = store.Entries[firstId];
        Assert.Equal("Draft title", untouched.Title);
        Assert.Equal(EntryStatus.Done, untouched.Status);
    }

    /// <summary>The same check for an archived match — the other settled status
    /// a later plan version must not reopen.</summary>
    [Fact]
    public async Task Reimporting_against_an_archived_entry_leaves_it_untouched_and_counts_it_skipped()
    {
        var store = new InMemoryBacklogRepository();

        var first = await Import(store, "# Draft title\n`prompt` `#myplan` `id:step-one` `!ready`\n\nOriginal body.\n");
        var firstId = Assert.Single(first.Entries).Id;
        store.Entries[firstId].SetStatus(EntryStatus.Done);
        store.Entries[firstId].SetStatus(EntryStatus.Archived);

        var second = await Import(store, "# Revised title\n`prompt` `#myplan` `id:step-one`\n\nRevised body.\n");

        Assert.Equal(1, second.Skipped);
        Assert.Equal("Draft title", store.Entries[firstId].Title);
    }

    /// <summary>A dependency on a skipped-but-referenced entry still resolves —
    /// a settled prompt is a real entry, even one this run leaves
    /// untouched.</summary>
    [Fact]
    public async Task A_dependency_on_a_skipped_entry_still_resolves_to_its_real_id()
    {
        var store = new InMemoryBacklogRepository();

        var first = await Import(store, "# Step one\n`prompt` `#myplan` `id:step-one` `!ready`\n");
        var firstId = Assert.Single(first.Entries).Id;
        store.Entries[firstId].SetStatus(EntryStatus.Done);

        const string plan =
            "# Step one\n`prompt` `#myplan` `id:step-one`\n\n"
            + "# Step two\n`prompt` `#myplan` `id:step-two` `after:step-one`\n";

        var result = await Import(store, plan);

        Assert.Equal(1, result.Skipped);
        var stepTwo = Assert.Single(result.Entries, e => e.Title == "Step two");
        Assert.Equal([firstId.ToString()], stepTwo.DependsOn!);
    }

    /// <summary>An entry with no <c>id:</c> token has nothing to be matched
    /// against, so it is created new on every import, never matched — even
    /// against an entry that shares its plan tag and title.</summary>
    [Fact]
    public async Task An_entry_with_no_id_token_is_always_created_new()
    {
        var store = new InMemoryBacklogRepository();

        await Import(store, "# Same title every time\n`prompt` `#myplan`\n");
        await Import(store, "# Same title every time\n`prompt` `#myplan`\n");

        Assert.Equal(2, store.Entries.Count);
    }

    [Fact]
    public async Task An_empty_plan_is_a_validation_error()
    {
        var store = new InMemoryBacklogRepository();

        var result = await new ImportPlanCommandHandler(store, new FakeRepositoryDirectory())
            .Handle(new ImportPlanCommand("\n\n   \n"));

        Assert.True(result.IsFailure);
        Assert.Equal(ImportPlanCommandHandler.EmptyPlan, result.Error);
    }

    /// <summary>
    /// Two entries in one document both claiming the same <c>id:</c> is a plan
    /// nothing can act on: an <c>after:</c> naming that id has two answers, and on
    /// a re-import both segments resolve to the one stored entry and overwrite
    /// each other while the count claims two updates. Refused before anything is
    /// written, naming the id so the text can be fixed.
    /// </summary>
    [Fact]
    public async Task Two_entries_claiming_the_same_id_are_a_validation_error()
    {
        var store = new InMemoryBacklogRepository();

        var directory = new FakeRepositoryDirectory();

        const string plan =
            "# First prompt\n`prompt` `#myplan` `id:same` `repo:brand-new`\n\n"
            + "# Second prompt\n`prompt` `#myplan` `id:same` `repo:brand-new`\n";

        var result = await new ImportPlanCommandHandler(store, directory).Handle(new ImportPlanCommand(plan));

        Assert.True(result.IsFailure);
        Assert.Equal(ImportPlanCommandHandler.DuplicateItemId("same"), result.Error);
        Assert.Contains("same", result.Error.Message, StringComparison.Ordinal);

        // Refused before the writing pass, so the plan leaves nothing half-imported.
        Assert.Empty(store.Entries);

        // And nothing half-registered either. Registering a repository is a write
        // to the workspace like creating an entry is, so a plan Import refuses
        // must not leave one behind for somebody to go and delete — which it did
        // while repository resolution ran inside the parse pipeline, ahead of
        // this guard.
        Assert.Empty(directory.Registered);
    }

    /// <summary>The same refusal on a re-import, where the damage would be worse:
    /// both segments match the one stored entry, so the second would silently
    /// overwrite what the first just wrote.</summary>
    [Fact]
    public async Task Two_entries_claiming_the_same_id_are_refused_on_a_reimport_too()
    {
        var store = new InMemoryBacklogRepository();

        await Import(store, "# First prompt\n`prompt` `#myplan` `id:same`\n");
        var before = Assert.Single(store.Entries).Value.Title;

        const string plan =
            "# Renamed once\n`prompt` `#myplan` `id:same`\n\n"
            + "# Renamed twice\n`prompt` `#myplan` `id:same`\n";

        var result = await new ImportPlanCommandHandler(store, new FakeRepositoryDirectory())
            .Handle(new ImportPlanCommand(plan));

        Assert.True(result.IsFailure);
        Assert.Equal(ImportPlanCommandHandler.DuplicateItemId("same"), result.Error);
        Assert.Equal(before, Assert.Single(store.Entries).Value.Title);
    }

    /// <summary>The Import dialog's "Target repository" field fills in for an
    /// entry that names none of its own — the same mechanism `repo:` already
    /// uses, sourced from the command instead of a parsed token.
    /// <para>
    /// Stored as an id, like every other repository value: the empty registry
    /// here has never heard of "widgets", so Import registers it, and a bare name
    /// registers with owner and name standing in as the alias.
    /// </para></summary>
    [Fact]
    public async Task An_entry_with_no_repo_token_picks_up_the_default_repo()
    {
        var store = new InMemoryBacklogRepository();

        var result = await Import(store, "# Only prompt\n`prompt`\n", defaultRepo: "widgets");

        Assert.Equal(["widgets/widgets"], Assert.Single(result.Entries).RepoIds!);
    }

    /// <summary>An entry's own `repo:` token is the power-user override and
    /// always wins, even when a default is also supplied — a plan spanning more
    /// than one repository still works.</summary>
    [Fact]
    public async Task An_entry_with_its_own_repo_token_keeps_it_even_when_a_default_is_supplied()
    {
        var store = new InMemoryBacklogRepository();

        var result = await Import(store, "# Only prompt\n`prompt` `repo:its-own-repo`\n", defaultRepo: "widgets");

        Assert.Equal(["its-own-repo/its-own-repo"], Assert.Single(result.Entries).RepoIds!);
    }

    /// <summary>A name the registry already knows resolves to that repository's
    /// id and nothing is registered — per ADR 0004 auto-registration is what
    /// happens to an <em>unrecognized</em> name, so a plan naming repositories
    /// that already exist must leave the registry exactly as it found it.</summary>
    [Fact]
    public async Task A_repo_name_the_registry_already_knows_resolves_without_registering_anything()
    {
        var store = new InMemoryBacklogRepository();
        var directory = new FakeRepositoryDirectory("widgets");

        var result = await Import(store, "# Only prompt\n`prompt` `repo:widgets`\n", directory: directory);

        Assert.Equal(["someone/widgets"], Assert.Single(result.Entries).RepoIds!);
        Assert.Empty(directory.Registered);
    }

    /// <summary>The Import dialog's matching row is the reader saying "the plan
    /// calls it this, I mean that repository". That answer is the strongest
    /// signal there is — a person looked at it — so it is taken before the
    /// registry is consulted and nothing is registered behind it.</summary>
    [Fact]
    public async Task A_name_the_reader_matched_in_the_dialog_wins_over_the_registry()
    {
        var store = new InMemoryBacklogRepository();
        var directory = new FakeRepositoryDirectory("widgets");

        var result = await Import(
            store,
            "# Only prompt\n`prompt` `repo:Fancy Widgets`\n",
            directory: directory,
            repoMatches: new Dictionary<string, string> { ["Fancy Widgets"] = "widgets" });

        // The answer they gave is resolved through the registry like any other
        // name, so both branches end at an id rather than one ending at whatever
        // the dialog happened to put in the map.
        Assert.Equal(["someone/widgets"], Assert.Single(result.Entries).RepoIds!);
        Assert.Empty(directory.Registered);
        Assert.DoesNotContain("Fancy Widgets", directory.Resolved);
    }

    /// <summary>A name nothing recognises is registered on the spot, per ADR
    /// 0004 — once for the whole plan, however many entries name it. Registering
    /// per entry would be the same repository asked for twice, and a registry
    /// that answered by adding it twice would be a plan quietly corrupting the
    /// workspace it introduced itself to.</summary>
    [Fact]
    public async Task An_unknown_repo_named_by_two_entries_is_registered_once_for_the_whole_plan()
    {
        var store = new InMemoryBacklogRepository();
        var directory = new FakeRepositoryDirectory();

        const string plan =
            "# First prompt\n`prompt` `#myplan` `repo:newcomer`\n\n"
            + "# Second prompt\n`prompt` `#myplan` `repo:newcomer`\n";

        var result = await Import(store, plan, directory: directory);

        Assert.Equal(["newcomer"], directory.Registered);
        Assert.All(result.Entries, entry => Assert.Equal(["newcomer/newcomer"], entry.RepoIds!));
    }

    private static async Task<ImportPlanResultDto> Import(
        ITaskRepository store,
        string rawText,
        string? defaultRepo = null,
        FakeRepositoryDirectory? directory = null,
        IReadOnlyDictionary<string, string>? repoMatches = null)
    {
        var handler = new ImportPlanCommandHandler(store, directory ?? new FakeRepositoryDirectory());
        var result = await handler.Handle(new ImportPlanCommand(rawText, defaultRepo, repoMatches));

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    /// <summary>The same small hand-written fake <c>RecurringTaskTests</c> uses,
    /// kept local to this file for the same reason: what is under test is
    /// Import's own orchestration rather than the storage format.</summary>
    private sealed class InMemoryBacklogRepository : ITaskRepository
    {
        public Dictionary<Guid, TaskItem> Entries { get; } = [];

        public Task SaveAsync(TaskItem entry, CancellationToken cancellationToken = default)
        {
            Entries[entry.Id] = entry;
            return Task.CompletedTask;
        }

        public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.TryGetValue(id, out var entry) ? entry : null);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Entries.Remove(id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>([.. Entries.Values]);
    }
}
