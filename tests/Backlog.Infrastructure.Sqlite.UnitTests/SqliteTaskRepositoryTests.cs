using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The store the whole product sits on. What these assert is that an aggregate
/// put in comes back out unchanged — every field, and the owned collections with
/// their order — because a store that quietly drops one is a store that loses
/// somebody's work without ever failing.
/// </summary>
public sealed class SqliteTaskRepositoryTests : IDisposable
{
    private readonly string _root;
    private readonly SqliteTaskRepository _repository;

    public SqliteTaskRepositoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "backlog-sqlite-tests", Guid.NewGuid().ToString("n"));
        _repository = new SqliteTaskRepository(_root);
    }

    [Fact]
    public async Task A_new_store_holds_nothing()
    {
        Assert.Empty(await _repository.ListAsync());
    }

    [Fact]
    public async Task The_database_is_one_file_under_the_root()
    {
        await _repository.SaveAsync(new TaskItem("Ship it", "Body.", EntryType.Task));

        Assert.Equal(Path.Combine(_root, "backlog.db"), _repository.DatabasePath);
        Assert.True(File.Exists(_repository.DatabasePath));
    }

    [Fact]
    public async Task No_markdown_file_is_written_for_a_task()
    {
        await _repository.SaveAsync(new TaskItem("Ship it", "# Heading\n\nBody.\n", EntryType.Task));

        Assert.Empty(Directory.EnumerateFiles(_root, "*.md", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_saved_task_comes_back_whole()
    {
        var task = new TaskItem(
            "Ship SpecManager",
            "# Ship SpecManager\n\nThe body, in markdown.\n",
            EntryType.Task,
            Priority.High,
            repoIds: ["JSdotNet/Backlog"],
            tags: ["ship", "q3"]);

        task.SetArea("repos");
        task.SetOrder(4);
        task.SetStatus(EntryStatus.InProgress);
        task.SetEffort(13);
        var step = task.AddSubItem("Write the tests", "xUnit");
        task.AddSubItem("Then the code");
        task.SetSubItemStatus(step.Id, SubItemStatus.Done);
        task.RecordUsage("copied");
        task.AddProjectionRef(new ProjectionRef("JSdotNet/Backlog", "42", "issue"));

        await _repository.SaveAsync(task);
        var loaded = await _repository.GetAsync(task.Id);

        Assert.NotNull(loaded);
        Assert.Equal(task.Id, loaded.Id);
        Assert.Equal("Ship SpecManager", loaded.Title);
        Assert.Equal("# Ship SpecManager\n\nThe body, in markdown.\n", loaded.ContentMd);
        Assert.Equal(EntryType.Task, loaded.Type);
        Assert.Equal(EntryStatus.InProgress, loaded.Status);
        Assert.Equal(Priority.High, loaded.Priority);
        Assert.Equal("repos", loaded.Area);
        Assert.Equal(4, loaded.Order);
        Assert.Equal(13, loaded.Effort);
        Assert.Equal(["JSdotNet/Backlog"], loaded.RepoIds);
        Assert.Equal(["ship", "q3"], loaded.Tags);
        Assert.Equal(task.CreatedAt, loaded.CreatedAt);

        Assert.Collection(
            loaded.SubItems,
            first =>
            {
                Assert.Equal("Write the tests", first.Title);
                Assert.Equal("xUnit", first.Notes);
                Assert.Equal(SubItemStatus.Done, first.Status);
                Assert.Equal(0, first.Order);
            },
            second =>
            {
                Assert.Equal("Then the code", second.Title);
                Assert.Null(second.Notes);
                Assert.Equal(SubItemStatus.Pending, second.Status);
                Assert.Equal(1, second.Order);
            });

        var usage = Assert.Single(loaded.UsageEvents);
        Assert.Equal("copied", usage.Action);
        Assert.Equal(task.UsageEvents[0].Timestamp, usage.Timestamp);

        var projection = Assert.Single(loaded.ProjectionRefs);
        Assert.Equal(new ProjectionRef("JSdotNet/Backlog", "42", "issue"), projection);
    }

    /// <summary>
    /// The content is markdown and the store is not. Nothing may normalise it —
    /// no trailing newline added, no leading whitespace trimmed — because the
    /// document is the person's, not ours.
    /// </summary>
    [Theory]
    [InlineData("# Heading\n\n- [ ] a step\n\n```csharp\nvar x = 1;\n```\n")]
    [InlineData("no trailing newline")]
    [InlineData("  leading and trailing spaces  ")]
    [InlineData("")]
    [InlineData("windows\r\nline\r\nendings\r\n")]
    public async Task Markdown_content_round_trips_byte_for_byte(string content)
    {
        var task = new TaskItem("Title", content, EntryType.Idea);

        await _repository.SaveAsync(task);
        var loaded = await _repository.GetAsync(task.Id);

        Assert.NotNull(loaded);
        Assert.Equal(content, loaded.ContentMd);
    }

    [Fact]
    public async Task Scheduling_and_dependencies_round_trip()
    {
        var task = new TaskItem("Renew the certificate", string.Empty, EntryType.Idea);
        task.SetDueOn(new DateOnly(2026, 8, 21));
        task.SetReminder(new DateTime(2026, 8, 21, 9, 0, 0, DateTimeKind.Unspecified));
        task.SetRecurrence(new Recurrence(2, RecurrenceUnit.Week));
        task.SetInMyDayOn(new DateOnly(2026, 8, 19));
        task.SetView(EntryView.Notes);
        task.SetDependsOn(["a1b2c3", "d4e5f6"]);

        await _repository.SaveAsync(task);
        var loaded = await _repository.GetAsync(task.Id);

        Assert.NotNull(loaded);
        Assert.Equal(new DateOnly(2026, 8, 21), loaded.DueOn);
        Assert.Equal(new DateTime(2026, 8, 21, 9, 0, 0), loaded.RemindAt);
        Assert.Equal(new Recurrence(2, RecurrenceUnit.Week), loaded.Recurrence);
        Assert.Equal(new DateOnly(2026, 8, 19), loaded.InMyDayOn);
        Assert.Equal(EntryView.Notes, loaded.View);
        Assert.Equal(["a1b2c3", "d4e5f6"], loaded.DependsOn);
    }

    /// <summary>
    /// A reminder is wall-clock intent: 09:00 means 09:00 wherever the person is.
    /// Storing an offset would move it, so the value must come back
    /// <see cref="DateTimeKind.Unspecified"/> rather than Utc or Local.
    /// </summary>
    [Fact]
    public async Task A_reminder_comes_back_without_a_timezone()
    {
        var task = new TaskItem("Stand-up", string.Empty, EntryType.Task);
        task.SetReminder(new DateTime(2026, 8, 21, 9, 15, 0, DateTimeKind.Unspecified));

        await _repository.SaveAsync(task);
        var loaded = await _repository.GetAsync(task.Id);

        Assert.NotNull(loaded);
        Assert.Equal(DateTimeKind.Unspecified, loaded.RemindAt!.Value.Kind);
        Assert.Equal(new DateTime(2026, 8, 21, 9, 15, 0), loaded.RemindAt);
    }

    /// <summary>Every-weekday is the one weekday-restricted repeat the grammar can
    /// spell, and it survives.</summary>
    [Fact]
    public async Task An_every_weekday_repeat_round_trips()
    {
        var weekdays = new Recurrence(
            1,
            RecurrenceUnit.Week,
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday]);

        var task = new TaskItem("Stand-up", string.Empty, EntryType.Task);
        task.SetRecurrence(weekdays);

        await _repository.SaveAsync(task);
        var loaded = await _repository.GetAsync(task.Id);

        Assert.NotNull(loaded);
        Assert.Equal(weekdays, loaded.Recurrence);
    }

    /// <summary>
    /// A weekday set the grammar has no spelling for keeps its interval and loses
    /// its days.
    /// <para>
    /// Documented rather than fixed, and deliberately so. The repeat is stored as
    /// the same <c>repeat:</c> token <see cref="EntryTextParser"/> writes on the
    /// metadata line, which "has exactly one spelling for a weekday-restricted
    /// repeat, and it is Monday-to-Friday" — every other set is unwritable there
    /// too. A database that held Tuesday-and-Thursday would be a database holding
    /// a repeat the text cannot express, so the next time the metadata line was
    /// rewritten it would vanish anyway. Two stores that disagree is worse than
    /// one that is honestly narrow, and the grammar is the published language.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_weekday_set_the_grammar_cannot_spell_falls_back_to_its_interval()
    {
        var task = new TaskItem("Water the plants", string.Empty, EntryType.Task);
        task.SetRecurrence(new Recurrence(1, RecurrenceUnit.Week, [DayOfWeek.Tuesday, DayOfWeek.Thursday]));

        await _repository.SaveAsync(task);
        var loaded = await _repository.GetAsync(task.Id);

        Assert.NotNull(loaded);
        Assert.Equal(new Recurrence(1, RecurrenceUnit.Week), loaded.Recurrence);
    }

    [Fact]
    public async Task The_recurrence_provenance_id_round_trips()
    {
        var source = Guid.NewGuid();
        var task = new TaskItem(
            Guid.NewGuid(),
            "Next occurrence",
            string.Empty,
            EntryType.Task,
            EntryStatus.Ready,
            Priority.Medium,
            repoIds: null,
            tags: null,
            sourceInboxId: "inbox-7",
            createdAt: DateTimeOffset.UtcNow,
            recurrenceSourceId: source);

        await _repository.SaveAsync(task);
        var loaded = await _repository.GetAsync(task.Id);

        Assert.NotNull(loaded);
        Assert.Equal(source, loaded.RecurrenceSourceId);
        Assert.Equal("inbox-7", loaded.SourceInboxId);
    }

    [Fact]
    public async Task Saving_the_same_task_twice_updates_it_rather_than_duplicating_it()
    {
        var task = new TaskItem("First title", "First body.", EntryType.Task);
        await _repository.SaveAsync(task);

        task.Rename("Second title");
        task.UpdateContent("Second body.");
        await _repository.SaveAsync(task);

        var all = await _repository.ListAsync();
        var only = Assert.Single(all);
        Assert.Equal("Second title", only.Title);
        Assert.Equal("Second body.", only.ContentMd);
    }

    [Fact]
    public async Task Listing_puts_hand_ranked_tasks_first_and_falls_back_to_newest()
    {
        var ranked = new TaskItem("Ranked second", string.Empty, EntryType.Task);
        ranked.SetOrder(2);
        var rankedFirst = new TaskItem("Ranked first", string.Empty, EntryType.Task);
        rankedFirst.SetOrder(1);

        // Two that nobody ranked: they share rank 0, so recency decides.
        var older = Rehydrate("Older", DateTimeOffset.UtcNow.AddDays(-2));
        var newer = Rehydrate("Newer", DateTimeOffset.UtcNow.AddDays(-1));

        foreach (var task in new[] { ranked, older, rankedFirst, newer })
        {
            await _repository.SaveAsync(task);
        }

        Assert.Equal(
            ["Newer", "Older", "Ranked first", "Ranked second"],
            (await _repository.ListAsync()).Select(task => task.Title));
    }

    [Fact]
    public async Task Listing_returns_whole_aggregates_including_their_steps()
    {
        var task = new TaskItem("Has steps", "Body.", EntryType.Task);
        task.AddSubItem("A step");
        await _repository.SaveAsync(task);

        var only = Assert.Single(await _repository.ListAsync());
        Assert.Equal("Body.", only.ContentMd);
        Assert.Equal("A step", Assert.Single(only.SubItems).Title);
    }

    [Fact]
    public async Task Getting_a_task_that_is_not_there_is_not_an_error()
    {
        Assert.Null(await _repository.GetAsync(Guid.NewGuid()));
    }

    /// <summary>
    /// Deleting is saving a tombstone, and both reads have to hide what it marks.
    /// This is the half the app sees, and it has to look exactly like the removal
    /// it replaced.
    /// </summary>
    [Fact]
    public async Task A_tombstoned_task_is_gone_to_every_read()
    {
        var task = new TaskItem("Delete me", string.Empty, EntryType.Task);
        await _repository.SaveAsync(task);

        task.MarkDeleted();
        await _repository.SaveAsync(task);

        Assert.Null(await _repository.GetAsync(task.Id));
        Assert.Empty(await _repository.ListAsync());
    }

    /// <summary>
    /// And the other half: the row is still there. That is the whole point of a
    /// tombstone — a task that is simply gone from this machine is
    /// indistinguishable from one the other machine has never seen, so the
    /// deletion would be undone by the next reconciliation. Asserted against the
    /// file with raw SQL, because every read through the port is supposed to be
    /// unable to see it.
    /// </summary>
    [Fact]
    public async Task A_tombstoned_task_keeps_its_row_and_its_stamp()
    {
        var task = new TaskItem("Delete me", string.Empty, EntryType.Task);
        await _repository.SaveAsync(task);

        task.MarkDeleted();
        await _repository.SaveAsync(task);

        var (rows, deletedAt) = await ReadTombstoneAsync(task.Id);

        Assert.Equal(1, rows);
        Assert.NotNull(deletedAt);
        Assert.Equal(
            task.DeletedAt!.Value,
            DateTimeOffset.Parse(
                deletedAt!,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind));
    }

    /// <summary>Deleting the same task twice keeps the first instant. The second
    /// call says nothing new, and moving the stamp would quietly extend the
    /// tombstone's life past the retention its first deletion started.</summary>
    [Fact]
    public async Task Deleting_an_already_deleted_task_does_not_move_the_tombstone()
    {
        var task = new TaskItem("Delete me", string.Empty, EntryType.Task);
        await _repository.SaveAsync(task);

        task.MarkDeleted();
        var first = task.DeletedAt;

        task.MarkDeleted();

        Assert.Equal(first, task.DeletedAt);
    }

    /// <summary>
    /// The store survives the process that wrote it. This is the claim the whole
    /// change rests on, so it is asserted through a second repository over the
    /// same folder rather than through the one that did the writing.
    /// </summary>
    [Fact]
    public async Task A_task_survives_a_new_repository_over_the_same_folder()
    {
        var task = new TaskItem("Outlives the session", "Body.", EntryType.Task);
        task.AddSubItem("A step");
        await _repository.SaveAsync(task);

        var reopened = new SqliteTaskRepository(_root);
        var loaded = await reopened.GetAsync(task.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Outlives the session", loaded.Title);
        Assert.Equal("A step", Assert.Single(loaded.SubItems).Title);
    }

    /// <summary>A store rooted where nothing exists yet makes what it needs
    /// rather than refusing: a first run is the ordinary case.</summary>
    [Fact]
    public async Task A_root_that_does_not_exist_yet_is_created()
    {
        var nested = Path.Combine(_root, "not", "there", "yet");
        var repository = new SqliteTaskRepository(nested);

        await repository.SaveAsync(new TaskItem("First ever", string.Empty, EntryType.Idea));

        Assert.Single(await repository.ListAsync());
    }

    /// <summary>
    /// The one that matters. A database an earlier build wrote — before the effort
    /// column existed — still opens, upgrades itself, and reads. There is no
    /// migration framework here on purpose, so <c>OpenAsync</c> brings the column
    /// in with an additive <c>ALTER</c> once <c>PRAGMA table_info</c> shows it
    /// absent, and a row written under the old schema reads back with a null
    /// estimate because the column it never had is null for it.
    /// </summary>
    [Fact]
    public async Task A_database_written_before_the_effort_column_still_opens_and_reads()
    {
        var id = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

        Directory.CreateDirectory(_root);
        var path = _repository.DatabasePath;

        // Hand-build the pre-effort schema and a row in it, through no code path
        // that knows the column was ever added.
        await using (var seed = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            await seed.OpenAsync();

            await using var create = seed.CreateCommand();
            create.CommandText = """
                CREATE TABLE tasks (
                    id TEXT PRIMARY KEY NOT NULL, title TEXT NOT NULL, content_md TEXT NOT NULL DEFAULT '',
                    type TEXT NOT NULL, status TEXT NOT NULL, priority TEXT NOT NULL,
                    sort_order INTEGER NOT NULL DEFAULT 0, area TEXT NULL, created_at TEXT NOT NULL,
                    source_inbox_id TEXT NULL, recurrence_source_id TEXT NULL, due_on TEXT NULL,
                    remind_at TEXT NULL, recurrence TEXT NULL, in_my_day_on TEXT NULL, view TEXT NULL,
                    tags TEXT NOT NULL DEFAULT '[]', repo_ids TEXT NOT NULL DEFAULT '[]',
                    depends_on TEXT NOT NULL DEFAULT '[]', sub_items TEXT NOT NULL DEFAULT '[]',
                    usage_events TEXT NOT NULL DEFAULT '[]', projections TEXT NOT NULL DEFAULT '[]'
                );
                """;
            await create.ExecuteNonQueryAsync();

            await using var insert = seed.CreateCommand();
            insert.CommandText = """
                INSERT INTO tasks (id, title, type, status, priority, created_at)
                VALUES ($id, $title, 'task', 'ready', 'high', $created_at);
                """;
            insert.Parameters.AddWithValue("$id", id.ToString());
            insert.Parameters.AddWithValue("$title", "Written before effort existed");
            insert.Parameters.AddWithValue(
                "$created_at",
                createdAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            await insert.ExecuteNonQueryAsync();
        }

        // Release the seed connection's handle on the file before the repository
        // opens over it.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Opening over that file must not throw, and the old row reads back with no
        // estimate — the additive column defaults to null for a row that predates
        // it.
        var loaded = await _repository.GetAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal("Written before effort existed", loaded.Title);
        Assert.Null(loaded.Effort);

        // And the upgraded column is writable: a fresh estimate saves and reads.
        loaded.SetEffort(5);
        await _repository.SaveAsync(loaded);
        var again = await _repository.GetAsync(id);

        Assert.Equal(5, again!.Effort);
    }

    /// <summary>
    /// A row written when <c>follow_up</c> was still a type reads back as a task
    /// rather than throwing. <c>ParseType</c> no longer knows the word, so a file
    /// carrying it would fail every read of that row — which is a person's entry
    /// lost to a vocabulary change. Opening normalizes the retired value away
    /// instead, once, in place.
    /// </summary>
    [Fact]
    public async Task A_row_written_with_the_retired_follow_up_type_reads_back_as_a_task()
    {
        var id = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

        // The table has to exist before the raw insert can put a retired value in
        // it, and the repository's own open is what creates it.
        await _repository.SaveAsync(Rehydrate("Something else entirely", createdAt));

        await using (var seed = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = _repository.DatabasePath }.ToString()))
        {
            await seed.OpenAsync();

            await using var insert = seed.CreateCommand();
            insert.CommandText = """
                INSERT INTO tasks (id, title, type, status, priority, created_at)
                VALUES ($id, $title, 'follow_up', 'ready', 'medium', $created_at);
                """;
            insert.Parameters.AddWithValue("$id", id.ToString());
            insert.Parameters.AddWithValue("$title", "Rework the onboarding email");
            insert.Parameters.AddWithValue(
                "$created_at",
                createdAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            await insert.ExecuteNonQueryAsync();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var loaded = await _repository.GetAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal("Rework the onboarding email", loaded.Title);
        Assert.Equal(EntryType.Task, loaded.Type);
    }

    /// <summary>
    /// The assertion the whole sync design rests on, and the one a test over the
    /// aggregate structurally cannot make.
    /// <para>
    /// Rehydration has no load-only path: <c>Read</c> replays a stored task
    /// through the ordinary command-side setters, and every one of those restamps
    /// <c>UpdatedAt</c> to now. So a read that ended anywhere but
    /// <c>LoadStamps</c> would overwrite the column it had just read, every task
    /// would look as though it had been edited the instant it was loaded, and
    /// last-write-wins would compare load times instead of edit times. A stamp
    /// written far enough in the past that "now" could never be mistaken for it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Reading_a_task_does_not_restamp_it()
    {
        var createdAt = new DateTimeOffset(2026, 1, 5, 8, 30, 0, TimeSpan.Zero);
        var task = Rehydrate("Written a while ago", createdAt);

        // Give it every field the load path replays through a mutator, so a
        // restamp anywhere in Read has something to fire on.
        task.SetArea("projects");
        task.SetEffort(3);
        task.SetDueOn(new DateOnly(2026, 2, 1));
        task.SetDependsOn(["other"]);
        task.AddProjectionRef(new ProjectionRef("JSdotNet/Backlog", "1", "issue"));
        task.LoadStamps(createdAt, deletedAt: null);

        await _repository.SaveAsync(task);

        var loaded = await _repository.GetAsync(task.Id);

        Assert.NotNull(loaded);
        Assert.Equal(createdAt, loaded.UpdatedAt);
        Assert.Null(loaded.DeletedAt);
    }

    /// <summary>An edit moves the stamp forward, which is the other half of the
    /// same contract: reading must not touch it, and editing must.</summary>
    [Fact]
    public async Task Editing_a_task_moves_its_stamp_past_what_was_stored()
    {
        var createdAt = new DateTimeOffset(2026, 1, 5, 8, 30, 0, TimeSpan.Zero);
        var task = Rehydrate("Edit me", createdAt);
        task.LoadStamps(createdAt, deletedAt: null);
        await _repository.SaveAsync(task);

        var loaded = await _repository.GetAsync(task.Id);
        loaded!.Rename("Edited");
        await _repository.SaveAsync(loaded);

        var reloaded = await _repository.GetAsync(task.Id);

        Assert.NotNull(reloaded);
        Assert.True(reloaded.UpdatedAt > createdAt);
    }

    /// <summary>
    /// A database an earlier build wrote — before either stamp column existed —
    /// still opens and reads, and its rows come back stamped with their own
    /// creation time rather than with nothing.
    /// <para>
    /// Left null they would be unusable rather than merely unknown: the sync push
    /// asks for documents changed since a watermark, and <c>null &gt; watermark</c>
    /// is never true, so a row that predates the column would never travel and
    /// would stay invisible to the person's other machine for good. Creation time
    /// is the weakest statement that is actually true of it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_database_written_before_the_stamp_columns_reads_back_stamped_from_creation()
    {
        var id = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

        Directory.CreateDirectory(_root);

        // Hand-build the pre-stamp schema and a row in it, through no code path
        // that knows either column was ever added.
        await using (var seed = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = _repository.DatabasePath }.ToString()))
        {
            await seed.OpenAsync();

            await using var create = seed.CreateCommand();
            create.CommandText = """
                CREATE TABLE tasks (
                    id TEXT PRIMARY KEY NOT NULL, title TEXT NOT NULL, content_md TEXT NOT NULL DEFAULT '',
                    type TEXT NOT NULL, status TEXT NOT NULL, priority TEXT NOT NULL,
                    sort_order INTEGER NOT NULL DEFAULT 0, area TEXT NULL, created_at TEXT NOT NULL,
                    source_inbox_id TEXT NULL, recurrence_source_id TEXT NULL, due_on TEXT NULL,
                    remind_at TEXT NULL, recurrence TEXT NULL, in_my_day_on TEXT NULL, view TEXT NULL,
                    tags TEXT NOT NULL DEFAULT '[]', repo_ids TEXT NOT NULL DEFAULT '[]',
                    depends_on TEXT NOT NULL DEFAULT '[]', sub_items TEXT NOT NULL DEFAULT '[]',
                    usage_events TEXT NOT NULL DEFAULT '[]', projections TEXT NOT NULL DEFAULT '[]',
                    effort INTEGER NULL, import_plan_id TEXT NULL, import_item_id TEXT NULL
                );
                """;
            await create.ExecuteNonQueryAsync();

            await using var insert = seed.CreateCommand();
            insert.CommandText = """
                INSERT INTO tasks (id, title, type, status, priority, created_at)
                VALUES ($id, $title, 'task', 'ready', 'high', $created_at);
                """;
            insert.Parameters.AddWithValue("$id", id.ToString());
            insert.Parameters.AddWithValue("$title", "Written before the stamps existed");
            insert.Parameters.AddWithValue(
                "$created_at",
                createdAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            await insert.ExecuteNonQueryAsync();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var loaded = await _repository.GetAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal("Written before the stamps existed", loaded.Title);
        Assert.Equal(createdAt, loaded.UpdatedAt);

        // A row that predates the tombstone column is live, not deleted. Null there
        // is the value rather than a missing one, so the old row needs no seeding
        // and must not read as gone.
        Assert.Null(loaded.DeletedAt);
        Assert.Single(await _repository.ListAsync());

        // And the backfill actually wrote the column rather than the read merely
        // coalescing it, so the very first sync sees a real value.
        var stored = await ReadUpdatedAtAsync(id);
        Assert.NotNull(stored);
        Assert.Equal(
            createdAt,
            DateTimeOffset.Parse(
                stored!,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind));
    }

    /// <summary>Reads the tombstone straight out of the file. Every read through
    /// the port hides it, so raw SQL is the only way to assert the row is still
    /// there.</summary>
    private async Task<(int Rows, string? DeletedAt)> ReadTombstoneAsync(Guid id)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = _repository.DatabasePath }.ToString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MAX(deleted_at) FROM tasks WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return (reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private async Task<string?> ReadUpdatedAtAsync(Guid id)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = _repository.DatabasePath }.ToString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT updated_at FROM tasks WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : (string)value;
    }

    private static TaskItem Rehydrate(string title, DateTimeOffset createdAt) =>
        new(
            Guid.NewGuid(),
            title,
            string.Empty,
            EntryType.Task,
            EntryStatus.Draft,
            Priority.Medium,
            repoIds: null,
            tags: null,
            sourceInboxId: null,
            createdAt: createdAt);

    public void Dispose()
    {
        // The connection pool can still hold the file open the instant a test
        // ends, and a temp folder that will not delete is not a failure worth
        // failing a green suite over.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
