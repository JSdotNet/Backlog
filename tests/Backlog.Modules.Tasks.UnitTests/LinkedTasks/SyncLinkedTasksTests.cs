using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.Modules.Tasks.Abstractions.Services;
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

    private static readonly TimeZoneInfo FiveHoursEast =
        TimeZoneInfo.CreateCustomTimeZone("Test +05:00", TimeSpan.FromHours(5), "Test +05:00", "Test +05:00");

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

    /// <summary>Blocked is the source's word, so it follows the source on every
    /// sync like the assignee does: set with its reason, then cleared, reason and
    /// all, when the source unblocks the item.</summary>
    [Fact]
    public async Task Blocked_and_its_reason_follow_the_source()
    {
        _connector.Items.Add(Item("I_1") with { IsBlocked = true, BlockedReason = "Waiting on the design review" });
        await SyncAsync();

        var source = Assert.Single(_tasks.Entries.Values).SourceRef!;
        Assert.True(source.Blocked);
        Assert.Equal("Waiting on the design review", source.BlockedReason);

        _connector.Items[0] = _connector.Items[0] with { IsBlocked = false, UpdatedAt = Now };
        var summary = await SyncAsync();

        Assert.Equal(1, summary.Updated);
        source = Assert.Single(_tasks.Entries.Values).SourceRef!;
        Assert.False(source.Blocked);
        Assert.Null(source.BlockedReason);
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

    /// <summary>The tick is the day the work finished, and when the source says when
    /// that was, it is that day — on this machine's calendar, so an item finished
    /// late in the evening UTC is ticked off on the next day five hours east.</summary>
    [Fact]
    public async Task A_closed_item_is_ticked_off_on_the_local_day_the_source_finished_it()
    {
        _time.Zone = FiveHoursEast;
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();

        _connector.Items[0] = Item("I_1", NormalisedSourceState.Done) with
        {
            CompletedAt = new DateTimeOffset(2026, 10, 3, 21, 30, 0, TimeSpan.Zero),
        };
        await SyncAsync();

        var task = Assert.Single(_tasks.Entries.Values);
        Assert.Equal(EntryStatus.Done, task.Status);
        Assert.Equal(new DateOnly(2026, 10, 4), task.CompletedOn);
    }

    [Fact]
    public async Task An_item_created_finished_is_ticked_off_on_the_day_the_source_finished_it()
    {
        _connector.Items.Add(Item("I_1", NormalisedSourceState.Done) with
        {
            CompletedAt = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.FromHours(2)),
        });

        await SyncAsync();

        Assert.Equal(new DateOnly(2026, 9, 28), Assert.Single(_tasks.Entries.Values).CompletedOn);
    }

    /// <summary>A finish the source gives no day for is ticked off today, the day the
    /// sync learned of it.</summary>
    [Fact]
    public async Task A_closed_item_without_a_finish_day_is_ticked_off_today()
    {
        _connector.Items.Add(Item("I_1", NormalisedSourceState.Done));

        await SyncAsync();

        Assert.Equal(Today, Assert.Single(_tasks.Entries.Values).CompletedOn);
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

    // --- Write-back refusals ------------------------------------------------------

    /// <summary>A refusal is Backlog's, so the reference the sync builds from the item
    /// carries none; it is carried over while the task is done here and the item
    /// open there, which is the one state it explains.</summary>
    [Fact]
    public async Task A_write_back_refusal_is_kept_while_the_task_is_done_here_and_open_at_the_source()
    {
        var task = await RefusedTaskAsync();

        _connector.Items[0] = _connector.Items[0] with { Assignee = "Job", UpdatedAt = Now };
        await SyncAsync();

        Assert.Equal("Refused at the source", task.SourceRef!.WriteBackRefusal);
        Assert.True(task.SourceRef.HasFlag(LinkedTaskFlags.DoneLocally));
        Assert.Equal("Job", task.SourceRef.Assignee);
    }

    [Fact]
    public async Task A_write_back_refusal_is_dropped_once_the_source_finishes_the_item()
    {
        var task = await RefusedTaskAsync();

        _connector.Items[0] = Item("I_1", NormalisedSourceState.Done);
        await SyncAsync();

        Assert.Equal(EntryStatus.Done, task.Status);
        Assert.Null(task.SourceRef!.WriteBackRefusal);
    }

    [Fact]
    public async Task A_write_back_refusal_is_dropped_once_the_task_is_no_longer_done()
    {
        var task = await RefusedTaskAsync();

        task.SetStatus(EntryStatus.Ready, Today);
        await SyncAsync();

        Assert.Equal(EntryStatus.Ready, task.Status);
        Assert.Null(task.SourceRef!.WriteBackRefusal);
    }

    [Fact]
    public async Task A_vanished_task_loses_its_write_back_refusal_with_its_done()
    {
        var task = await RefusedTaskAsync();

        _connector.Items.Clear();
        await SyncAsync();

        Assert.Equal(EntryStatus.Archived, task.Status);
        Assert.Null(task.SourceRef!.WriteBackRefusal);
    }

    /// <summary>
    /// A write-back closed the item, and the sync it asked for ran before the
    /// source's search caught up, so the item still read as open. The next fetch
    /// reaches back over the close (the GitHub connector's closed-query overlap) and
    /// returns it closed: the task ends Done by the source's word, not archived as
    /// vanished, and seeing the closed item again after that changes nothing.
    /// </summary>
    [Fact]
    public async Task An_item_completed_by_write_back_that_reads_open_once_more_ends_done_not_vanished()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        var task = Assert.Single(_tasks.Entries.Values);
        task.ChangeStatus(EntryStatus.InProgress, Today);
        task.ChangeStatus(EntryStatus.Done, Today);

        // The sync the write-back asked for: the search has not seen the close yet.
        await SyncAsync();
        Assert.True(task.SourceRef!.HasFlag(LinkedTaskFlags.DoneLocally));

        _connector.Items[0] = Item("I_1", NormalisedSourceState.Done) with { UpdatedAt = Now };
        var summary = await SyncAsync();

        Assert.Equal(0, summary.Vanished);
        Assert.Equal(EntryStatus.Done, task.Status);
        Assert.Equal(NormalisedSourceState.Done, task.SourceRef!.NormalisedState);
        Assert.Empty(task.SourceRef.Flags);

        var writes = _tasks.Writes;
        var again = await SyncAsync();

        Assert.Equal(1, again.Unchanged);
        Assert.Equal(writes, _tasks.Writes);
    }

    /// <summary>A linked task the person finished while its item is open, synced
    /// once since, and refused by the source when the write-back asked.</summary>
    private async Task<TaskItem> RefusedTaskAsync()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        var task = Assert.Single(_tasks.Entries.Values);
        task.ChangeStatus(EntryStatus.InProgress, Today);
        task.ChangeStatus(EntryStatus.Done, Today);
        await SyncAsync();
        task.SetSourceRef(task.SourceRef! with { WriteBackRefusal = "Refused at the source" });
        return task;
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

    /// <summary>The plan tag also files the task under that plan, which is what the
    /// roadmap's shelf groups by — so the plan shows there with nothing done in
    /// Roadmap, and no Roadmap Item is made (ADR 0020, §6).</summary>
    [Fact]
    public async Task A_plan_label_files_the_task_under_its_plan_and_a_task_without_one_under_none()
    {
        _connector.Items.Add(Item("I_1", labels: ["bug", "+Offline Sync", "+beta"]));
        _connector.Items.Add(Item("I_2", labels: ["bug"]));

        await SyncAsync();

        Assert.Equal("+offline-sync", _tasks.Entries[LinkedTaskIds.For(StubTaskConnector.Id, "I_1")].ImportPlanId);
        Assert.Null(_tasks.Entries[LinkedTaskIds.For(StubTaskConnector.Id, "I_2")].ImportPlanId);
    }

    /// <summary>A task linked before the sync filed tasks under their plan is filed
    /// on its next sync, once — while it still carries the plan tag its first plan
    /// label gave it. One whose tag the person took off is left out.</summary>
    [Fact]
    public async Task A_task_linked_before_plans_were_filed_is_filed_once_while_it_keeps_the_plan_tag()
    {
        _connector.Items.Add(Item("I_1", labels: ["+alpha"]));
        _connector.Items.Add(Item("I_2", labels: ["+alpha"]));
        await SyncAsync();
        var kept = _tasks.Entries[LinkedTaskIds.For(StubTaskConnector.Id, "I_1")];
        var untagged = _tasks.Entries[LinkedTaskIds.For(StubTaskConnector.Id, "I_2")];
        kept.SetImportPlanId(null);
        untagged.SetImportPlanId(null);
        untagged.SetTags([]);

        var first = await SyncAsync();
        var writes = _tasks.Writes;
        var second = await SyncAsync();

        Assert.Equal("+alpha", kept.ImportPlanId);
        Assert.Null(untagged.ImportPlanId);
        Assert.Equal(1, first.Updated);
        Assert.Equal(2, second.Unchanged);
        Assert.Equal(writes, _tasks.Writes);
    }

    /// <summary>The sync reaches no Roadmap port: the roadmap changes only through a
    /// person's gesture (ADR 0013, ruling 3), so the handler is given nothing that
    /// could create a Roadmap Item.</summary>
    [Fact]
    public void The_sync_is_given_nothing_that_can_create_a_roadmap_item()
    {
        var parameters = typeof(SyncLinkedTasksCommandHandler).GetConstructors().Single().GetParameters();

        Assert.DoesNotContain(parameters, parameter =>
            parameter.ParameterType.FullName!.Contains("Roadmap", StringComparison.Ordinal));
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
        Assert.Equal($"Stub could not be reached for {Repo}.", result.Error.Message);
        Assert.Equal(writes, _tasks.Writes);
        Assert.Equal(EntryStatus.Ready, Assert.Single(_tasks.Entries.Values).Status);
        Assert.Equal(target!.LastSyncedAt, _targets.Get(StubTaskConnector.Id, Repo)!.LastSyncedAt);
    }

    /// <summary>A failure the connector recognised reaches the person in the
    /// connector's own words, under a code of its kind, rather than as the source
    /// not being reached — a repository nobody can see is not a network that is
    /// down.</summary>
    [Theory]
    [InlineData(TaskConnectorFetchFailure.NotFound, "linked_tasks.fetch_not_found")]
    [InlineData(TaskConnectorFetchFailure.NoAccess, "linked_tasks.fetch_no_access")]
    [InlineData(TaskConnectorFetchFailure.NotConfigured, "linked_tasks.fetch_not_configured")]
    [InlineData(TaskConnectorFetchFailure.SignInRequired, "linked_tasks.fetch_sign_in_required")]
    public async Task A_fetch_failure_the_connector_recognised_says_what_it_was_in_the_connectors_words(
        TaskConnectorFetchFailure kind, string code)
    {
        _connector.Failure = new TaskConnectorFetchException(kind, "The connector's own sentence.");

        var result = await Handler().Handle(new SyncLinkedTasksCommand(StubTaskConnector.Id, Repo), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal("The connector's own sentence.", result.Error.Message);
    }

    [Fact]
    public async Task A_source_that_did_not_answer_is_reported_as_not_reached()
    {
        _connector.Failure = new TaskConnectorFetchException(TaskConnectorFetchFailure.Unreachable, "timed out");

        var result = await Handler().Handle(new SyncLinkedTasksCommand(StubTaskConnector.Id, Repo), TestContext.Current.CancellationToken);

        Assert.Equal("linked_tasks.fetch_failed", result.Error.Code);
        Assert.Equal($"Stub could not be reached for {Repo}.", result.Error.Message);
    }

    [Fact]
    public async Task A_recognised_fetch_failure_writes_nothing_and_archives_nothing()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        var writes = _tasks.Writes;
        _connector.Items.Clear();

        _connector.Failure = new TaskConnectorFetchException(TaskConnectorFetchFailure.NotFound, "Gone.");
        await Handler().Handle(new SyncLinkedTasksCommand(StubTaskConnector.Id, Repo), TestContext.Current.CancellationToken);

        Assert.Equal(writes, _tasks.Writes);
        Assert.Equal(EntryStatus.Ready, Assert.Single(_tasks.Entries.Values).Status);
    }

    /// <summary>The failure is kept on the target, so its card can say why it is not
    /// syncing — without moving where the next fetch asks from, or the first-sync
    /// cut-off, which are the last success's.</summary>
    [Fact]
    public async Task A_failed_sync_is_recorded_on_the_target_without_moving_its_progress()
    {
        _targets.Save(new ConnectedTarget(StubTaskConnector.Id, Repo) { SkipUntouchedOlderThan = TimeSpan.FromDays(30) });
        await SyncAsync();
        var before = _targets.Get(StubTaskConnector.Id, Repo)!;

        _time.Now = Now.AddMinutes(20);
        _connector.Failure = new TaskConnectorFetchException(TaskConnectorFetchFailure.NoAccess, "Nobody here may read it.");
        await Handler().Handle(new SyncLinkedTasksCommand(StubTaskConnector.Id, Repo), TestContext.Current.CancellationToken);

        var after = _targets.Get(StubTaskConnector.Id, Repo)!;
        Assert.Equal("Nobody here may read it.", after.LastSyncError);
        Assert.Equal(Now.AddMinutes(20), after.LastSyncFailedAt);
        Assert.Equal(before.LastSyncedAt, after.LastSyncedAt);
        Assert.Equal(before.IgnoreUntouchedBefore, after.IgnoreUntouchedBefore);
    }

    [Fact]
    public async Task A_sync_that_succeeds_after_a_failure_clears_it()
    {
        _connector.Failure = new HttpRequestException("offline");
        await Handler().Handle(new SyncLinkedTasksCommand(StubTaskConnector.Id, Repo), TestContext.Current.CancellationToken);
        Assert.NotNull(_targets.Get(StubTaskConnector.Id, Repo)!.LastSyncError);

        _connector.Failure = null;
        await SyncAsync();

        var target = _targets.Get(StubTaskConnector.Id, Repo)!;
        Assert.Null(target.LastSyncError);
        Assert.Null(target.LastSyncFailedAt);
        Assert.Equal(Now, target.LastSyncedAt);
    }

    // --- The repository a linked task is filed under ---------------------------------

    /// <summary>A source whose targets are repositories files each new task under its
    /// repository, spelled the way the registry spells it, so the row does not read
    /// "No repo".</summary>
    [Fact]
    public async Task A_task_from_a_repository_is_filed_under_that_repository_as_the_registry_spells_it()
    {
        _connector.Capabilities = new(TargetIsRepository: true);
        var targets = new InMemoryConnectedTargets(new ConnectedTarget(StubTaskConnector.Id, "jsdotnet/backlog"));
        var registry = new FakeRepositoryDirectory([new TasksRepositoryRef("backlog", "JSdotNet", "Backlog")]);
        _connector.Items.Add(Item("I_1"));

        await Handler(targets: targets, repositories: registry)
            .Handle(new SyncLinkedTasksCommand(StubTaskConnector.Id, "jsdotnet/backlog"), TestContext.Current.CancellationToken);

        Assert.Equal(["JSdotNet/Backlog"], Assert.Single(_tasks.Entries.Values).RepoIds);
    }

    [Fact]
    public async Task A_repository_the_registry_does_not_know_is_filed_as_the_target_names_it()
    {
        _connector.Capabilities = new(TargetIsRepository: true);
        _connector.Items.Add(Item("I_1"));

        await SyncAsync(repositories: new FakeRepositoryDirectory());

        Assert.Equal([Repo], Assert.Single(_tasks.Entries.Values).RepoIds);
    }

    [Fact]
    public async Task A_linked_task_with_no_repository_is_filed_under_its_targets_once()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        Assert.Empty(Assert.Single(_tasks.Entries.Values).RepoIds);
        var writes = _tasks.Writes;

        _connector.Capabilities = new(TargetIsRepository: true);
        var summary = await SyncAsync();

        Assert.Equal(1, summary.Updated);
        Assert.Equal(writes + 1, _tasks.Writes);
        Assert.Equal([Repo], Assert.Single(_tasks.Entries.Values).RepoIds);
    }

    /// <summary>A repository list the task already has — the person's, or an
    /// earlier sync's — is never overwritten, and a sync with nothing else to say
    /// stays quiet.</summary>
    [Fact]
    public async Task A_linked_task_already_filed_under_a_repository_keeps_it_and_stays_quiet()
    {
        _connector.Capabilities = new(TargetIsRepository: true);
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        var task = Assert.Single(_tasks.Entries.Values);
        task.SetRepoIds(["someone/elsewhere"]);
        var writes = _tasks.Writes;

        var summary = await SyncAsync();

        Assert.Equal(1, summary.Unchanged);
        Assert.Equal(writes, _tasks.Writes);
        Assert.Equal(["someone/elsewhere"], task.RepoIds);
    }

    [Fact]
    public async Task A_task_from_a_source_whose_targets_are_not_repositories_is_filed_under_none()
    {
        _connector.Items.Add(Item("I_1"));

        await SyncAsync(repositories: new FakeRepositoryDirectory([new TasksRepositoryRef("backlog", "JSdotNet", "Backlog")]));

        Assert.Empty(Assert.Single(_tasks.Entries.Values).RepoIds);
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

    // --- Blocked ------------------------------------------------------------------

    [Fact]
    public async Task An_item_blocked_at_the_source_is_created_blocked_today_with_its_reason()
    {
        _connector.Items.Add(Item("I_1") with { IsBlocked = true, BlockedReason = "Waits on #3" });

        await SyncAsync();

        var task = Assert.Single(_tasks.Entries.Values);
        Assert.Equal(Today, task.BlockedSince);
        Assert.True(task.SourceRef!.Blocked);
        Assert.Equal("Waits on #3", task.SourceRef.BlockedReason);
    }

    [Fact]
    public async Task An_item_the_source_blocks_marks_its_task_blocked_on_the_day_of_the_move()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        var task = Assert.Single(_tasks.Entries.Values);
        Assert.Null(task.BlockedSince);
        Assert.False(task.SourceRef!.Blocked);

        _time.Now = Now.AddDays(1);
        _connector.Items[0] = _connector.Items[0] with { IsBlocked = true, BlockedReason = "Impeded" };
        var summary = await SyncAsync();

        Assert.Equal(1, summary.Updated);
        Assert.Equal(Today.AddDays(1), task.BlockedSince);
        Assert.Equal("Impeded", task.SourceRef!.BlockedReason);
    }

    /// <summary>A task the person already marked blocked keeps the day they marked
    /// it: the source agreeing says nothing about since when.</summary>
    [Fact]
    public async Task A_task_the_person_marked_blocked_keeps_its_day_when_the_source_blocks_it_too()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        var task = Assert.Single(_tasks.Entries.Values);
        task.SetBlockedSince(Today.AddDays(-3));

        _connector.Items[0] = _connector.Items[0] with { IsBlocked = true };
        await SyncAsync();

        Assert.Equal(Today.AddDays(-3), task.BlockedSince);
        Assert.True(task.SourceRef!.Blocked);
    }

    [Fact]
    public async Task An_item_the_source_unblocks_unblocks_its_task_and_drops_the_reason()
    {
        _connector.Items.Add(Item("I_1") with { IsBlocked = true, BlockedReason = "Waits on #3" });
        await SyncAsync();
        var task = Assert.Single(_tasks.Entries.Values);

        _connector.Items[0] = _connector.Items[0] with { IsBlocked = false, BlockedReason = null };
        await SyncAsync();

        Assert.Null(task.BlockedSince);
        Assert.False(task.SourceRef!.Blocked);
        Assert.Null(task.SourceRef.BlockedReason);
    }

    /// <summary>The source owns blocked-ness by its moves, as it owns the state: what
    /// the person set between two of them stands, either way round.</summary>
    [Fact]
    public async Task A_blocked_mark_the_person_changed_stands_while_the_source_does_not_move()
    {
        _connector.Items.Add(Item("I_1"));
        _connector.Items.Add(Item("I_2") with { IsBlocked = true });
        await SyncAsync();
        var markedHere = _tasks.Entries[LinkedTaskIds.For(StubTaskConnector.Id, "I_1")];
        var clearedHere = _tasks.Entries[LinkedTaskIds.For(StubTaskConnector.Id, "I_2")];
        markedHere.SetBlockedSince(Today);
        clearedHere.SetBlockedSince(null);
        var writes = _tasks.Writes;

        var summary = await SyncAsync();

        Assert.Equal(2, summary.Unchanged);
        Assert.Equal(writes, _tasks.Writes);
        Assert.Equal(Today, markedHere.BlockedSince);
        Assert.Null(clearedHere.BlockedSince);
    }

    /// <summary>A reason the source gives without blocking the item explains nothing,
    /// so the reference records none.</summary>
    [Fact]
    public async Task A_reason_on_an_item_that_is_not_blocked_is_not_recorded()
    {
        _connector.Items.Add(Item("I_1") with { BlockedReason = "Stale reason" });

        await SyncAsync();

        Assert.Null(Assert.Single(_tasks.Entries.Values).SourceRef!.BlockedReason);
    }

    // --- Devbook references ---------------------------------------------------------

    [Fact]
    public async Task An_items_references_become_its_tasks_devbook_references_and_one_naming_no_page_is_skipped()
    {
        _connector.Items.Add(Item("I_1") with
        {
            References = ["domain/tasks/features.md#linked-tasks", "not a page", ".devbook/arc42/adr/0020-linked.md"],
        });

        await SyncAsync();

        Assert.Equal(
            [".devbook/domain/tasks/features.md#linked-tasks", ".devbook/arc42/adr/0020-linked.md"],
            Assert.Single(_tasks.Entries.Values).DevbookReferences);
    }

    /// <summary>The references are a union: one the source adds is added, one the
    /// person added stays, and one the source removed stays too — a reference says
    /// what the task was about, and losing it silently loses that.</summary>
    [Fact]
    public async Task The_sources_references_are_added_to_the_tasks_and_never_removed()
    {
        _connector.Items.Add(Item("I_1") with { References = [".devbook/domain/tasks/features.md"] });
        await SyncAsync();
        var task = Assert.Single(_tasks.Entries.Values);
        task.SetDevbookReferences([.. task.DevbookReferences, ".devbook/design/README.md"]);

        _connector.Items[0] = _connector.Items[0] with { References = [".devbook/arc42/adr/0020-linked.md#decision"] };
        var summary = await SyncAsync();

        Assert.Equal(1, summary.Updated);
        Assert.Equal(
            [".devbook/domain/tasks/features.md", ".devbook/design/README.md", ".devbook/arc42/adr/0020-linked.md#decision"],
            task.DevbookReferences);
    }

    /// <summary>A reference the task already holds — in any spelling that normalises
    /// to it — is no change, so the quiet sync stays quiet.</summary>
    [Fact]
    public async Task A_sync_whose_references_the_task_already_holds_saves_nothing()
    {
        _connector.Items.Add(Item("I_1") with
        {
            IsBlocked = true,
            BlockedReason = "Waits on #3",
            References = ["domain/tasks/features.md#linked-tasks", "./.devbook/design/README.md"],
        });
        await SyncAsync();
        var task = Assert.Single(_tasks.Entries.Values);
        var stamp = task.UpdatedAt;
        var writes = _tasks.Writes;

        _connector.Items[0] = _connector.Items[0] with
        {
            References = [".devbook/domain/tasks/features.md#linked-tasks", "[.devbook/design/README.md]", "not a page"],
        };
        var summary = await SyncAsync();

        Assert.Equal(1, summary.Unchanged);
        Assert.Equal(writes, _tasks.Writes);
        Assert.Equal(stamp, task.UpdatedAt);
    }

    // --- Waits-on --------------------------------------------------------------------

    /// <summary>An item the source says waits on another becomes a task that comes
    /// after that one's task — when both are linked tasks. Which came first in the
    /// fetch does not matter; one the waited-on item has no task for adds nothing.</summary>
    [Fact]
    public async Task Waits_on_becomes_a_dependency_when_both_ends_are_linked()
    {
        _connector.Items.Add(Item("I_2") with { WaitsOn = ["I_1", "I_unknown"] });
        _connector.Items.Add(Item("I_1"));

        await SyncAsync();

        var waiting = _tasks.Entries[LinkedTaskIds.For(StubTaskConnector.Id, "I_2")];
        Assert.Equal([LinkedTaskIds.For(StubTaskConnector.Id, "I_1").ToString()], waiting.DependsOn);
        Assert.Empty(_tasks.Entries[LinkedTaskIds.For(StubTaskConnector.Id, "I_1")].DependsOn);
        Assert.Equal(["I_1", "I_unknown"], waiting.SourceRef!.WaitsOn);
    }

    [Fact]
    public async Task A_waited_on_item_linked_later_becomes_a_dependency_then()
    {
        _connector.Items.Add(Item("I_2") with { WaitsOn = ["I_1"] });
        await SyncAsync();
        var waiting = Assert.Single(_tasks.Entries.Values);
        Assert.Empty(waiting.DependsOn);

        _connector.Items.Add(Item("I_1"));
        await SyncAsync();

        Assert.Equal([LinkedTaskIds.For(StubTaskConnector.Id, "I_1").ToString()], waiting.DependsOn);
    }

    [Fact]
    public async Task A_waited_on_item_whose_task_the_person_deleted_is_not_a_dependency()
    {
        _connector.Items.Add(Item("I_1"));
        await SyncAsync();
        _tasks.Entries[LinkedTaskIds.For(StubTaskConnector.Id, "I_1")].MarkDeleted();

        _connector.Items.Add(Item("I_2") with { WaitsOn = ["I_1"] });
        await SyncAsync();

        Assert.Empty(_tasks.Entries[LinkedTaskIds.For(StubTaskConnector.Id, "I_2")].DependsOn);
    }

    /// <summary>The source owns the dependencies it gave, not the person's: a
    /// waits-on the source drops is taken off the task, and one the person added in
    /// Backlog stays.</summary>
    [Fact]
    public async Task A_waits_on_the_source_drops_is_removed_and_the_persons_own_dependency_stays()
    {
        _connector.Items.Add(Item("I_1"));
        _connector.Items.Add(Item("I_2") with { WaitsOn = ["I_1"] });
        await SyncAsync();
        var waiting = _tasks.Entries[LinkedTaskIds.For(StubTaskConnector.Id, "I_2")];
        var own = Guid.NewGuid().ToString();
        waiting.SetDependsOn([.. waiting.DependsOn, own]);

        _connector.Items[1] = _connector.Items[1] with { WaitsOn = [] };
        var summary = await SyncAsync();

        Assert.Equal([own], waiting.DependsOn);
        Assert.Empty(waiting.SourceRef!.WaitsOn);
        Assert.Equal(1, summary.Updated);
    }

    [Fact]
    public async Task A_sync_whose_waits_on_the_task_already_follows_saves_nothing()
    {
        _connector.Items.Add(Item("I_1"));
        _connector.Items.Add(Item("I_2") with { WaitsOn = ["I_1"] });
        await SyncAsync();
        var waiting = _tasks.Entries[LinkedTaskIds.For(StubTaskConnector.Id, "I_2")];
        var stamp = waiting.UpdatedAt;
        var writes = _tasks.Writes;

        var summary = await SyncAsync();

        Assert.Equal(2, summary.Unchanged);
        Assert.Equal(writes, _tasks.Writes);
        Assert.Equal(stamp, waiting.UpdatedAt);
    }

    // --- Helpers -------------------------------------------------------------------

    private SyncLinkedTasksCommandHandler Handler(
        InMemoryTaskRepository? tasks = null,
        InMemoryConnectedTargets? targets = null,
        FakeRepositoryDirectory? repositories = null) =>
        new([_connector], tasks ?? _tasks, targets ?? _targets, _time, repositories);

    private async Task<LinkedTaskSyncSummary> SyncAsync(
        InMemoryTaskRepository? tasks = null,
        InMemoryConnectedTargets? targets = null,
        FakeRepositoryDirectory? repositories = null)
    {
        Result<LinkedTaskSyncSummary> result = await Handler(tasks, targets, repositories)
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
