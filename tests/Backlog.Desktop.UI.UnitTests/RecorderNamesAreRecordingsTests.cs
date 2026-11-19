using Backlog.Mobile.UI.Recordings;
using Backlog.Modules.Inbox.DomainModels;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The desktop knows a recording the app made by the name the phone's recorder gives it
/// (local ADR 0049 §1). The Inbox module restates the recorder's rule because it cannot
/// reference the phone's project; this is the one place both are in reach, so a change to
/// <see cref="RecordingNames"/> that the Inbox's rule does not follow fails here.
/// </summary>
public sealed class RecorderNamesAreRecordingsTests
{
    public static TheoryData<int, double> Parts => new()
    {
        { 1, 0 },
        { 1, 59.9 },
        { 1, 754 },
        { 1, 3599 },
        { 1, 3600 },
        { 1, 3723 },
        { 1, 36_000 },
        { 2, 30 },
        { 3, 3723 },
        { 12, 600 },
    };

    [Theory]
    [MemberData(nameof(Parts))]
    public void Every_part_name_the_recorder_gives_is_a_recording(int part, double seconds) =>
        Assert.True(IsRecording(RecordingNames.Part(part, TimeSpan.FromSeconds(seconds))));

    [Fact]
    public void The_interrupted_name_is_a_recording() =>
        Assert.True(IsRecording(RecordingNames.Interrupted));

    [Fact]
    public void A_note_title_the_recorder_gives_is_not_a_file_it_names() =>
        Assert.False(IsRecording(RecordingNames.NoteTitle(new DateOnly(2026, 11, 10))));

    [Theory]
    [InlineData("standup.m4a")]
    [InlineData("Recording")]
    [InlineData("Recording · ")]
    [InlineData("Recording · notes.m4a")]
    [InlineData("Recording · 12:3")]
    [InlineData("Recording · 12:60")]
    [InlineData("Recording · part 1 · 0:30")]
    [InlineData("Recording · part 2")]
    [InlineData("Recording · 1:2:03")]
    [InlineData("Recording (interrupted) 2")]
    public void A_name_the_recorder_never_gives_is_not_a_recording(string name) =>
        Assert.False(IsRecording(name));

    private static bool IsRecording(string name) =>
        InboxAttachment.Named(Guid.CreateVersion7(), name, "audio/aac", 1, new string('a', 64)).IsRecording;
}
