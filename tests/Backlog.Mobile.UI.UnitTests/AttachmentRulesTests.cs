using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.TalkNotes;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The phone takes a recording as a file of its own (local ADR 0048 §5,
/// .devbook/domain/inbox/requirements.md#recordings-on-a-note): AAC in ADTS or in MPEG-4 is
/// not refused, reads as "AAC audio", is no picture, and goes up as it was recorded.
/// </summary>
public sealed class AttachmentRulesTests
{
    [Theory]
    [InlineData("meeting.aac", "audio/aac")]
    [InlineData("meeting.m4a", "audio/mp4")]
    [InlineData("MEETING.AAC", "audio/aac")]
    public void An_audio_file_with_no_declared_type_is_typed_by_its_extension(string name, string expected)
    {
        Assert.Equal(expected, AttachmentRules.ContentTypeOf(name, null));
        Assert.Equal(expected, AttachmentRules.ContentTypeOf(name, "application/octet-stream"));
    }

    [Theory]
    [InlineData("meeting.aac", "audio/aac")]
    [InlineData("meeting.m4a", "audio/mp4")]
    [InlineData("meeting.m4a", "audio/x-m4a")]
    public void Recorded_audio_is_not_refused(string name, string contentType)
    {
        Assert.Null(AttachmentRules.RefusalOf(name, contentType, 24L * 1024 * 1024));
    }

    [Theory]
    [InlineData("audio/aac")]
    [InlineData("audio/mp4")]
    [InlineData("audio/x-m4a")]
    public void Recorded_audio_reads_as_AAC_audio_and_is_no_picture(string contentType)
    {
        Assert.Equal("AAC audio", AttachmentRules.Describe(contentType));
        Assert.False(AttachmentRules.IsPicture(contentType));
    }

    [Fact]
    public void Other_audio_is_still_refused_and_the_reason_names_audio()
    {
        var refusal = AttachmentRules.RefusalOf("song.mp3", "audio/mpeg", 1_000);

        Assert.Equal("Not a type cloud sync takes (.mp3). Pictures, audio, PDF, text and Office files only.", refusal);
    }

    [Fact]
    public async Task A_recording_goes_up_byte_for_byte_as_recorded()
    {
        var clock = new FakeTimeProvider();
        var files = new TalkNoteFiles(TestFolders.Create());
        using var outbox = new DeviceOutbox(new InMemoryDeviceStore(), [], clock);
        var composer = new TalkNoteComposer(files, outbox, clock);

        var recorded = AdtsStream.Of([200, 180, 220]);
        var path = Path.Combine(TestFolders.Create(), "part.aac");
        await File.WriteAllBytesAsync(path, recorded, TestContext.Current.CancellationToken);
        var draft = new DraftAttachment(Guid.CreateVersion7(), "Recording · 0:00", "audio/aac", recorded.Length, path);

        var metadata = await composer.PrepareAsync(draft, sendOriginals: false, TestContext.Current.CancellationToken);

        Assert.Equal("audio/aac", metadata.ContentType);
        Assert.Equal("Recording · 0:00", metadata.Name);
        Assert.Equal(recorded.Length, metadata.SizeBytes);
        Assert.Equal(recorded, File.ReadAllBytes(files.OutboxPath(draft.Id)));
        Assert.False(File.Exists(path));
    }
}
