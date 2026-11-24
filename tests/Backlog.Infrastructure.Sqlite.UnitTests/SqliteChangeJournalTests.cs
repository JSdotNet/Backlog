using Backlog.Infrastructure.Sqlite.Changes;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.SharedKernel.Changes;
using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The change journal's read side (local ADR 0061 §2): the change sets newest first
/// with their document counts, and each document's text before the change set beside
/// its text now.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteChangeJournalTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new("backlog-change-journal");

    private CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Change_sets_list_newest_first_with_their_document_counts()
    {
        var repository = new SqliteTaskRepository(_temp.Path);
        var tasks = new[] { "One", "Two" }.Select(title => new TaskItem(title, string.Empty, EntryType.Task)).ToList();
        var time = new ManualTime(new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero));

        var adding = ChangeSet.Start(ChangeActor.Person, "adding", time);
        using (ChangeContext.Enter(adding))
        {
            await repository.SaveManyAsync(tasks, Token);
        }

        time.Advance(TimeSpan.FromMinutes(5));
        var agent = ChangeSet.Start(ChangeActor.Agent, "agent run", time) with
        {
            Caller = "claude-code",
            SessionId = "session-1",
            CorrelationId = "call-1",
        };
        using (ChangeContext.Enter(agent))
        {
            tasks[0].Rename("One (agent)");
            await repository.SaveAsync(tasks[0], Token);
        }

        var journal = new SqliteChangeJournal(repository.DatabasePath);
        var entries = await journal.ListAsync(cancellationToken: Token);

        Assert.Equal([agent.Id, adding.Id], entries.Select(entry => entry.ChangeSet.Id));
        Assert.Equal([1, 2], entries.Select(entry => entry.DocumentCount));

        var newest = entries[0].ChangeSet;
        Assert.Equal(ChangeActor.Agent, newest.Actor);
        Assert.Equal("agent run", newest.Label);
        Assert.Equal("claude-code", newest.Caller);
        Assert.Equal("session-1", newest.SessionId);
        Assert.Equal("call-1", newest.CorrelationId);
        Assert.Equal(agent.StartedAt, newest.StartedAt);
    }

    [Fact]
    public async Task A_session_filter_keeps_only_that_sessions_change_sets_whatever_its_case()
    {
        var repository = new SqliteTaskRepository(_temp.Path);
        var task = new TaskItem("Shared", string.Empty, EntryType.Task);

        using (ChangeContext.Begin(ChangeActor.Person, "adding"))
        {
            await repository.SaveAsync(task, Token);
        }

        var mine = ChangeSet.Start(ChangeActor.Agent, "mine") with { SessionId = "ABC-session" };
        using (ChangeContext.Enter(mine))
        {
            task.Rename("Mine");
            await repository.SaveAsync(task, Token);
        }

        using (ChangeContext.Enter(ChangeSet.Start(ChangeActor.Agent, "theirs") with { SessionId = "other" }))
        {
            task.Rename("Theirs");
            await repository.SaveAsync(task, Token);
        }

        var journal = new SqliteChangeJournal(repository.DatabasePath);
        var entry = Assert.Single(await journal.ListAsync("abc-session", cancellationToken: Token));

        Assert.Equal(mine.Id, entry.ChangeSet.Id);
    }

    [Fact]
    public async Task The_limit_keeps_the_newest()
    {
        var repository = new SqliteTaskRepository(_temp.Path);
        var task = new TaskItem("Busy", string.Empty, EntryType.Task);
        var time = new ManualTime(new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero));
        var ids = new List<Guid>();

        for (var i = 0; i < 3; i++)
        {
            time.Advance(TimeSpan.FromMinutes(1));
            var changeSet = ChangeSet.Start(ChangeActor.Person, $"edit {i}", time);
            ids.Add(changeSet.Id);
            using (ChangeContext.Enter(changeSet))
            {
                task.Rename($"Busy {i}");
                await repository.SaveAsync(task, Token);
            }
        }

        var journal = new SqliteChangeJournal(repository.DatabasePath);
        var entries = await journal.ListAsync(limit: 2, cancellationToken: Token);

        Assert.Equal([ids[2], ids[1]], entries.Select(entry => entry.ChangeSet.Id));
    }

    [Fact]
    public async Task A_tasks_before_and_current_text_are_its_entry_text()
    {
        var repository = new SqliteTaskRepository(_temp.Path);
        var task = new TaskItem("Plan the trip", "Book the train.", EntryType.Task);

        using (ChangeContext.Begin(ChangeActor.Person, "adding"))
        {
            await repository.SaveAsync(task, Token);
        }

        var agent = ChangeSet.Start(ChangeActor.Agent, "agent run");
        using (ChangeContext.Enter(agent))
        {
            task.SetStatus(EntryStatus.Done, new DateOnly(2026, 11, 25));
            task.Rename("Plan the trip (done)");
            await repository.SaveAsync(task, Token);
        }

        var journal = new SqliteChangeJournal(repository.DatabasePath);
        var document = Assert.Single(await journal.ReadAsync(agent.Id, Token));

        Assert.Equal("task", document.Kind);
        Assert.Equal(task.Id.ToString(), document.Id);
        Assert.Equal("Plan the trip (done)", document.Title);
        Assert.NotNull(document.Before);
        Assert.StartsWith("# Plan the trip\n", document.Before.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.DoesNotContain("`!done`", document.Before, StringComparison.Ordinal);
        Assert.Contains("Book the train.", document.Before, StringComparison.Ordinal);
        Assert.NotNull(document.Current);
        Assert.StartsWith("# Plan the trip (done)\n", document.Current.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("`!done`", document.Current, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_document_the_change_set_created_has_no_before_text()
    {
        var repository = new SqliteTaskRepository(_temp.Path);
        var task = new TaskItem("Brand new", string.Empty, EntryType.Task);
        var adding = ChangeSet.Start(ChangeActor.Person, "adding");

        using (ChangeContext.Enter(adding))
        {
            await repository.SaveAsync(task, Token);
        }

        var document = Assert.Single(await new SqliteChangeJournal(repository.DatabasePath).ReadAsync(adding.Id, Token));

        Assert.Null(document.Before);
        Assert.Equal("Brand new", document.Title);
        Assert.Contains("# Brand new", document.Current, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_kinds_before_text_is_its_fields_one_per_line()
    {
        var repository = new SqliteTaskRepository(_temp.Path);
        var task = new TaskItem("Anchor", string.Empty, EntryType.Task);
        var changeSet = ChangeSet.Start(ChangeActor.Agent, "goal edit");

        using (ChangeContext.Enter(changeSet))
        {
            await repository.SaveAsync(task, Token);
        }

        await using (var raw = new SqliteConnection($"Data Source={repository.DatabasePath}"))
        {
            await raw.OpenAsync(Token);
            await using var command = raw.CreateCommand();
            command.CommandText = """
                INSERT INTO revisions (change_set_id, document_kind, document_id, before, after_updated_at, recorded_at)
                VALUES ($set, 'goal', 'goal-1', '{"id":"goal-1","title":"Ship it","weight":3,"note":null}', NULL, '2026-11-25T09:00:00.0000000+00:00');
                """;
            command.Parameters.AddWithValue("$set", changeSet.Id.ToString());
            await command.ExecuteNonQueryAsync(Token);
        }

        var documents = await new SqliteChangeJournal(repository.DatabasePath).ReadAsync(changeSet.Id, Token);
        var goal = Assert.Single(documents, document => document.Kind == "goal");

        Assert.Equal("Ship it", goal.Title);
        Assert.Equal("id: goal-1\ntitle: Ship it\nweight: 3\nnote: ", goal.Before);
        Assert.Null(goal.Current);
    }

    [Fact]
    public async Task A_journal_over_a_database_never_written_is_empty()
    {
        var journal = new SqliteChangeJournal(SqliteTaskRepository.DatabasePathFor(_temp.Path));

        Assert.Empty(await journal.ListAsync(cancellationToken: Token));
        Assert.Empty(await journal.ReadAsync(Guid.NewGuid(), Token));
    }

    public void Dispose() => _temp.Dispose();

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
