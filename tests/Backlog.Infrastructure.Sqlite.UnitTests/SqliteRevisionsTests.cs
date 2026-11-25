using System.Globalization;
using System.Text.Json;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.RecordTaskUsage;
using Backlog.SharedKernel.Changes;
using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The change journal's <c>revisions</c> (local ADR 0061 §2): the triggers on
/// <c>tasks</c> keep one before-image per document per change set, file a write the
/// app did not name under an unknown writer, and skip a write kept out of the journal.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteRevisionsTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new("backlog-revisions");

    private CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_agent_change_set_over_three_tasks_yields_three_revisions()
    {
        var repository = new SqliteTaskRepository(_temp.Path);
        var tasks = new[] { "One", "Two", "Three" }
            .Select(title => new TaskItem(title, string.Empty, EntryType.Task))
            .ToList();

        using (ChangeContext.Begin(ChangeActor.Person, "adding"))
        {
            await repository.SaveManyAsync(tasks, Token);
        }

        var agent = ChangeSet.Start(ChangeActor.Agent, "agent run") with { Caller = "grant:test" };
        using (ChangeContext.Enter(agent))
        {
            foreach (var task in tasks)
            {
                task.Rename($"{task.Title} (agent)");
                await repository.SaveAsync(task, Token);
                task.Rename($"{task.Title} again");
                await repository.SaveAsync(task, Token);
            }
        }

        var revisions = (await ReadAsync(repository.DatabasePath))
            .Where(revision => revision.ChangeSetId == agent.Id.ToString())
            .ToList();

        Assert.Equal(3, revisions.Count);
        Assert.Equal(
            ["One", "Three", "Two"],
            revisions.Select(revision => TitleOf(revision.Before)).Order());
        Assert.All(revisions, revision => Assert.Equal("task", revision.DocumentKind));
        Assert.All(tasks, task => Assert.Equal(
            task.UpdatedAt.ToString("O", CultureInfo.InvariantCulture),
            revisions.Single(revision => revision.DocumentId == task.Id.ToString()).AfterUpdatedAt));
    }

    [Fact]
    public async Task The_roadmap_plan_goals_and_impediments_are_journalled_under_their_own_kinds()
    {
        var roadmap = new Roadmap.SqliteRoadmapPlanRepository(_temp.Path);
        var goals = new Progress.SqliteGoalRepository(_temp.Path);
        var impediments = new Progress.SqliteImpedimentRepository(_temp.Path);

        var goal = ((Backlog.SharedKernel.Results.Ok<Backlog.Modules.Progress.DomainModels.Goal>)
            Backlog.Modules.Progress.DomainModels.Goal.Create(Guid.NewGuid(), "Goal", null, null).Value!).Value;
        var impediment = ((Backlog.SharedKernel.Results.Ok<Backlog.Modules.Progress.DomainModels.Impediment>)
            Backlog.Modules.Progress.DomainModels.Impediment.Raise(Guid.NewGuid(), "Impediment", new DateOnly(2026, 11, 25)).Value!).Value;

        var change = ChangeSet.Start(ChangeActor.Person, "planning");
        using (ChangeContext.Enter(change))
        {
            await roadmap.SaveAsync(Backlog.Modules.Roadmap.DomainModels.RoadmapPlan.Empty(), Token);
            await goals.SaveAsync(goal, Token);
            await impediments.SaveAsync(impediment, Token);
            await goals.DeleteAsync(goal.Id, Token);
        }

        var revisions = await ReadAsync(roadmap.DatabasePath);

        Assert.All(revisions, revision => Assert.Equal(change.Id.ToString(), revision.ChangeSetId));
        Assert.Equal(
            ["goal", "impediment", "roadmap-plan"],
            revisions.Select(revision => revision.DocumentKind).Order());
        Assert.All(revisions, revision => Assert.Null(revision.Before));
    }

    [Fact]
    public async Task A_typing_session_yields_one_revision()
    {
        var repository = new SqliteTaskRepository(_temp.Path);
        var task = new TaskItem("Draft", string.Empty, EntryType.Task);
        var typing = ChangeSet.Start(ChangeActor.Person, "editing “Draft”");

        // Each debounced save of a typing session joins the session's change set.
        foreach (var title in new[] { "Draft", "Draft a", "Draft a pl", "Draft a plan" })
        {
            using (ChangeContext.Enter(typing))
            {
                task.Rename(title);
                await repository.SaveAsync(task, Token);
            }
        }

        var revision = Assert.Single(await ReadAsync(repository.DatabasePath));
        Assert.Equal(typing.Id.ToString(), revision.ChangeSetId);
        Assert.Null(revision.Before);
        Assert.Equal(task.UpdatedAt.ToString("O", CultureInfo.InvariantCulture), revision.AfterUpdatedAt);
    }

    [Fact]
    public async Task Each_change_set_keeps_its_own_before_image()
    {
        var repository = new SqliteTaskRepository(_temp.Path);
        var task = new TaskItem("First", string.Empty, EntryType.Task);

        using (ChangeContext.Begin(ChangeActor.Person, "adding"))
        {
            await repository.SaveAsync(task, Token);
        }

        using (ChangeContext.Begin(ChangeActor.Agent, "renaming"))
        {
            task.Rename("Second");
            await repository.SaveAsync(task, Token);
        }

        var revisions = await ReadAsync(repository.DatabasePath);
        Assert.Equal([null, "First"], revisions.Select(revision => TitleOf(revision.Before)));
    }

    [Fact]
    public async Task A_write_through_a_separate_raw_connection_yields_an_unknown_writer_revision()
    {
        var repository = new SqliteTaskRepository(_temp.Path);
        var task = new TaskItem("Before sqlite3", string.Empty, EntryType.Task);

        using (ChangeContext.Begin(ChangeActor.Person, "adding"))
        {
            await repository.SaveAsync(task, Token);
        }

        await using (var raw = new SqliteConnection($"Data Source={repository.DatabasePath}"))
        {
            await raw.OpenAsync(Token);
            await using var command = raw.CreateCommand();
            command.CommandText = "UPDATE tasks SET title = 'After sqlite3' WHERE id = $id;";
            command.Parameters.AddWithValue("$id", task.Id.ToString());
            Assert.Equal(1, await command.ExecuteNonQueryAsync(Token));
        }

        var revision = (await ReadAsync(repository.DatabasePath)).Last();
        Assert.Equal("Before sqlite3", TitleOf(revision.Before));
        Assert.Equal("unknown", await ActorOfAsync(repository.DatabasePath, revision.ChangeSetId));
        Assert.True(Guid.TryParse(revision.ChangeSetId, out _));
        Assert.Equal("After sqlite3", (await repository.GetAsync(task.Id, Token))!.Title);
    }

    [Fact]
    public async Task Recording_a_use_writes_no_revision()
    {
        var repository = new SqliteTaskRepository(_temp.Path);
        var task = new TaskItem("Copied into a prompt", string.Empty, EntryType.Task);

        using (ChangeContext.Begin(ChangeActor.Person, "adding"))
        {
            await repository.SaveAsync(task, Token);
        }

        using (ChangeContext.Begin(ChangeActor.Agent, "copying"))
        {
            await new RecordTaskUsageCommandHandler(repository)
                .Handle(new RecordTaskUsageCommand(task.Id, "copied"), Token);
        }

        Assert.Single(await ReadAsync(repository.DatabasePath));
        Assert.Single((await repository.GetAsync(task.Id, Token))!.UsageEvents);
        Assert.Equal(0L, await ScalarAsync(repository.DatabasePath, "SELECT count(*) FROM journal_context;"));
    }

    [Fact]
    public async Task No_journal_context_outlives_its_write()
    {
        var repository = new SqliteTaskRepository(_temp.Path);

        using (ChangeContext.Begin(ChangeActor.Person, "adding"))
        {
            await repository.SaveAsync(new TaskItem("One", string.Empty, EntryType.Task), Token);
            await repository.SaveManyAsync([new TaskItem("Two", string.Empty, EntryType.Task)], Token);
        }

        Assert.Equal(0L, await ScalarAsync(repository.DatabasePath, "SELECT count(*) FROM journal_context;"));
    }

    private static string? TitleOf(string? before) =>
        before is null ? null : JsonElement.Parse(before).GetProperty("title").GetString();

    private sealed record Revision(
        string ChangeSetId,
        string DocumentKind,
        string DocumentId,
        string? Before,
        string? AfterUpdatedAt);

    private async Task<IReadOnlyList<Revision>> ReadAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(Token);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT change_set_id, document_kind, document_id, before, after_updated_at
            FROM revisions ORDER BY sequence;
            """;

        var rows = new List<Revision>();
        await using var reader = await command.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token))
        {
            rows.Add(new Revision(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return rows;
    }

    private async Task<string?> ActorOfAsync(string databasePath, string changeSetId)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT actor_kind FROM change_sets WHERE id = $id;";
        command.Parameters.AddWithValue("$id", changeSetId);
        return await command.ExecuteScalarAsync(Token) as string;
    }

    private async Task<long> ScalarAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync(Token))!;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _temp.Dispose();
    }
}
