using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.Tasks;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The task feed's note documents folded into one row per note, the way tasks are
/// folded into <c>task_view</c> (.devbook/arc42/06-runtime-view.md#mobile-note-sync):
/// the later stamp wins, a tombstone hides the note, page order does not matter,
/// and the two folds never take each other's documents.
/// </summary>
public sealed class NoteFoldTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Only_note_documents_become_rows()
    {
        var rows = new Dictionary<Guid, NoteViewRow>();

        var changed = NoteFold.Apply(rows,
        [
            Record(TestTasks.Task("A task", T0), 1),
            Record(TestTasks.Task("A capture", T0, type: TaskFold.CaptureType), 2),
            Record(Note("Meeting notes", T0), 3),
        ]);

        Assert.Equal("Meeting notes", Assert.Single(changed).Title);
        Assert.Single(rows);
    }

    [Fact]
    public void The_task_fold_never_lists_a_note()
    {
        var rows = new Dictionary<Guid, TaskViewRow>();

        var changed = TaskFold.Apply(rows, [Record(Note("Meeting notes", T0), 1)]);

        Assert.Empty(changed);
        Assert.Empty(rows);
    }

    [Fact]
    public void A_later_edit_replaces_the_row_and_an_earlier_one_arriving_late_changes_nothing()
    {
        var id = Guid.CreateVersion7();
        var rows = new Dictionary<Guid, NoteViewRow>();

        NoteFold.Apply(rows, [Record(Note("Edited on the desktop", T0.AddMinutes(5), id), 2)]);
        var late = NoteFold.Apply(rows, [Record(Note("Written on the phone", T0, id), 1)]);

        Assert.Empty(late);
        Assert.Equal("Edited on the desktop", rows[id].Title);
    }

    [Fact]
    public void A_tombstone_hides_the_note_and_an_older_live_copy_cannot_bring_it_back()
    {
        var id = Guid.CreateVersion7();
        var rows = new Dictionary<Guid, NoteViewRow>();

        NoteFold.Apply(rows, [Record(Note("Archived on the desktop", T0.AddMinutes(10), id, deletedAt: T0.AddMinutes(10)), 3)]);
        var replay = NoteFold.Apply(rows, [Record(Note("Still live", T0, id), 1)]);

        Assert.Empty(replay);
        Assert.True(rows[id].IsHidden);
    }

    [Fact]
    public void The_fold_ends_on_the_same_row_whatever_order_the_pages_arrive_in()
    {
        var id = Guid.CreateVersion7();
        var first = Record(Note("First", T0, id), 1);
        var second = Record(Note("Second", T0, id), 2);
        var tombstone = Record(Note("Second", T0, id, deletedAt: T0), 3);

        var forward = new Dictionary<Guid, NoteViewRow>();
        NoteFold.Apply(forward, [first, second, tombstone]);

        var backward = new Dictionary<Guid, NoteViewRow>();
        NoteFold.Apply(backward, [tombstone, second, first]);

        Assert.Equal(forward[id], backward[id]);
        Assert.True(forward[id].IsHidden);
    }

    [Fact]
    public void An_equal_stamp_goes_to_the_higher_server_timestamp()
    {
        var id = Guid.CreateVersion7();
        var rows = new Dictionary<Guid, NoteViewRow>();

        NoteFold.Apply(rows, [Record(Note("Server second", T0, id), 9)]);
        NoteFold.Apply(rows, [Record(Note("Server first", T0, id), 4)]);

        Assert.Equal("Server second", rows[id].Title);
    }

    internal static TaskChange Note(
        string title,
        DateTimeOffset updatedAt,
        Guid? id = null,
        DateTimeOffset? deletedAt = null,
        string body = "") =>
        TestTasks.Task(title, updatedAt, type: NoteFold.NoteType, id: id, deletedAt: deletedAt, contentMd: body, status: "draft");

    private static TaskChangeRecord Record(TaskChange change, long serverTimestamp) =>
        new(change, Guid.NewGuid(), serverTimestamp);
}
