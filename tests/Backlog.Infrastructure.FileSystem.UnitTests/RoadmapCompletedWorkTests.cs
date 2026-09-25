using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// What a measured pace is counted from: the backlog entries finished on or after a
/// day, with the estimate each carried. An entry with no estimate, or one of zero,
/// has nothing to add to a pace and is left out.
/// </summary>
public class RoadmapCompletedWorkTests
{
    private static readonly DateOnly Since = new(2026, 9, 1);

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

        var completed = RoadmapCompletedWork.Completed(backlog, Since);

        Assert.Equal(
            [new CompletedEffortDto(Since, 3), new CompletedEffortDto(Since.AddDays(4), 2)],
            completed);
    }

    private static TaskItemDto Entry(string title, DateOnly? completedOn, int? effort) =>
        new(Guid.NewGuid(), title, string.Empty, EntryType.Task, Priority.Medium,
            completedOn is null ? EntryStatus.Ready : EntryStatus.Done,
            null, [], 0, 0, 0, [],
            CompletedOn: completedOn,
            Effort: effort);
}
