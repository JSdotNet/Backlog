using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.Sync;
using Backlog.Infrastructure.Sync.Sessions;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The footer's phrase, read without rendering. What it says in each state is
/// fixed here so the band and its window cannot drift apart on a word, and so
/// the folding of two loops into one reading is asserted on its own rather than
/// through a cycle.
/// </summary>
public sealed class SyncActivityPresentationTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Nothing_to_say_before_the_first_cycle()
    {
        var state = new SyncFooterState(Syncing: false, Sent: 0, Received: 0, At: null, Error: null);

        Assert.False(state.HasSomethingToShow);
        Assert.Null(SyncActivityPresentation.Label(state));
    }

    [Fact]
    public void A_cycle_in_flight_says_so_before_anything_else()
    {
        var state = new SyncFooterState(Syncing: true, Sent: 3, Received: 2, At: Noon, Error: "stale");

        Assert.Equal("Syncing…", SyncActivityPresentation.Label(state));
        Assert.StartsWith("Syncing with the cloud.", SyncActivityPresentation.ControlLabel(state), StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_cycle_outranks_the_last_good_one()
    {
        var state = new SyncFooterState(Syncing: false, Sent: 3, Received: 2, At: Noon, Error: "The sync service could not be reached.");

        Assert.Equal("Sync failed", SyncActivityPresentation.Label(state));
        Assert.Contains("could not be reached", SyncActivityPresentation.ControlLabel(state), StringComparison.Ordinal);
    }

    /// <summary>Arrows on the band, words in the accessible name: the direction
    /// is the point, and it must not be carried by a glyph alone.</summary>
    [Fact]
    public void A_completed_cycle_shows_both_directions_and_the_clock()
    {
        var state = new SyncFooterState(Syncing: false, Sent: 3, Received: 12, At: Noon, Error: null);

        var label = SyncActivityPresentation.Label(state);

        Assert.NotNull(label);
        Assert.StartsWith("Synced ↑3 ↓12 · ", label, StringComparison.Ordinal);
        Assert.EndsWith(SyncActivityPresentation.Clock(Noon), label, StringComparison.Ordinal);

        var spoken = SyncActivityPresentation.ControlLabel(state);
        Assert.Contains("3 sent, 12 received", spoken, StringComparison.Ordinal);
        Assert.EndsWith("Open sync activity", spoken, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two loops, one phrase. Sent adds up across both; received is what was
    /// applied, never what was pulled — a device's own echo comes back every
    /// cycle and changes nothing. The clock is the later of the two.
    /// </summary>
    [Fact]
    public void Two_loops_fold_into_one_reading()
    {
        var tasks = new TaskSyncSummary(Pushed: 2, Pulled: 40, Applied: 1, Skipped: 0, At: Noon);
        var sessions = new SessionSyncSummary(Pushed: 1, Pulled: 5, Applied: 4, At: Noon.AddMinutes(3));

        Assert.False(SyncFooterState.From(null, null).HasSomethingToShow);

        var folded = SyncFooterState.From(syncing: false, tasks, taskError: null, sessions, sessionError: null);

        Assert.Equal(3, folded.Sent);
        Assert.Equal(5, folded.Received);
        Assert.Equal(Noon.AddMinutes(3), folded.At);
        Assert.Null(folded.Error);
        Assert.True(folded.HasSomethingToShow);
    }

    [Fact]
    public void One_loop_alone_is_enough_for_a_reading_and_either_loops_error_is_the_bands()
    {
        var sessions = new SessionSyncSummary(Pushed: 1, Pulled: 5, Applied: 4, At: Noon);

        var alone = SyncFooterState.From(syncing: false, tasks: null, taskError: null, sessions, sessionError: null);
        Assert.Equal(1, alone.Sent);
        Assert.Equal(4, alone.Received);
        Assert.Equal(Noon, alone.At);

        var failing = SyncFooterState.From(syncing: false, tasks: null, taskError: null, sessions, sessionError: "Offline");
        Assert.Equal("Offline", failing.Error);
        Assert.Equal("Sync failed", SyncActivityPresentation.Label(failing));
    }

    [Fact]
    public void The_window_lines_say_what_was_pulled_as_well_as_what_was_kept()
    {
        var tasks = new TaskSyncSummary(Pushed: 2, Pulled: 40, Applied: 1, Skipped: 1, At: Noon);
        var sessions = new SessionSyncSummary(Pushed: 1, Pulled: 5, Applied: 4, At: Noon);

        Assert.Equal(
            $"Tasks: sent 2, received 1 of 40 pulled, 1 unreadable by this build · {SyncActivityPresentation.Clock(Noon)}",
            SyncActivityPresentation.TaskLine(tasks));
        Assert.Equal(
            $"Sessions: sent 1, received 4 of 5 pulled · {SyncActivityPresentation.Clock(Noon)}",
            SyncActivityPresentation.SessionLine(sessions));
        Assert.Null(SyncActivityPresentation.TaskLine(null));
    }

    [Fact]
    public void Directions_and_kinds_read_as_words()
    {
        Assert.Equal("↑ Sent", SyncActivityPresentation.Direction(SyncDirection.Sent));
        Assert.Equal("↓ Received", SyncActivityPresentation.Direction(SyncDirection.Received));
        Assert.Equal("Capture", SyncActivityPresentation.Kind(SyncItemKind.Capture));
    }
}
