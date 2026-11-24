using Backlog.Infrastructure.Sqlite.Roadmap;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.SharedKernel.Changes;
using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The change journal's <c>change_sets</c> (local ADR 0061 §2): a journalled write
/// records the change set it ran under, once, and a write under none records nothing.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteChangeSetsTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new("backlog-change-sets");

    private CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_save_under_a_change_set_records_it_with_its_actor_and_label()
    {
        var repository = new SqliteTaskRepository(_temp.Path);

        using (ChangeContext.Begin(ChangeActor.Person, "editing “Ship it”"))
        {
            await repository.SaveAsync(new TaskItem("Ship it", string.Empty, EntryType.Task), Token);
        }

        var row = Assert.Single(await ReadAsync(repository.DatabasePath));
        Assert.Equal(("person", "editing “Ship it”"), (row.Actor, row.Label));
    }

    [Fact]
    public async Task Several_saves_under_one_change_set_leave_one_row()
    {
        var repository = new SqliteTaskRepository(_temp.Path);

        using (ChangeContext.Begin(ChangeActor.Sync, "applying a task sync page"))
        {
            await repository.SaveAsync(new TaskItem("One", string.Empty, EntryType.Task), Token);
            await repository.SaveAsync(new TaskItem("Two", string.Empty, EntryType.Task), Token);
            await repository.SaveManyAsync([new TaskItem("Three", string.Empty, EntryType.Task)], Token);
        }

        var row = Assert.Single(await ReadAsync(repository.DatabasePath));
        Assert.Equal("sync", row.Actor);
    }

    [Fact]
    public async Task Each_change_set_is_its_own_row()
    {
        var repository = new SqliteTaskRepository(_temp.Path);

        using (ChangeContext.Begin(ChangeActor.Person, "first"))
        {
            await repository.SaveAsync(new TaskItem("One", string.Empty, EntryType.Task), Token);
        }

        using (ChangeContext.Begin(ChangeActor.System, "second"))
        {
            await repository.SaveAsync(new TaskItem("Two", string.Empty, EntryType.Task), Token);
        }

        Assert.Equal(["person", "system"], (await ReadAsync(repository.DatabasePath)).Select(row => row.Actor).Order());
    }

    [Fact]
    public async Task A_task_save_under_no_change_set_is_recorded_under_an_unknown_writer()
    {
        var repository = new SqliteTaskRepository(_temp.Path);
        var task = new TaskItem("Unattributed", string.Empty, EntryType.Task);

        await repository.SaveAsync(task, Token);

        var row = Assert.Single(await ReadAsync(repository.DatabasePath));
        Assert.Equal(("unknown", (string?)null), (row.Actor, row.Label));
        Assert.NotNull(await repository.GetAsync(task.Id, Token));
    }

    [Fact]
    public async Task An_unjournalled_save_records_no_change_set()
    {
        var repository = new SqliteTaskRepository(_temp.Path);

        using (ChangeContext.Begin(ChangeActor.Person, "noting a use"))
        using (ChangeContext.Unjournalled())
        {
            await repository.SaveAsync(new TaskItem("Used", string.Empty, EntryType.Task), Token);
        }

        Assert.Empty(await ReadAsync(repository.DatabasePath));
    }

    [Fact]
    public async Task A_roadmap_plan_save_records_its_change_set_too()
    {
        var plans = new SqliteRoadmapPlanRepository(_temp.Path);

        using (ChangeContext.Begin(ChangeActor.Person, "importing a plan"))
        {
            await plans.SaveAsync(RoadmapPlan.Empty(), Token);
        }

        var row = Assert.Single(await ReadAsync(plans.DatabasePath));
        Assert.Equal("importing a plan", row.Label);
        Assert.NotNull(await plans.ReadAsync(Token));
    }

    /// <summary>The table read with plain SQL: an empty list when no write ever
    /// created it.</summary>
    private async Task<IReadOnlyList<(string Actor, string? Label)>> ReadAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(Token);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT actor_kind, label FROM change_sets;";

        var rows = new List<(string, string?)>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(Token);
            while (await reader.ReadAsync(Token))
            {
                rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
            }
        }
        catch (SqliteException ex) when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
        {
        }

        return rows;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _temp.Dispose();
    }
}
