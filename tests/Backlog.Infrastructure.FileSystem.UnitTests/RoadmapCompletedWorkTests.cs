using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// What a measured pace is counted from: the backlog entries finished on or after a
/// day, with the estimate each carried and the repositories it was filed under. An
/// entry with no estimate, or one of zero, has nothing to add to a pace and is left
/// out.
/// </summary>
public class RoadmapCompletedWorkTests
{
    private static readonly DateOnly Since = new(2026, 9, 1);

    private static readonly TasksRepositoryRef[] Configured =
    [
        new("backlog", "JSdotNet", "Backlog"),
        new("site", "JSdotNet", "Site")
    ];

    [Fact]
    public void Only_estimated_work_finished_since_the_day_is_counted()
    {
        TaskItemDto[] backlog =
        [
            Entry("before", completedOn: Since.AddDays(-1), effort: 5),
            Entry("on the day", completedOn: Since, effort: 3),
            Entry("after", completedOn: Since.AddDays(4), effort: 2),
            Entry("no estimate", completedOn: Since.AddDays(1), effort: null),
            Entry("estimated at nothing", completedOn: Since.AddDays(1), effort: 0),
            Entry("still open", completedOn: null, effort: 8)
        ];

        var completed = RoadmapCompletedWork.Completed(backlog, Since, Configured);

        Assert.Equal(
            [(Since, 3), (Since.AddDays(4), 2)],
            completed.Select(entry => (entry.CompletedOn, entry.Effort)));
        Assert.All(completed, entry => Assert.Empty(entry.RepositoryAliases));
    }

    /// <summary>A task stores the <c>owner/name</c> id Import resolved, and a pace is
    /// kept under the alias — so each id is turned back into one, matched without
    /// regard to case as GitHub matches it. An id nobody configured is kept as written.</summary>
    [Fact]
    public void Each_entry_carries_the_aliases_of_the_repositories_it_was_filed_under()
    {
        TaskItemDto[] backlog =
        [
            Entry("one", Since, 3, "JSdotNet/Backlog"),
            Entry("two", Since, 5, "jsdotnet/backlog", "JSdotNet/Site", "JSdotNet/Backlog"),
            Entry("unknown", Since, 2, "someone/else")
        ];

        var completed = RoadmapCompletedWork.Completed(backlog, Since, Configured);

        Assert.Equal(["backlog"], completed[0].RepositoryAliases);
        Assert.Equal(["backlog", "site"], completed[1].RepositoryAliases);
        Assert.Equal(["someone/else"], completed[2].RepositoryAliases);
    }

    [Fact]
    public void The_configured_repositories_are_offered_by_alias()
    {
        var work = new RoadmapCompletedWork(
            () => throw new InvalidOperationException("The backlog is not read for this."),
            () => new FixedDirectory(Configured));

        Assert.Equal(["backlog", "site"], work.Repositories);
    }

    private static TaskItemDto Entry(string title, DateOnly? completedOn, int? effort, params string[] repoIds) =>
        new(Guid.NewGuid(), title, string.Empty, EntryType.Task, Priority.Medium,
            completedOn is null ? EntryStatus.Ready : EntryStatus.Done,
            null, [], 0, 0, 0, [],
            CompletedOn: completedOn,
            Effort: effort)
        {
            RepoIds = repoIds
        };

    private sealed class FixedDirectory(IReadOnlyList<TasksRepositoryRef> repositories) : IRepositoryDirectory
    {
        public IReadOnlyList<TasksRepositoryRef> Repositories => repositories;

        public TasksRepositoryRef? Resolve(string name) => null;

        public TasksRepositoryRef Register(string name) => new(name, name, name);
    }
}
