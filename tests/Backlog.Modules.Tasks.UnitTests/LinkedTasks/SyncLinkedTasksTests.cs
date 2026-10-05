using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.SyncLinkedTasks;
using Backlog.Modules.Tasks.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.UnitTests.LinkedTasks;

/// <summary>
/// The sync that turns a connected target's items into linked tasks (local ADR
/// 0020, §2 to §6 and §9). What is worth holding here: which fields the source
/// owns and which it gave away at creation, that the status only ever moves along
/// the lifecycle's edges, that a person's delete and a person's Done both stand,
/// that a vanished item archives rather than deletes, and that a sync with nothing
/// to say writes nothing — the last because an idle machine that restamped every
/// task would win last-write-wins over real edits made elsewhere.
/// </summary>
public sealed class SyncLinkedTasksTests
{
    private const string Repo = "JSdotNet/Backlog";
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 5);

    private readonly StubTaskConnector _connector = new();
    private readonly InMemoryTaskRepository _tasks = new();
    private readonly InMemoryConnectedTargets _targets = new(new ConnectedTarget(StubTaskConnector.Id, Repo));
    private readonly ManualTimeProvider _time = new(Now);

    // --- Creating -------------------------------------------------------------

    [Fact]
    public async Task An_open_item_becomes_a_ready_linked_task()
    {
        _connector.Items.Add(Item("I_1", labels: ["+Offline Sync", "Good first issue"], effort: 3, dueOn: new DateOnly(2026, 11, 1)));

        var summary = await SyncAsync();

        Assert.Equal(1, summary.Created);
        var task = Assert.Single(_tasks.Entries.Values);
        Assert.Equal(LinkedTaskIds.For(StubTaskConnector.Id, "I_1"), task.Id);
        Assert.Equal(EntryType.Task, task.Type);
        Assert.Equal(EntryStatus.Ready, task.Status);
        Assert.Equal("Item I_1", task.Title);
        Assert.Equal("Body of I_1", task.ContentMd);
        Assert.Equal(["+offline-sync", "good-first-issue"], task.Tags);
        Assert.Equal(3, task.Effort);
        Assert.Equal(new DateOnly(2026, 11, 1), task.DueOn);

        var source = Assert.IsType<SourceRef>(task.SourceRef);
        Assert.Equal(StubTaskConnector.Id, source.ConnectorId);
        Assert.Equal(Repo, source.Target);
        Assert.Equal("I_1", source.ExternalId);
        Assert.Equal("https://example.test/I_1", source.Url);
        Assert.Equal("#I_1", source.DisplayKey);
        Assert.Equal("open", source.SourceState);
        Assert.Equal(Now.AddDays(-1), source.SourceUpdatedAt);
        Assert.Equal(NormalisedSourceState.Open, source.NormalisedState);
        Assert.Empty(source.Flags);
    }

    [Theory]
    [InlineData(NormalisedSourceState.Open, EntryStatus.Ready)]
    [InlineData(NormalisedSourceState.Active, EntryStatus.InProgress)]
    [InlineData(NormalisedSourceState.Done, EntryStatus.Done)]
    [InlineData(NormalisedSourceState.Dropped, EntryStatus.Archived)]
    public async Task Each_normalised_state_maps_to_one_status(NormalisedSourceState state, EntryStatus expected)
    {
        _connector.Items.Add(Item("I_1", state));

        await SyncAsync();

        Assert.Equal(expected, Assert.Single(_tasks.Entries.Values).Status);
    }

    // --- Updating ---------------------------------------------------------------

    [Fact]
    public async Task The_source_overwrites_the_title_the_state_and_the_assignee()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();

        _connector.Items[0] = Item("I_1", NormalisedSourceState.Active) with
        {
            Title = "Renamed at the source",
            Assignee = "Job",
            SourceStateName = "in progress",
            UpdatedAt = Now,
        };
        var summary = await SyncAsync();

        Assert.Equal(1, summary.Updated);
        var task = Assert.Single(_tasks.Entries.Values);
        Assert.Equal("Renamed at the source", task.Title);
        Assert.Equal(EntryStatus.InProgress, task.Status);
        Assert.Equal("Job", task.SourceRef!.Assignee);
        Assert.Equal("in progress", task.SourceRef.SourceState);
        Assert.Equal(Now, task.SourceRef.SourceUpdatedAt);
    }

    [Fact]
    public async Task The_body_labels_effort_and_due_date_are_copied_once()
    {
        _connector.Items.Add(Item("I_1", labels: ["bug"], effort: 2, dueOn: new DateOnly(2026, 11, 1)));
        await SyncAsync();

        var task = Assert.Single(_tasks.Entries.Values);
        task.UpdateContent("Body of I_1\n\nMy own notes.");

        _connector.Items[0] = _connector.Items[0] with
        {
            Body = "Rewritten at the source",
            Labels = ["feature"],
            Effort = 8,
            DueOn = new DateOnly(2027, 1, 1),
            UpdatedAt = Now,
        };
        await SyncAsync();

        Assert.Equal("Body of I_1\n\nMy own notes.", task.ContentMd);
        Assert.Equal(["bug"], task.Tags);
        Assert.Equal(2, task.Effort);
        Assert.Equal(new DateOnly(2026, 11, 1), task.DueOn);
    }

    [Fact]
    public async Task A_target_whose_title_does_not_follow_the_source_keeps_the_local_title()
    {
        _targets.Save(new ConnectedTarget(StubTaskConnector.Id, Repo) { TitleFollowsSource = false });
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();

        var task = Assert.Single(_tasks.Entries.Values);
        task.Rename("My own name for it");

        _connector.Items[0] = _connector.Items[0] with { Title = "Renamed at the source" };
        await SyncAsync();

        Assert.Equal("My own name for it", task.Title);
    }

    /// <summary>A title the person changed has stopped following the source (ADR 0020
    /// §9): the next sync keeps it even though the source's title changed, and goes
    /// on recording what the source calls the item.</summary>
    [Fact]
    public async Task A_title_renamed_locally_survives_a_changed_source_title()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        var task = Assert.Single(_tasks.Entries.Values);
        Assert.Equal("Item I_1", task.SourceRef!.SourceTitle);
        task.Rename("My own name for it");

        _connector.Items[0] = _connector.Items[0] with { Title = "Renamed at the source" };
        await SyncAsync();

        Assert.Equal("My own name for it", task.Title);
        Assert.Equal("Renamed at the source", task.SourceRef!.SourceTitle);
    }

    /// <summary>A reference from before the source title was kept cannot tell a local
    /// rename from an old source title, so the title follows the source, once, and
    /// the reference records it from then on.</summary>
    [Fact]
    public async Task An_older_reference_without_a_source_title_follows_the_source()
    {
        var id = LinkedTaskIds.For(StubTaskConnector.Id, "I_1");
        var task = new TaskItem(id, "Old title", string.Empty, EntryType.Task, EntryStatus.Ready, Priority.Medium, null, null, null, Now);
        task.SetSourceRef(new SourceRef(
            StubTaskConnector.Id, Repo, "I_1", "u", "#I_1", null, "open", Now.AddDays(-2), normalisedState: NormalisedSourceState.Open));
        _tasks.Entries[id] = task;
        _connector.Items.Add(Item("I_1"));

        await SyncAsync();

        Assert.Equal("Item I_1", task.Title);
        Assert.Equal("Item I_1", task.SourceRef!.SourceTitle);
    }

    /// <summary>The body is the person's from creation on, so it is stored as prose:
    /// a heading, a checklist line or an unmatched fence in the source's text must
    /// not become a sub-item, split off a second entry, or swallow the chapters the
    /// person adds below it.</summary>
    [Fact]
    public async Task A_body_with_entry_structure_in_it_is_stored_as_prose()
    {
        const string body = "Intro line.\n# Not a new entry\n## Not a step\n- [ ] Not a checklist step\n```\nunclosed fence\nMore #deploy prose.";
        _connector.Items.Add(Item("I_1") with { Body = body });

        await SyncAsync();

        var task = Assert.Single(_tasks.Entries.Values);
        Assert.Empty(task.SubItems);

        var raw = EntryTextParser.ToRawText(task.ToDto());
        Assert.Single(EntryTextParser.SplitSegments(raw));
        Assert.Equal(0, EntryTextParser.CountSubItems(raw));

        var parent = EntryTextParser.GetParentText(raw);
        foreach (var line in new[] { "Intro line.", "Not a new entry", "Not a step", "Not a checklist step", "unclosed fence", "More #deploy prose." })
        {
            Assert.Contains(line, parent, StringComparison.Ordinal);
        }

        // A chapter the person adds below the body is still a chapter.
        Assert.Equal(1, EntryTextParser.CountSubItems(EntryTextParser.AppendSubItem(raw, "A real step")));
    }

    [Fact]
    public async Task A_balanced_code_block_in_the_body_is_kept_as_it_was()
    {
        const string body = "Steps:\n```\n## inside code\n```\nAfter.";
        _connector.Items.Add(Item("I_1") with { Body = body });

        await SyncAsync();

        Assert.Equal(body, Assert.Single(_tasks.Entries.Values).ContentMd);
    }

    /// <summary>The target's spelling in the settings may differ in case from the one
    /// the task was linked under. That is the same target, and a sync with nothing
    /// else to say does not rewrite the reference for it.</summary>
    [Fact]
    public async Task A_target_spelled_in_another_case_is_not_a_change()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        _targets.Save(_targets.Get(StubTaskConnector.Id, Repo)! with { Target = Repo.ToLowerInvariant() });
        var writes = _tasks.Writes;

        var summary = await SyncAsync();

        Assert.Equal(1, summary.Unchanged);
        Assert.Equal(writes, _tasks.Writes);
        Assert.Equal(Repo, Assert.Single(_tasks.Entries.Values).SourceRef!.Target);
    }

    /// <summary>The sync records its progress over whatever the settings say when it
    /// finishes: a setting the person changed while the fetch ran survives.</summary>
    [Fact]
    public async Task A_setting_changed_during_the_fetch_survives_the_sync()
    {
        _connector.DuringFetch = () =>
            _targets.Save(_targets.Get(StubTaskConnector.Id, Repo)! with { TitleFollowsSource = false });

        await SyncAsync();

        var stored = _targets.Get(StubTaskConnector.Id, Repo)!;
        Assert.False(stored.TitleFollowsSource);
        Assert.Equal(Now, stored.LastSyncedAt);
        Assert.Equal(1, _targets.Updates);
    }

    // --- Walking the lifecycle ----------------------------------------------------

    [Theory]
    [InlineData(EntryStatus.Ready, EntryStatus.Done, new[] { EntryStatus.InProgress, EntryStatus.Done })]
    [InlineData(EntryStatus.Ready, EntryStatus.Archived, new[] { EntryStatus.InProgress, EntryStatus.Done, EntryStatus.Archived })]
    [InlineData(EntryStatus.Archived, EntryStatus.Ready, new[] { EntryStatus.Draft, EntryStatus.Ready })]
    [InlineData(EntryStatus.Draft, EntryStatus.InProgress, new[] { EntryStatus.Ready, EntryStatus.InProgress })]
    [InlineData(EntryStatus.Done, EntryStatus.Done, new EntryStatus[0])]
    public void The_walk_is_the_shortest_path_along_the_lifecycle(EntryStatus from, EntryStatus to, EntryStatus[] expected)
    {
        var path = SyncLinkedTasksCommandHandler.PathBetween(from, to);

        Assert.Equal(expected, path);
        var at = from;
        foreach (var step in path)
        {
            Assert.True(EntryStatusFlow.IsAllowed(at, step), $"{at} to {step} is not an edge.");
            at = step;
        }
    }

    /// <summary>A closed issue walks its Ready task through In progress — the graph
    /// has no shorter way — but passing through is not starting: no start date is
    /// stamped, and the task is ticked off the day it lands on Done.</summary>
    [Fact]
    public async Task A_closed_item_walks_its_ready_task_to_done_and_ticks_it_without_a_start()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();

        _connector.Items[0] = Item("I_1", NormalisedSourceState.Done);
        await SyncAsync();

        var task = Assert.Single(_tasks.Entries.Values);
        Assert.Equal(EntryStatus.Done, task.Status);
        Assert.Null(task.StartedOn);
        Assert.Equal(Today, task.CompletedOn);
    }

    [Fact]
    public async Task An_item_the_source_starts_stamps_the_start()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();

        _connector.Items[0] = Item("I_1", NormalisedSourceState.Active);
        await SyncAsync();

        var task = Assert.Single(_tasks.Entries.Values);
        Assert.Equal(EntryStatus.InProgress, task.Status);
        Assert.Equal(Today, task.StartedOn);
    }

    [Fact]
    public async Task An_item_the_source_reopens_unticks_its_task()
    {
        _connector.Items.Add(Item("I_1", NormalisedSourceState.Done));
        await SyncAsync();
        var task = Assert.Single(_tasks.Entries.Values);
        Assert.Equal(Today, task.CompletedOn);

        _connector.Items[0] = Item("I_1", NormalisedSourceState.Open);
        await SyncAsync();

        Assert.Equal(EntryStatus.Ready, task.Status);
        Assert.Null(task.CompletedOn);
    }

    [Fact]
    public async Task A_dropped_item_archives_its_ready_task()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();

        _connector.Items[0] = Item("I_1", NormalisedSourceState.Dropped);
        await SyncAsync();

        Assert.Equal(EntryStatus.Archived, Assert.Single(_tasks.Entries.Values).Status);
    }

    [Fact]
    public async Task A_reopened_item_brings_its_archived_task_back_to_ready()
    {
        _connector.Items.Add(Item("I_1", NormalisedSourceState.Dropped));
        await SyncAsync();

        _connector.Items[0] = Item("I_1", NormalisedSourceState.Open);
        await SyncAsync();

        Assert.Equal(EntryStatus.Ready, Assert.Single(_tasks.Entries.Values).Status);
    }

    // --- The status moves only when the source's state moved ------------------------

    [Fact]
    public async Task A_task_the_person_started_stays_started_while_the_source_still_says_open()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        var task = Assert.Single(_tasks.Entries.Values);
        task.ChangeStatus(EntryStatus.InProgress, Today);
        var stamp = task.UpdatedAt;
        var writes = _tasks.Writes;

        var summary = await SyncAsync();

        Assert.Equal(EntryStatus.InProgress, task.Status);
        Assert.Equal(1, summary.Unchanged);
        Assert.Equal(writes, _tasks.Writes);
        Assert.Equal(stamp, task.UpdatedAt);
    }

    [Fact]
    public async Task A_task_the_person_archived_stays_archived_while_the_source_still_says_open()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        var task = Assert.Single(_tasks.Entries.Values);
        task.ChangeStatus(EntryStatus.Draft, Today);
        task.SetStatus(EntryStatus.Archived, Today);
        var writes = _tasks.Writes;

        await SyncAsync();

        Assert.Equal(EntryStatus.Archived, task.Status);
        Assert.Equal(writes, _tasks.Writes);
    }

    [Fact]
    public async Task A_state_change_at_the_source_moves_a_task_the_person_had_moved()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        var task = Assert.Single(_tasks.Entries.Values);
        task.ChangeStatus(EntryStatus.InProgress, Today);
        await SyncAsync();

        _connector.Items[0] = Item("I_1", NormalisedSourceState.Done);
        await SyncAsync();

        Assert.Equal(EntryStatus.Done, task.Status);
        Assert.Equal(NormalisedSourceState.Done, task.SourceRef!.NormalisedState);
    }

    /// <summary>A reference written before the normalised state was kept says
    /// nothing about what the source said last, so the first sync treats the state
    /// as changed — once — and records it.</summary>
    [Fact]
    public async Task An_older_reference_without_a_normalised_state_is_walked_once_then_left_alone()
    {
        var id = LinkedTaskIds.For(StubTaskConnector.Id, "I_1");
        var task = new TaskItem(id, "Item I_1", "Body of I_1", EntryType.Task, EntryStatus.Draft, Priority.Medium, null, null, null, Now);
        task.SetSourceRef(new SourceRef(StubTaskConnector.Id, Repo, "I_1", "https://example.test/I_1", "#I_1", null, "open", Now.AddDays(-1)));
        _tasks.Entries[id] = task;
        _connector.Items.Add(Item("I_1"));

        await SyncAsync();

        Assert.Equal(EntryStatus.Ready, task.Status);
        Assert.Equal(NormalisedSourceState.Open, task.SourceRef!.NormalisedState);

        task.ChangeStatus(EntryStatus.InProgress, Today);
        var writes = _tasks.Writes;
        await SyncAsync();

        Assert.Equal(EntryStatus.InProgress, task.Status);
        Assert.Equal(writes, _tasks.Writes);
    }

    // --- What the person did stands ------------------------------------------------

    [Fact]
    public async Task A_deleted_linked_task_is_never_made_again()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        var task = Assert.Single(_tasks.Entries.Values);
        task.MarkDeleted();
        var writes = _tasks.Writes;

        var summary = await SyncAsync();

        Assert.Equal(1, summary.SkippedTombstoned);
        Assert.Equal(writes, _tasks.Writes);
        Assert.NotNull(_tasks.Entries[task.Id].DeletedAt);
    }

    [Fact]
    public async Task A_local_done_stays_done_and_is_flagged_while_the_source_holds_it_open()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        var task = Assert.Single(_tasks.Entries.Values);
        task.ChangeStatus(EntryStatus.InProgress, Today);
        task.ChangeStatus(EntryStatus.Done, Today);

        await SyncAsync();

        Assert.Equal(EntryStatus.Done, task.Status);
        Assert.True(task.SourceRef!.HasFlag(LinkedTaskFlags.DoneLocally));

        _connector.Items[0] = Item("I_1", NormalisedSourceState.Done);
        await SyncAsync();

        Assert.Equal(EntryStatus.Done, task.Status);
        Assert.False(task.SourceRef!.HasFlag(LinkedTaskFlags.DoneLocally));
    }

    [Fact]
    public async Task A_local_done_the_source_then_drops_is_archived_and_unflagged()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        var task = Assert.Single(_tasks.Entries.Values);
        task.ChangeStatus(EntryStatus.InProgress, Today);
        task.ChangeStatus(EntryStatus.Done, Today);
        await SyncAsync();

        _connector.Items[0] = Item("I_1", NormalisedSourceState.Dropped);
        await SyncAsync();

        Assert.Equal(EntryStatus.Archived, task.Status);
        Assert.Empty(task.SourceRef!.Flags);
    }

    // --- Vanishing ---------------------------------------------------------------

    [Fact]
    public async Task A_vanished_item_archives_and_flags_its_task_and_does_not_delete_it()
    {
        _connector.Items.Add(Item("I_1"));
        _connector.Items.Add(Item("I_2"));
        await SyncAsync();
        var gone = _tasks.Entries[LinkedTaskIds.For(StubTaskConnector.Id, "I_2")];
        gone.UpdateContent("Notes that must survive.");

        _connector.Items.RemoveAt(1);
        var summary = await SyncAsync();

        Assert.Equal(1, summary.Vanished);
        Assert.Equal(EntryStatus.Archived, gone.Status);
        Assert.Null(gone.DeletedAt);
        Assert.Equal("Notes that must survive.", gone.ContentMd);
        Assert.True(gone.SourceRef!.HasFlag(LinkedTaskFlags.Vanished));
        Assert.Equal(EntryStatus.Ready, _tasks.Entries[LinkedTaskIds.For(StubTaskConnector.Id, "I_1")].Status);
    }

    [Fact]
    public async Task A_vanished_task_is_archived_once()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        _connector.Items.Clear();
        await SyncAsync();
        var writes = _tasks.Writes;

        var summary = await SyncAsync();

        Assert.Equal(0, summary.Vanished);
        Assert.Equal(writes, _tasks.Writes);
    }

    [Fact]
    public async Task A_done_task_whose_item_drops_out_of_the_fetch_has_not_vanished()
    {
        _connector.Items.Add(Item("I_1", NormalisedSourceState.Done));
        await SyncAsync();

        _connector.Items.Clear();
        var summary = await SyncAsync();

        Assert.Equal(0, summary.Vanished);
        Assert.Equal(EntryStatus.Done, Assert.Single(_tasks.Entries.Values).Status);
    }

    [Fact]
    public async Task An_item_that_comes_back_clears_the_flag_and_reopens_its_task()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        _connector.Items.Clear();
        await SyncAsync();

        _connector.Items.Add(Item("I_1"));
        await SyncAsync();

        var task = Assert.Single(_tasks.Entries.Values);
        Assert.Equal(EntryStatus.Ready, task.Status);
        Assert.False(task.SourceRef!.HasFlag(LinkedTaskFlags.Vanished));
    }

    /// <summary>The fetch is every open item plus what closed since the last sync, so
    /// an item that closed earlier is absent without being gone. A task the person
    /// reopened after its item closed is not archived for that absence.</summary>
    [Fact]
    public async Task A_task_reopened_after_its_item_closed_is_not_vanished_when_the_item_drops_out()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        _connector.Items[0] = Item("I_1", NormalisedSourceState.Done);
        await SyncAsync();
        var task = Assert.Single(_tasks.Entries.Values);
        task.ChangeStatus(EntryStatus.InProgress, Today);

        _connector.Items.Clear();
        var summary = await SyncAsync();

        Assert.Equal(0, summary.Vanished);
        Assert.Equal(EntryStatus.InProgress, task.Status);
        Assert.False(task.SourceRef!.HasFlag(LinkedTaskFlags.Vanished));
    }

    /// <summary>A device syncing a target for the first time fetches only the open
    /// items. A task that reached it through the replica and whose item closed
    /// meanwhile is absent from that answer, and is not archived for it.</summary>
    [Fact]
    public async Task The_first_sync_on_a_device_vanishes_nothing()
    {
        var id = LinkedTaskIds.For(StubTaskConnector.Id, "I_1");
        var replicated = new TaskItem(id, "Item I_1", string.Empty, EntryType.Task, EntryStatus.Ready, Priority.Medium, null, null, null, Now);
        replicated.SetSourceRef(new SourceRef(
            StubTaskConnector.Id, Repo, "I_1", "u", "#I_1", null, "open", Now.AddDays(-2), normalisedState: NormalisedSourceState.Open));
        _tasks.Entries[id] = replicated;

        var summary = await SyncAsync();

        Assert.Equal(0, summary.Vanished);
        Assert.Equal(EntryStatus.Ready, replicated.Status);
    }

    [Fact]
    public async Task Another_targets_tasks_are_not_vanished_by_this_targets_fetch()
    {
        var other = new TaskItem(Guid.NewGuid(), "Elsewhere", string.Empty, EntryType.Task, EntryStatus.Ready, Priority.Medium, null, null, null, Now);
        other.SetSourceRef(new SourceRef(StubTaskConnector.Id, "JSdotNet/Other", "I_9", "u", "#9", null, "open", Now));
        _tasks.Entries[other.Id] = other;

        var summary = await SyncAsync();

        Assert.Equal(0, summary.Vanished);
        Assert.Equal(EntryStatus.Ready, other.Status);
    }

    // --- Labels ------------------------------------------------------------------

    [Fact]
    public async Task Only_the_first_plan_label_becomes_the_plan_tag_and_the_task_is_flagged()
    {
        _connector.Items.Add(Item("I_1", labels: ["+alpha", "bug", "+beta"]));

        await SyncAsync();

        var task = Assert.Single(_tasks.Entries.Values);
        Assert.Equal(["+alpha", "bug"], task.Tags);
        Assert.True(task.SourceRef!.HasFlag(LinkedTaskFlags.MultiplePlanTags));
    }

    [Theory]
    [InlineData("Blocked", "blocked")]
    [InlineData("good first issue", "good-first-issue")]
    [InlineData("  Needs   Review ", "needs-review")]
    [InlineData("area: sync", "area-sync")]
    [InlineData("v1.2", "v12")]
    [InlineData("2026", null)]
    [InlineData("!!!", null)]
    public void A_label_is_made_into_a_slug(string label, string? expected)
    {
        Assert.Equal(expected, SyncLinkedTasksCommandHandler.Slug(label));
    }

    // --- The first sync's age cut-off -------------------------------------------------

    [Fact]
    public async Task The_first_sync_skips_items_untouched_for_longer_than_the_set_age_and_fixes_the_cutoff()
    {
        _targets.Save(new ConnectedTarget(StubTaskConnector.Id, Repo) { SkipUntouchedOlderThan = TimeSpan.FromDays(30) });
        _connector.Items.Add(Item("I_old") with { UpdatedAt = Now.AddDays(-40) });
        _connector.Items.Add(Item("I_recent") with { UpdatedAt = Now.AddDays(-20) });

        var summary = await SyncAsync();

        Assert.Equal(1, summary.Created);
        Assert.Equal(1, summary.SkippedUntouched);
        Assert.Equal(["Item I_recent"], _tasks.Entries.Values.Select(task => task.Title));
        Assert.Equal(Now.AddDays(-30), _targets.Get(StubTaskConnector.Id, Repo)!.IgnoreUntouchedBefore);

        // Twenty days on, the cut-off has not slid. An item last changed 25 days
        // before the first sync is older than thirty days before today's run, so a
        // sliding window would skip it; it is newer than the fixed cut-off, so it
        // comes in.
        _time.Now = Now.AddDays(20);
        _connector.Items.Add(Item("I_late") with { UpdatedAt = Now.AddDays(-25) });
        await SyncAsync();

        Assert.Contains(_tasks.Entries.Values, task => task.Title == "Item I_late");
        Assert.DoesNotContain(_tasks.Entries.Values, task => task.Title == "Item I_old");
    }

    /// <summary>The age is read at the first sync only. A target that has already
    /// synced and has the setting switched on later has no first sync left to cut
    /// at, so nothing is skipped and no cut-off is invented.</summary>
    [Fact]
    public async Task An_age_set_after_the_first_sync_skips_nothing()
    {
        _targets.Save(new ConnectedTarget(StubTaskConnector.Id, Repo)
        {
            LastSyncedAt = Now.AddDays(-1),
            SkipUntouchedOlderThan = TimeSpan.FromDays(30),
        });
        _connector.Items.Add(Item("I_old") with { UpdatedAt = Now.AddDays(-40) });

        var summary = await SyncAsync();

        Assert.Equal(1, summary.Created);
        Assert.Null(_targets.Get(StubTaskConnector.Id, Repo)!.IgnoreUntouchedBefore);
    }

    // --- Failure and bookkeeping --------------------------------------------------

    [Fact]
    public async Task A_fetch_that_fails_writes_nothing_and_archives_nothing()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        var target = _targets.Get(StubTaskConnector.Id, Repo);
        var writes = _tasks.Writes;

        _connector.Failure = new HttpRequestException("offline");
        var result = await Handler().Handle(new SyncLinkedTasksCommand(StubTaskConnector.Id, Repo), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("linked_tasks.fetch_failed", result.Error.Code);
        Assert.Equal(writes, _tasks.Writes);
        Assert.Equal(EntryStatus.Ready, Assert.Single(_tasks.Entries.Values).Status);
        Assert.Equal(target, _targets.Get(StubTaskConnector.Id, Repo));
    }

    [Fact]
    public async Task A_sync_records_when_its_fetch_started_and_the_next_fetch_asks_from_there()
    {
        await SyncAsync();
        Assert.Equal(Now, _targets.Get(StubTaskConnector.Id, Repo)!.LastSyncedAt);

        _time.Now = Now.AddMinutes(15);
        await SyncAsync();

        Assert.Equal([(Repo, (DateTimeOffset?)null), (Repo, Now)], _connector.Fetches);
    }

    [Fact]
    public async Task A_connector_that_is_not_registered_is_refused_with_no_writes()
    {
        var result = await Handler().Handle(new SyncLinkedTasksCommand("unknown", Repo), TestContext.Current.CancellationToken);

        Assert.Equal(SyncLinkedTasksCommandHandler.ConnectorNotFound, result.Error);
        Assert.Equal(0, _tasks.Writes);
    }

    [Fact]
    public async Task A_target_that_is_not_connected_is_refused_with_no_fetch()
    {
        var result = await Handler().Handle(new SyncLinkedTasksCommand(StubTaskConnector.Id, "JSdotNet/Other"), TestContext.Current.CancellationToken);

        Assert.Equal(SyncLinkedTasksCommandHandler.TargetNotFound, result.Error);
        Assert.Empty(_connector.Fetches);
    }

    [Fact]
    public async Task A_disabled_target_is_refused_with_no_fetch()
    {
        _targets.Save(new ConnectedTarget(StubTaskConnector.Id, Repo, Enabled: false));

        var result = await Handler().Handle(new SyncLinkedTasksCommand(StubTaskConnector.Id, Repo), TestContext.Current.CancellationToken);

        Assert.Equal(SyncLinkedTasksCommandHandler.TargetDisabled, result.Error);
        Assert.Empty(_connector.Fetches);
        Assert.Equal(0, _tasks.Writes);
    }

    // --- Quiet syncs and other devices ---------------------------------------------

    [Fact]
    public async Task A_sync_with_nothing_new_saves_nothing_and_leaves_every_stamp_alone()
    {
        _connector.Items.Add(Item("I_1", labels: ["+alpha", "+beta"]));
        _connector.Items.Add(Item("I_2", NormalisedSourceState.Active));
        await SyncAsync();
        var stamps = _tasks.Entries.Values.ToDictionary(task => task.Id, task => task.UpdatedAt);
        var writes = _tasks.Writes;

        var summary = await SyncAsync();

        Assert.Equal(2, summary.Unchanged);
        Assert.Equal(0, summary.Updated);
        Assert.Equal(writes, _tasks.Writes);
        Assert.All(_tasks.Entries.Values, task => Assert.Equal(stamps[task.Id], task.UpdatedAt));
    }

    [Fact]
    public async Task Two_devices_syncing_the_same_items_make_the_same_tasks()
    {
        _connector.Items.Add(Item("I_1", labels: ["+alpha", "bug"], effort: 5));
        _connector.Items.Add(Item("I_2", NormalisedSourceState.Done));

        var here = new InMemoryTaskRepository();
        var there = new InMemoryTaskRepository();
        await SyncAsync(here, new InMemoryConnectedTargets(new ConnectedTarget(StubTaskConnector.Id, Repo)));
        await SyncAsync(there, new InMemoryConnectedTargets(new ConnectedTarget(StubTaskConnector.Id, Repo)));

        Assert.Equal(here.Entries.Keys.Order(), there.Entries.Keys.Order());
        foreach (var (id, mine) in here.Entries)
        {
            var theirs = there.Entries[id];
            Assert.Equal(mine.Title, theirs.Title);
            Assert.Equal(mine.ContentMd, theirs.ContentMd);
            Assert.Equal(mine.Status, theirs.Status);
            Assert.Equal(mine.Tags, theirs.Tags);
            Assert.Equal(mine.Effort, theirs.Effort);
            Assert.Equal(mine.CreatedAt, theirs.CreatedAt);
            Assert.Equal(mine.SourceRef, theirs.SourceRef);
        }
    }

    // --- Helpers -------------------------------------------------------------------

    private SyncLinkedTasksCommandHandler Handler(InMemoryTaskRepository? tasks = null, InMemoryConnectedTargets? targets = null) =>
        new([_connector], tasks ?? _tasks, targets ?? _targets, _time);

    private async Task<LinkedTaskSyncSummary> SyncAsync(InMemoryTaskRepository? tasks = null, InMemoryConnectedTargets? targets = null)
    {
        Result<LinkedTaskSyncSummary> result = await Handler(tasks, targets)
            .Handle(new SyncLinkedTasksCommand(StubTaskConnector.Id, Repo), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    private static SourceItem Item(
        string externalId,
        NormalisedSourceState state = NormalisedSourceState.Open,
        IReadOnlyList<string>? labels = null,
        int? effort = null,
        DateOnly? dueOn = null) =>
        new(
            externalId,
            $"#{externalId}",
            $"Item {externalId}",
            $"https://example.test/{externalId}",
            $"Body of {externalId}",
            state,
            state.ToString().ToLowerInvariant(),
            Assignee: null,
            UpdatedAt: Now.AddDays(-1),
            Labels: labels ?? [],
            Effort: effort,
            DueOn: dueOn);
}
