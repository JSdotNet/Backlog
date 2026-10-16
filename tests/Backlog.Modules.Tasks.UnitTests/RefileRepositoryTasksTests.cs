using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.RefileRepositoryTasks;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// The open tasks of a repository being archived, moved to another repository or
/// dropped (local ADR 0029; requirements "Archiving a repository"). Only open work
/// moves — Draft, Ready and In progress — because finished work is history and
/// still counts toward pace under the repository it was done in. Moving replaces
/// the one filing and nothing else: an issue link still points into the repository
/// the issue lives in. Dropping sets the Task Status <c>archived</c>, deletes
/// nothing and keeps the filing. Keeping the tasks is no command at all.
/// </summary>
public sealed class RefileRepositoryTasksTests
{
    private const string Archived = "JSdotNet/Budgetbeheer";
    private const string Other = "JSdotNet/Backlog";
    private const string Third = "JSdotNet/Docs";

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(EntryStatus.Draft)]
    [InlineData(EntryStatus.Ready)]
    [InlineData(EntryStatus.InProgress)]
    public async Task Moving_files_an_open_task_under_the_other_repository(EntryStatus status)
    {
        var store = StoreWith(Entry([Archived], status));

        var changed = await Move(store, Other);

        Assert.Equal(1, changed);
        var entry = store.Entries.Values.Single();
        Assert.Equal([Other], entry.RepoIds);
        Assert.Equal(status, entry.Status);
    }

    /// <summary>The issue still lives in the archived repository, so its link
    /// stays there; only where the task is filed moves.</summary>
    [Fact]
    public async Task Moving_keeps_the_issue_links_where_the_issues_are()
    {
        var entry = Entry([Archived]);
        entry.AddProjectionRef(new ProjectionRef(Archived, "42", "issue"));
        var store = StoreWith(entry);

        await Move(store, Other);

        Assert.Equal([new ProjectionRef(Archived, "42", "issue")], store.Entries.Values.Single().ProjectionRefs);
    }

    [Fact]
    public async Task A_task_filed_under_two_repositories_has_only_the_archived_one_replaced()
    {
        var store = StoreWith(Entry([Third, Archived]));

        await Move(store, Other);

        Assert.Equal([Third, Other], store.Entries.Values.Single().RepoIds);
    }

    /// <summary>A task already filed under the repository it moves to names it
    /// once, not twice.</summary>
    [Fact]
    public async Task A_task_already_filed_under_the_other_repository_names_it_once()
    {
        var store = StoreWith(Entry([Other, Archived]));

        await Move(store, Other);

        Assert.Equal([Other], store.Entries.Values.Single().RepoIds);
    }

    [Fact]
    public async Task The_archived_repository_is_matched_without_regard_to_case()
    {
        var store = StoreWith(Entry(["jsdotnet/budgetbeheer"]));

        var changed = await Move(store, Other);

        Assert.Equal(1, changed);
        Assert.Equal([Other], store.Entries.Values.Single().RepoIds);
    }

    [Theory]
    [InlineData(EntryStatus.Draft)]
    [InlineData(EntryStatus.Ready)]
    [InlineData(EntryStatus.InProgress)]
    public async Task Dropping_archives_an_open_task_and_keeps_its_filing(EntryStatus status)
    {
        var store = StoreWith(Entry([Archived], status));

        var changed = await Drop(store);

        Assert.Equal(1, changed);
        var entry = store.Entries.Values.Single();
        Assert.Equal(EntryStatus.Archived, entry.Status);
        Assert.Equal([Archived], entry.RepoIds);
        Assert.Null(entry.DeletedAt);
    }

    /// <summary>Finished work is history: it stays Done or Archived under the
    /// repository it was done in, whichever choice was made.</summary>
    [Theory]
    [InlineData(EntryStatus.Done, true)]
    [InlineData(EntryStatus.Done, false)]
    [InlineData(EntryStatus.Archived, true)]
    [InlineData(EntryStatus.Archived, false)]
    public async Task Finished_work_is_left_alone(EntryStatus status, bool drop)
    {
        var store = StoreWith(Entry([Archived], status));

        var changed = drop ? await Drop(store) : await Move(store, Other);

        Assert.Equal(0, changed);
        Assert.Equal(0, store.Writes);
        var entry = store.Entries.Values.Single();
        Assert.Equal(status, entry.Status);
        Assert.Equal([Archived], entry.RepoIds);
    }

    [Fact]
    public async Task A_task_filed_elsewhere_is_not_written()
    {
        var store = StoreWith(Entry([Other]), Entry([]));

        Assert.Equal(0, await Drop(store));
        Assert.Equal(0, await Move(store, Third));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task A_deleted_task_is_left_alone()
    {
        var deleted = Entry([Archived]);
        deleted.MarkDeleted();
        var store = StoreWith(deleted);

        Assert.Equal(0, await Drop(store));
        Assert.Equal(0, await Move(store, Other));
        Assert.Equal([Archived], store.Entries.Values.Single().RepoIds);
        Assert.Equal(EntryStatus.Ready, store.Entries.Values.Single().Status);
    }

    [Fact]
    public async Task Nothing_is_deleted()
    {
        var store = StoreWith(Entry([Archived]), Entry([Archived], EntryStatus.InProgress), Entry([Archived], EntryStatus.Done));

        await Drop(store);

        Assert.Equal(3, store.Entries.Count);
        Assert.All(store.Entries.Values, entry => Assert.Null(entry.DeletedAt));
    }

    [Fact]
    public async Task A_second_move_writes_nothing()
    {
        var store = StoreWith(Entry([Archived]));
        Assert.Equal(1, await Move(store, Other));
        store.Writes = 0;

        Assert.Equal(0, await Move(store, Other));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task A_second_drop_writes_nothing()
    {
        var store = StoreWith(Entry([Archived]));
        Assert.Equal(1, await Drop(store));
        store.Writes = 0;

        Assert.Equal(0, await Drop(store));
        Assert.Equal(0, store.Writes);
    }

    [Theory]
    [InlineData(" ", Other, false)]
    [InlineData(Archived, null, false)]
    [InlineData(Archived, " ", false)]
    [InlineData(Archived, Other, true)]
    [InlineData(Archived, "jsdotnet/budgetbeheer", false)]
    public async Task A_request_that_is_not_one_move_or_one_drop_is_refused(string repositoryId, string? moveToId, bool drop)
    {
        var store = StoreWith(Entry([Archived]));

        var result = await Handler(store).Handle(new RefileRepositoryTasksCommand(repositoryId, moveToId, drop), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(RefileRepositoryTasksCommand.Invalid, result.Error);
        Assert.Equal(0, store.Writes);
    }

    private static Task<int> Move(InMemoryTaskRepository store, string moveToId) =>
        Run(store, new RefileRepositoryTasksCommand(Archived, moveToId, Drop: false));

    private static Task<int> Drop(InMemoryTaskRepository store) =>
        Run(store, new RefileRepositoryTasksCommand(Archived, MoveToId: null, Drop: true));

    private static async Task<int> Run(InMemoryTaskRepository store, RefileRepositoryTasksCommand command)
    {
        var result = await Handler(store).Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    private static RefileRepositoryTasksCommandHandler Handler(InMemoryTaskRepository store) =>
        new(store, new LinkedTasks.ManualTimeProvider(Now));

    private static TaskItem Entry(IEnumerable<string> repoIds, EntryStatus status = EntryStatus.Ready)
    {
        var entry = new TaskItem("Ship it", string.Empty, EntryType.Task);
        entry.SetRepoIds(repoIds);
        entry.SetStatus(status, DateOnly.FromDateTime(Now.UtcDateTime));
        return entry;
    }

    private static InMemoryTaskRepository StoreWith(params TaskItem[] entries) => new(entries);
}
