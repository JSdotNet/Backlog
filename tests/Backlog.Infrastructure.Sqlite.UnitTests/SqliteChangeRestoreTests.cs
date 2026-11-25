using Backlog.Infrastructure.Sqlite.Changes;
using Backlog.Infrastructure.Sqlite.Progress;
using Backlog.Modules.Progress.DomainModels;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.SharedKernel.Changes;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// Restoring a change set (local ADR 0061 §4): its revisions walked newest first, each
/// document written back as a later save, a document changed since kept unless the
/// person restores it anyway, and the restore recorded as a change set of its own that
/// the restored one points at.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteChangeRestoreTests : IDisposable
{
    private static readonly IReadOnlySet<DocumentKey> NoOverrides = new HashSet<DocumentKey>();

    private readonly TemporaryDirectory _temp = new("backlog-change-restore");
    private readonly SqliteTaskRepository _tasks;
    private readonly SqliteChangeRestore _restore;
    private readonly SqliteChangeJournal _journal;

    public SqliteChangeRestoreTests()
    {
        _tasks = new SqliteTaskRepository(_temp.Path);
        _restore = new SqliteChangeRestore(_temp.Path, _tasks);
        _journal = new SqliteChangeJournal(_tasks.DatabasePath);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_clean_restore_brings_back_what_the_change_set_replaced_as_a_later_save()
    {
        var task = await AddAsync("Plan the trip", "Book the train.");
        var agent = await AgentAsync("agent run", async () =>
        {
            var held = await ReloadAsync(task);
            held.Rename("Plan the trip (agent)");
            held.SetStatus(EntryStatus.Done, new DateOnly(2026, 11, 25));
            await _tasks.SaveAsync(held, Token);
        });
        var agentStamp = (await ReloadAsync(task)).UpdatedAt;

        var plan = Assert.IsType<RestorePlan>(await _restore.PlanAsync(agent.Id, Token));
        var step = Assert.Single(plan.Steps);
        Assert.Equal(new DocumentKey("task", task.Id.ToString()), step.Key);
        Assert.Equal(RestoreAction.Revert, step.Action);
        Assert.False(step.Conflict);
        Assert.Equal("Restore of agent run", plan.RestoreLabel);

        var outcome = await _restore.RestoreAsync(agent.Id, NoOverrides, Token);

        Assert.Equal(1, outcome.Restored);
        Assert.Equal(0, outcome.Kept);
        var restored = await ReloadAsync(task);
        Assert.Equal("Plan the trip", restored.Title);
        Assert.NotEqual(EntryStatus.Done, restored.Status);
        Assert.True(restored.UpdatedAt > agentStamp, "A restore is a later save, never a rewind.");

        var entries = await _journal.ListAsync(cancellationToken: Token);
        var restore = Assert.Single(entries, entry => entry.ChangeSet.Id == outcome.RestoreChangeSetId).ChangeSet;
        Assert.Equal(ChangeActor.Person, restore.Actor);
        Assert.Equal("Restore of agent run", restore.Label);
        Assert.Equal(outcome.RestoreChangeSetId, Assert.Single(entries, entry => entry.ChangeSet.Id == agent.Id).ChangeSet.UndoneBy);
        Assert.Equal(outcome.RestoreChangeSetId, (await _restore.PlanAsync(agent.Id, Token))!.UndoneBy);
    }

    [Fact]
    public async Task A_document_changed_since_is_a_conflict_kept_as_it_is()
    {
        var untouched = await AddAsync("Untouched since", string.Empty);
        var edited = await AddAsync("Edited since", string.Empty);
        var agent = await AgentAsync("agent run", async () =>
        {
            foreach (var task in new[] { untouched, edited })
            {
                var held = await ReloadAsync(task);
                held.Rename(held.Title + " (agent)");
                await _tasks.SaveAsync(held, Token);
            }
        });

        await PersonEditAsync(edited, "Edited since (mine)");

        var plan = Assert.IsType<RestorePlan>(await _restore.PlanAsync(agent.Id, Token));
        Assert.Equal(1, plan.ConflictCount);
        Assert.True(Assert.Single(plan.Steps, step => step.Key.Id == edited.Id.ToString()).Conflict);
        Assert.False(Assert.Single(plan.Steps, step => step.Key.Id == untouched.Id.ToString()).Conflict);

        var outcome = await _restore.RestoreAsync(agent.Id, NoOverrides, Token);

        Assert.Equal(1, outcome.Restored);
        Assert.Equal(1, outcome.Kept);
        Assert.Equal("Untouched since", (await ReloadAsync(untouched)).Title);
        Assert.Equal("Edited since (mine)", (await ReloadAsync(edited)).Title);
    }

    [Fact]
    public async Task A_conflict_restored_anyway_takes_the_prior_content_over_the_later_edit()
    {
        var edited = await AddAsync("Edited since", string.Empty);
        var agent = await AgentAsync("agent run", async () =>
        {
            var held = await ReloadAsync(edited);
            held.Rename("Edited since (agent)");
            await _tasks.SaveAsync(held, Token);
        });
        await PersonEditAsync(edited, "Edited since (mine)");
        var mineStamp = (await ReloadAsync(edited)).UpdatedAt;

        var outcome = await _restore.RestoreAsync(
            agent.Id,
            new HashSet<DocumentKey> { new("task", edited.Id.ToString()) },
            Token);

        Assert.Equal(1, outcome.Restored);
        var restored = await ReloadAsync(edited);
        Assert.Equal("Edited since", restored.Title);
        Assert.True(restored.UpdatedAt > mineStamp);
    }

    [Fact]
    public async Task A_deleted_task_comes_back_live_and_stamped_later_than_its_tombstone()
    {
        var task = await AddAsync("Deleted by the agent", "Keep me.");
        var agent = await AgentAsync("delete", async () =>
        {
            var held = await ReloadAsync(task);
            held.MarkDeleted();
            await _tasks.SaveAsync(held, Token);
        });
        Assert.Null(await _tasks.GetAsync(task.Id, Token));
        var tombstone = await StoredAsync(task.Id);
        Assert.NotNull(tombstone.DeletedAt);

        var outcome = await _restore.RestoreAsync(agent.Id, NoOverrides, Token);

        Assert.Equal(1, outcome.Restored);
        var restored = await ReloadAsync(task);
        Assert.Null(restored.DeletedAt);
        Assert.Equal("Keep me.", restored.ContentMd);
        Assert.True(restored.UpdatedAt > tombstone.UpdatedAt, "The restore must beat the tombstone on every device.");
    }

    [Fact]
    public async Task A_task_the_change_set_created_is_tombstoned()
    {
        TaskItem created = null!;
        var agent = await AgentAsync("create_item", async () =>
        {
            created = new TaskItem("Made by the agent", string.Empty, EntryType.Task);
            await _tasks.SaveAsync(created, Token);
        });

        Assert.Equal(RestoreAction.Tombstone, Assert.Single((await _restore.PlanAsync(agent.Id, Token))!.Steps).Action);

        await _restore.RestoreAsync(agent.Id, NoOverrides, Token);

        Assert.Null(await _tasks.GetAsync(created.Id, Token));
        Assert.NotNull((await StoredAsync(created.Id)).DeletedAt);
    }

    [Fact]
    public async Task A_deleted_goal_comes_back_and_a_created_one_is_tombstoned()
    {
        var goals = new SqliteGoalRepository(_temp.Path);
        var kept = Goal.Restore(Guid.NewGuid(), "Ship the journal", "Agents can be undone", new DateOnly(2026, 12, 1), [], DateTimeOffset.UtcNow);
        using (ChangeContext.Begin(ChangeActor.Person, "adding"))
        {
            await goals.SaveAsync(kept, Token);
        }

        var made = Goal.Restore(Guid.NewGuid(), "Made by the agent", null, null, [], DateTimeOffset.UtcNow);
        var agent = await AgentAsync("goals", async () =>
        {
            await goals.DeleteAsync(kept.Id, Token);
            await goals.SaveAsync(made, Token);
        });
        Assert.Null(await goals.GetAsync(kept.Id, Token));

        var plan = Assert.IsType<RestorePlan>(await _restore.PlanAsync(agent.Id, Token));
        Assert.Equal(RestoreAction.Revert, Assert.Single(plan.Steps, step => step.Key.Id == kept.Id.ToString()).Action);
        Assert.Equal(RestoreAction.Tombstone, Assert.Single(plan.Steps, step => step.Key.Id == made.Id.ToString()).Action);

        var outcome = await _restore.RestoreAsync(agent.Id, NoOverrides, Token);

        Assert.Equal(2, outcome.Restored);
        Assert.Equal("Ship the journal", (await goals.GetAsync(kept.Id, Token))?.Title);
        Assert.Null(await goals.GetAsync(made.Id, Token));
    }

    [Fact]
    public async Task A_restore_that_writes_nothing_records_no_change_set_and_leaves_the_original_standing()
    {
        var edited = await AddAsync("Edited since", string.Empty);
        var agent = await AgentAsync("agent run", async () =>
        {
            var held = await ReloadAsync(edited);
            held.Rename("Edited since (agent)");
            await _tasks.SaveAsync(held, Token);
        });
        await PersonEditAsync(edited, "Edited since (mine)");

        var outcome = await _restore.RestoreAsync(agent.Id, NoOverrides, Token);

        Assert.Null(outcome.RestoreChangeSetId);
        Assert.Equal(0, outcome.Restored);
        Assert.Null((await _restore.PlanAsync(agent.Id, Token))!.UndoneBy);
        Assert.DoesNotContain(await _journal.ListAsync(cancellationToken: Token), entry => entry.ChangeSet.Label == "Restore of agent run");
    }

    [Fact]
    public async Task A_change_set_restored_once_is_not_restored_again()
    {
        var task = await AddAsync("Once", string.Empty);
        var agent = await AgentAsync("agent run", async () =>
        {
            var held = await ReloadAsync(task);
            held.Rename("Once (agent)");
            await _tasks.SaveAsync(held, Token);
        });
        await _restore.RestoreAsync(agent.Id, NoOverrides, Token);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _restore.RestoreAsync(agent.Id, NoOverrides, Token));
    }

    [Fact]
    public async Task A_task_whose_row_is_gone_is_a_conflict_written_back_when_restored_anyway()
    {
        var task = await AddAsync("Purged later", "Body.");
        var agent = await AgentAsync("agent run", async () =>
        {
            var held = await ReloadAsync(task);
            held.Rename("Purged later (agent)");
            await _tasks.SaveAsync(held, Token);
        });

        await using (var raw = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_tasks.DatabasePath};Pooling=False"))
        {
            await raw.OpenAsync(Token);
            await using var command = raw.CreateCommand();
            command.CommandText = "DELETE FROM tasks WHERE id = $id;";
            command.Parameters.AddWithValue("$id", task.Id.ToString());
            await command.ExecuteNonQueryAsync(Token);
        }

        Assert.True(Assert.Single((await _restore.PlanAsync(agent.Id, Token))!.Steps).Conflict);

        var outcome = await _restore.RestoreAsync(agent.Id, new HashSet<DocumentKey> { new("task", task.Id.ToString()) }, Token);

        Assert.Equal(1, outcome.Restored);
        Assert.Equal("Purged later", (await ReloadAsync(task)).Title);
    }

    [Fact]
    public async Task An_impediment_gets_its_prior_document_back_stamped_later()
    {
        var impediments = new SqliteImpedimentRepository(_temp.Path);
        var raised = Impediment.Restore(Guid.NewGuid(), "Waiting on review", null, null, null, null, [], [], DateTimeOffset.UtcNow, new DateOnly(2026, 11, 24));
        using (ChangeContext.Begin(ChangeActor.Person, "raising"))
        {
            await impediments.SaveAsync(raised, Token);
        }

        var before = (await impediments.ReadAsync(raised.Id, Token))!;
        var agent = await AgentAsync("update_impediment", async () =>
        {
            var held = (await impediments.GetAsync(raised.Id, Token))!;
            held.Rename("Waiting on review (agent)");
            await impediments.SaveAsync(held, Token);
        });
        var changed = (await impediments.ReadAsync(raised.Id, Token))!;
        Assert.NotEqual(before.Content, changed.Content);

        var outcome = await _restore.RestoreAsync(agent.Id, NoOverrides, Token);

        Assert.Equal(1, outcome.Restored);
        var restored = (await impediments.ReadAsync(raised.Id, Token))!;
        Assert.Equal(before.Content, restored.Content);
        Assert.True(restored.UpdatedAt > changed.UpdatedAt);
    }

    [Fact]
    public async Task A_change_set_the_journal_does_not_hold_has_no_plan()
    {
        Assert.Null(await _restore.PlanAsync(Guid.NewGuid(), Token));
    }

    private async Task<TaskItem> AddAsync(string title, string content)
    {
        var task = new TaskItem(title, content, EntryType.Task);
        using (ChangeContext.Begin(ChangeActor.Person, "adding"))
        {
            await _tasks.SaveAsync(task, Token);
        }

        return task;
    }

    private static async Task<ChangeSet> AgentAsync(string label, Func<Task> writes)
    {
        var agent = ChangeSet.Start(ChangeActor.Agent, label) with { Caller = "claude-code", SessionId = "session-1" };
        using (ChangeContext.Enter(agent))
        {
            await writes();
        }

        return agent;
    }

    private async Task PersonEditAsync(TaskItem task, string title)
    {
        using (ChangeContext.Begin(ChangeActor.Person, "editing"))
        {
            var held = await ReloadAsync(task);
            held.Rename(title);
            await _tasks.SaveAsync(held, Token);
        }
    }

    private async Task<TaskItem> ReloadAsync(TaskItem task) =>
        await _tasks.GetAsync(task.Id, Token) ?? throw new InvalidOperationException($"Task {task.Id} is not live.");

    private async Task<TaskItem> StoredAsync(Guid id)
    {
        await using var connection = await SqliteTaskRepository.OpenForJournalAsync(_tasks.DatabasePath, Token);
        return await SqliteTaskRepository.ReadCurrentAsync(connection, id.ToString(), Token)
            ?? throw new InvalidOperationException($"Task {id} has no row.");
    }

    public void Dispose() => _temp.Dispose();
}
