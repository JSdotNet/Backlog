using Backlog.Mobile.UI.Tasks;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The feed folded into one row per task: the later write wins, a tombstone takes
/// the task away, the order pages arrive in does not change where the fold ends,
/// and a capture never becomes a row — it is the Inbox's.
/// </summary>
public sealed class TaskFoldTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 25);

    [Fact]
    public void A_later_update_replaces_the_row()
    {
        var id = Guid.CreateVersion7();
        var rows = new Dictionary<Guid, TaskViewRow>();

        TaskFold.Apply(rows, [Record(TestTasks.Task("Draft the talk", T0, id: id), 1)]);
        var changed = TaskFold.Apply(rows, [Record(TestTasks.Task("Rehearse the talk", T0.AddMinutes(5), id: id), 2)]);

        Assert.Single(changed);
        Assert.Equal("Rehearse the talk", Assert.Single(rows.Values).Task.Title);
    }

    [Fact]
    public void An_earlier_update_arriving_late_changes_nothing()
    {
        var id = Guid.CreateVersion7();
        var rows = new Dictionary<Guid, TaskViewRow>();

        TaskFold.Apply(rows, [Record(TestTasks.Task("Rehearse the talk", T0.AddMinutes(5), id: id), 2)]);
        var changed = TaskFold.Apply(rows, [Record(TestTasks.Task("Draft the talk", T0, id: id), 1)]);

        Assert.Empty(changed);
        Assert.Equal("Rehearse the talk", rows[id].Task.Title);
    }

    [Fact]
    public void A_tombstone_removes_the_task_from_My_Day()
    {
        var id = Guid.CreateVersion7();
        var rows = new Dictionary<Guid, TaskViewRow>();

        TaskFold.Apply(rows, [Record(TestTasks.Task("Book the train", T0, inMyDayOn: Today, id: id), 1)]);
        Assert.True(rows[id].IsInMyDay(Today));

        TaskFold.Apply(rows, [Record(TestTasks.Task("Book the train", T0.AddMinutes(1), inMyDayOn: Today, id: id, deletedAt: T0.AddMinutes(1)), 2)]);

        Assert.False(rows[id].IsInMyDay(Today));
        Assert.NotNull(rows[id].DeletedAt);
    }

    /// <summary>The replayed and reordered pages a phone that lost a response
    /// meets: every order the three writes can arrive in ends on the tombstone.</summary>
    [Fact]
    public void Out_of_order_pages_converge_on_the_same_rows()
    {
        var id = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();

        TaskChangeRecord[] records =
        [
            Record(TestTasks.Task("Book the train", T0, inMyDayOn: Today, id: id), 1),
            Record(TestTasks.Task("Book the train, first class", T0.AddMinutes(1), inMyDayOn: Today, id: id), 2),
            Record(TestTasks.Task("Book the train", T0.AddMinutes(2), id: id, deletedAt: T0.AddMinutes(2)), 3),
            Record(TestTasks.Task("Pack", T0, inMyDayOn: Today, id: other), 4),
        ];

        var endings = Permutations(records)
            .Select(order =>
            {
                var rows = new Dictionary<Guid, TaskViewRow>();

                // One record per page, so each arrives as its own late page.
                foreach (var record in order) TaskFold.Apply(rows, [record]);

                return string.Join("|", rows.Values.OrderBy(row => row.Id).Select(row => $"{row.Id}:{row.Task.Title}:{row.DeletedAt}"));
            })
            .Distinct()
            .ToList();

        var ending = Assert.Single(endings);
        Assert.Contains($"{id}:Book the train:{T0.AddMinutes(2)}", ending, StringComparison.Ordinal);
    }

    [Fact]
    public void A_copy_the_phone_wrote_itself_is_replaced_by_the_replicas_copy_of_the_same_write()
    {
        var change = TestTasks.Task("Call the venue", T0, inMyDayOn: Today);
        var rows = new Dictionary<Guid, TaskViewRow>
        {
            [change.Id] = new(change.Id, change.UpdatedAt, null, ServerTimestamp: 0, change.Task)
        };

        var changed = TaskFold.Apply(rows, [Record(change, 7)]);

        Assert.Single(changed);
        Assert.Equal(7, rows[change.Id].ServerTimestamp);
    }

    [Fact]
    public void A_capture_is_never_a_row()
    {
        var rows = new Dictionary<Guid, TaskViewRow>();

        var changed = TaskFold.Apply(rows, [Record(TestTasks.Task("Idea from the hallway", T0, inMyDayOn: Today, type: "capture"), 1)]);

        Assert.Empty(changed);
        Assert.Empty(rows);
    }

    private static TaskChangeRecord Record(TaskChange change, long serverTimestamp) =>
        new(change, Guid.NewGuid(), serverTimestamp);

    private static IEnumerable<T[]> Permutations<T>(T[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }

        for (var i = 0; i < items.Length; i++)
        {
            var rest = items.Where((_, index) => index != i).ToArray();
            foreach (var tail in Permutations(rest)) yield return [items[i], .. tail];
        }
    }
}
