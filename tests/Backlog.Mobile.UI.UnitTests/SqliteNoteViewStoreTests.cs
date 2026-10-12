using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.Tasks;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary><c>note_view</c> survives the app closing: rows, tombstones and its own
/// cursor come back from the file as they went in, beside <c>task_view</c> and the
/// outbox in the same file, and neither cursor moves the other.</summary>
public sealed class SqliteNoteViewStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"note-view-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public async Task Rows_and_the_cursor_survive_a_new_store_over_the_same_file()
    {
        var live = NoteFoldTests.Note("Standup", T0, body: "- shipped");
        var gone = NoteFoldTests.Note("Archived", T0, deletedAt: T0.AddMinutes(1));

        await new SqliteNoteViewStore(_path).SaveAsync(
            [
                new NoteViewRow(live.Id, live.UpdatedAt, null, 3, live.Task),
                new NoteViewRow(gone.Id, gone.UpdatedAt, gone.DeletedAt, 4, gone.Task)
            ],
            "pos:7",
            TestContext.Current.CancellationToken);

        // The other tables in the same file do not get in the way, and the task
        // view's cursor is its own.
        _ = new Outbox.SqliteDeviceStore(_path);
        var tasks = new SqliteTaskViewStore(_path);
        var reopened = new SqliteNoteViewStore(_path);

        var rows = reopened.ReadRows().ToDictionary(row => row.Id);
        Assert.Equal("pos:7", reopened.ReadCursor());
        Assert.Null(tasks.ReadCursor());
        Assert.Equal("- shipped", rows[live.Id].Body);
        Assert.Equal(3, rows[live.Id].ServerTimestamp);
        Assert.Equal(T0, rows[live.Id].UpdatedAt);
        Assert.True(rows[gone.Id].IsHidden);

        await reopened.ResetCursorAsync(TestContext.Current.CancellationToken);
        Assert.Null(reopened.ReadCursor());
        Assert.Equal(2, reopened.ReadRows().Count);
    }
}
