using Backlog.Mobile.UI.Recordings;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// What a recording is called on its note (.devbook/domain/capture/features.md#meeting-recording):
/// "Recording · 12:34", a later part numbered, and a file a killed app left behind marked
/// interrupted.
/// </summary>
public sealed class RecordingNamesTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(59.9, "0:59")]
    [InlineData(754, "12:34")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3723, "1:02:03")]
    public void A_duration_reads_minutes_and_seconds_and_hours_from_an_hour(double seconds, string expected)
    {
        Assert.Equal(expected, RecordingNames.Duration(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void The_first_part_is_named_by_its_duration()
    {
        Assert.Equal("Recording · 12:34", RecordingNames.Part(1, TimeSpan.FromSeconds(754)));
    }

    [Fact]
    public void A_later_part_carries_its_number()
    {
        Assert.Equal("Recording · part 2 · 1:02:03", RecordingNames.Part(2, TimeSpan.FromSeconds(3723)));
    }

    [Fact]
    public void A_file_left_by_a_killed_app_is_marked_interrupted()
    {
        Assert.Equal("Recording (interrupted)", RecordingNames.Interrupted);
    }

    [Fact]
    public void A_note_made_for_a_recording_is_titled_by_its_date_as_a_photo_note_is()
    {
        Assert.Equal("Recording · 10 Nov 2026", RecordingNames.NoteTitle(new DateOnly(2026, 11, 10)));
    }
}
