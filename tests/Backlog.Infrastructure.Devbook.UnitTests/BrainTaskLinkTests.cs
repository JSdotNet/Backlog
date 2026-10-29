using Backlog.Modules.Devbook.Abstractions;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// A Brain note's <c>tasks</c> field read both ways (local ADR 0026 §3,
/// <c>.devbook/domain/brain/requirements.md#bi-directional-linking</c>): forwards as
/// the tasks a note names, in reverse as the notes naming one task — from the Brain's
/// own database, never from the backlog. A value is the task's stored id, a GUID.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public class BrainTaskLinkTests : IDisposable
{
    private const string Ship = "6f9619ff-8b86-d011-b42d-00c04fc964ff";
    private const string Plan = "0c8a1b4e-2f6d-4a37-9c11-5e0b7d3f2a90";

    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "backlog-brain-task-links", Guid.NewGuid().ToString("N"));

    private string Brain => Path.Combine(_scratch, "Brain");

    private string Target => DevbookDatabaseLocation.PathFor(Path.Combine(_scratch, "devbook-cache", DevbookDatabaseLocation.DatabasesFolderName), Brain)!;

    public BrainTaskLinkTests()
    {
        Write("CONVENTIONS.md", "# Conventions\n\n## Areas\n\n| Area | Folder |\n|---|---|\n| Projects | projects |\n| Work | work |\n");
        Write("projects/roadmap.md", $$"""
            # Roadmap

            ```meta
            tasks: [{{Ship.ToUpperInvariant()}}, not-a-task, "{{{Plan}}}"]
            ```

            The plan.

            ## Later

            ```meta
            status: draft
            tasks: [{{Ship}}]
            ```

            Again.
            """);
        Write("work/standup.md", $$"""
            # A standup

            ```meta
            tasks: [{{Ship}}]
            ```

            Talked about it.
            """);
        Write("work/quiet.md", "# Quiet\n\nNames nothing.\n");
    }

    [Theory]
    [InlineData(Ship, Ship)]
    [InlineData("6F9619FF-8B86-D011-B42D-00C04FC964FF", Ship)]
    [InlineData("{6f9619ff-8b86-d011-b42d-00c04fc964ff}", Ship)]
    [InlineData("\"6f9619ff-8b86-d011-b42d-00c04fc964ff\"", Ship)]
    [InlineData("not-a-task", null)]
    [InlineData("00000000-0000-0000-0000-000000000000", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_task_id_is_a_guid_in_one_spelling(string? value, string? expected) =>
        Assert.Equal(expected, BrainTaskLink.Normalize(value));

    [Fact]
    public void A_note_names_its_tasks_in_its_order_each_once_and_keeps_what_is_no_id()
    {
        using var database = Build();

        var links = BrainTaskLinkReader.TasksOf(database, "projects/roadmap.md");

        Assert.Equal([Ship, null, Plan], links.Select(link => link.TaskId));
        Assert.Equal("not-a-task", links[1].Raw);
        Assert.Empty(BrainTaskLinkReader.TasksOf(database, "work/quiet.md"));
    }

    [Fact]
    public void A_task_lists_the_notes_naming_it_in_title_order_each_once()
    {
        using var database = Build();

        var byTask = BrainTaskLinkReader.NotesByTask(database);

        Assert.Equal([Plan, Ship], byTask.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["work/standup.md", "projects/roadmap.md"], byTask[Ship].Select(note => note.NotePath));
        Assert.Equal(["A standup", "Roadmap"], byTask[Ship].Select(note => note.Title));
        Assert.Equal(["projects/roadmap.md"], byTask[Plan].Select(note => note.NotePath));
    }

    [Fact]
    public async Task The_reader_asks_the_brain_s_own_database()
    {
        using (Build())
        {
        }

        var reader = new BrainTaskLinkReader(() => Brain, root => DevbookDatabaseLocation.PathFor(Path.Combine(_scratch, "devbook-cache", DevbookDatabaseLocation.DatabasesFolderName), root));

        Assert.Equal(2, (await reader.NotesByTaskAsync(TestContext.Current.CancellationToken))[Ship].Count);
        Assert.Equal(3, (await reader.TasksOfAsync("projects/roadmap.md", TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task A_brain_with_no_database_or_no_brain_links_no_task()
    {
        var missing = new BrainTaskLinkReader(() => Brain, _ => Path.Combine(_scratch, "missing", "devbook.db"));
        var none = new BrainTaskLinkReader(() => null);

        Assert.Empty(await missing.NotesByTaskAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await missing.TasksOfAsync("projects/roadmap.md", TestContext.Current.CancellationToken));
        Assert.Empty(await none.NotesByTaskAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await none.TasksOfAsync("projects/roadmap.md", TestContext.Current.CancellationToken));
    }

    private DevbookDatabase Build()
    {
        Assert.True(DevbookDatabaseBuilder.Build(Brain, Target, DevbookCorpusKind.Brain, TestContext.Current.CancellationToken));
        var database = DevbookDatabase.TryOpen(Target);
        Assert.NotNull(database);
        return database;
    }

    private void Write(string relativePath, string text)
    {
        var path = Path.Combine(Brain, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(_scratch)) Directory.Delete(_scratch, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }
}
