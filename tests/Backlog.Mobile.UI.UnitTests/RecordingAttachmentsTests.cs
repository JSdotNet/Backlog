using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.Recordings;
using Backlog.Mobile.UI.TalkNotes;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The parts of a recording whose note is deleted elsewhere in the moment between the look and
/// the write (.devbook/domain/inbox/requirements.md#recordings-on-a-note): they go to a new note,
/// and the per-note cap is the new note's, not the deleted one's.
/// </summary>
public sealed class RecordingAttachmentsTests : IDisposable
{
    private readonly RecordingFixture _fixture = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_note_hidden_before_the_write_gets_its_parts_on_a_new_note_under_the_new_note_cap()
    {
        // One place left on the note: the cap holds back the second part on the first pass.
        AttachmentMetadata[] held = [.. Enumerable.Range(0, AttachmentRules.MaximumPerNote - 1)
            .Select(index => new AttachmentMetadata(Guid.NewGuid(), $"file {index}", "text/plain", 1, "00"))];
        var note = await _fixture.Notes.CreateAsync("Standup", "notes", held, Cancellation);
        var bytes = ScriptedMeetingRecorder.AdtsPart(TimeSpan.FromSeconds(30)).Bytes;
        var first = _fixture.LeaveRecording(note.Id, 1, ".aac", bytes);
        var second = _fixture.LeaveRecording(note.Id, 2, ".aac", bytes);

        var outcome = await RecordingAttachments.AttachAsync(
            _fixture.Composer,
            _fixture.Files,
            _fixture.Notes,
            note.Id,
            [
                new RecordingToAttach(first.AttachmentId, "Recording · 0:30", "audio/aac", first.Path),
                new RecordingToAttach(second.AttachmentId, "Recording · part 2 · 0:30", "audio/aac", second.Path),
            ],
            Cancellation,
            newNoteTitle: "Recording · 10 Nov 2026",
            attach: async (id, files, cancellationToken) =>
            {
                // The desktop archives the note while the parts were being readied.
                var at = _fixture.Clock.GetUtcNow().AddMinutes(5);
                _fixture.Service.State = InboxServiceState.Answering;
                _fixture.Service.Append(NoteFoldTests.Note("Standup", at, id, deletedAt: at));
                await _fixture.Notes.PullAsync(cancellationToken);
                return await _fixture.Notes.AttachAsync(id, files, cancellationToken);
            });

        var created = Assert.IsType<NoteViewRow>(outcome.NewNote);
        Assert.Equal(
            ["Recording · 0:30", "Recording · part 2 · 0:30"],
            _fixture.Notes.Find(created.Id)!.Attachments.Select(file => file.Name));
        Assert.Null(outcome.Error);
        Assert.Empty(Directory.GetFiles(_fixture.Files.Folder));
    }
}
