using Backlog.Mobile.UI.Recordings;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// How much room a meeting recording needs (.devbook/domain/capture/features.md#meeting-recording):
/// a recording stops at 200 MB free, and a start needs that floor plus two parts' worth on top,
/// 248 MB, so the first part and its copy on Stop both fit.
/// </summary>
public sealed class RecordingSpaceTests
{
    private const long Megabyte = 1024 * 1024;

    [Theory]
    [InlineData(248, true)]
    [InlineData(247, false)]
    [InlineData(0, false)]
    public void A_start_needs_the_floor_and_two_parts_free(long megabytes, bool expected)
    {
        Assert.Equal(expected, RecordingSpace.CanStart(megabytes * Megabyte));
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(199, true)]
    [InlineData(201, false)]
    public void A_recording_stops_at_or_below_200_MB_free(long megabytes, bool expected)
    {
        Assert.Equal(expected, RecordingSpace.MustStop(megabytes * Megabyte));
    }

    [Fact]
    public void A_refused_start_says_what_it_needs_and_what_there_is()
    {
        Assert.Equal(
            "There is not enough storage on this phone to record: it needs 248 MB free and has 120 MB.",
            RecordingSpace.Refusal(120 * Megabyte));
    }

    [Fact]
    public void A_recording_stopped_for_space_says_why_and_where_it_went()
    {
        Assert.Equal(
            "The recording stopped because the phone has less than 200 MB free. What was recorded is on the note.",
            RecordingSpace.Stopped);
    }

    [Fact]
    public void A_recording_stopped_for_space_whose_note_was_deleted_names_the_new_note()
    {
        Assert.Equal(
            "The recording stopped because the phone has less than 200 MB free. What was recorded is on a new note, Recording · 10 Nov 2026.",
            RecordingSpace.StoppedOnNewNote("Recording · 10 Nov 2026"));
    }
}
