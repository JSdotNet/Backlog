using Backlog.Mobile.UI.Recordings;
using Backlog.Modules.Inbox.DomainModels;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The desktop transcribes only what the phone's recorder named (local ADR 0049 §1), and the
/// Inbox restates those names because it cannot reference the phone. Here both are in reach:
/// every name the recorder gives must read as a recording, so a wording change on the phone
/// cannot silently stop recordings being transcribed.
/// </summary>
public sealed class RecordingNamesAreInboxRecordingsTests
{
    public static TheoryData<string> RecorderNames() =>
    [
        RecordingNames.Interrupted,
        RecordingNames.Part(1, TimeSpan.FromSeconds(1)),
        RecordingNames.Part(1, TimeSpan.FromMinutes(47) + TimeSpan.FromSeconds(12)),
        RecordingNames.Part(2, TimeSpan.Zero),
        RecordingNames.Part(3, TimeSpan.FromHours(1) + TimeSpan.FromSeconds(3)),
    ];

    [Theory]
    [MemberData(nameof(RecorderNames))]
    public void Every_name_the_recorder_gives_is_a_recording_on_the_desktop(string name) =>
        Assert.True(InboxAttachment.Named(Guid.CreateVersion7(), name, "audio/aac", 1, new string('a', 64)).IsRecording);
}
