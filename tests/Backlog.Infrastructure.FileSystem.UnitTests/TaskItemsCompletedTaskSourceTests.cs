using Backlog.Infrastructure.FileSystem.Dashboard;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// What the dashboard's tasks section is fed: the backlog's ticked-off tasks, with their
/// repository ids turned into the aliases the dashboard's scope matches against.
/// </summary>
public class TaskItemsCompletedTaskSourceTests
{
    private static readonly DateOnly Since = new(2026, 8, 24);

    [Fact]
    public void Only_tasks_ticked_off_on_or_after_the_horizon_cross()
    {
        var completed = TaskItemsCompletedTaskSource.Completed(
            [
                Entry(new DateOnly(2026, 8, 24), 3),
                Entry(new DateOnly(2026, 8, 23), 5),
                Entry(completedOn: null, 8)
            ],
            Since,
            new Directory());

        var only = Assert.Single(completed);
        Assert.Equal(new DateOnly(2026, 8, 24), only.CompletedOn);
        Assert.Equal(3, only.Effort);
    }

    [Fact]
    public void Repository_ids_resolve_to_aliases_and_an_unknown_id_crosses_as_itself()
    {
        var completed = TaskItemsCompletedTaskSource.Completed(
            [Entry(new DateOnly(2026, 9, 1), null, "JSdotNet/Backlog", "jsdotnet/backlog", "someone/else")],
            Since,
            new Directory(new TasksRepositoryRef("backlog", "JSdotNet", "Backlog")));

        var only = Assert.Single(completed);
        Assert.Null(only.Effort);
        Assert.Equal(["backlog", "someone/else"], only.RepositoryAliases);
    }

    private static TaskItemDto Entry(DateOnly? completedOn, int? effort, params string[] repoIds) =>
        new(
            Guid.NewGuid(),
            "Step",
            Body: "",
            EntryType.Task,
            Priority.Medium,
            EntryStatus.Ready,
            Area: null,
            Tags: [],
            Order: 0,
            TotalSubItems: 0,
            CompletedSubItems: 0,
            Projections: [],
            CompletedOn: completedOn,
            Effort: effort,
            RepoIds: repoIds);

    private sealed class Directory(params TasksRepositoryRef[] repositories) : IRepositoryDirectory
    {
        public IReadOnlyList<TasksRepositoryRef> Repositories => repositories;

        public TasksRepositoryRef? Resolve(string name) =>
            repositories.FirstOrDefault(repository =>
                string.Equals(repository.Id, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(repository.Alias, name, StringComparison.Ordinal));

        public TasksRepositoryRef Register(string name) => throw new NotSupportedException();
    }
}
