using Backlog.Modules.Events.DomainModels;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// The rule of <c>.devbook/domain/events/domain.invariants.md#attachment-request</c>: a request
/// names exactly one knowledge event and one Inbox note.
/// </summary>
public sealed class AttachmentRequestInvariantTests
{
    private static readonly Guid Request = Guid.Parse("0c3a5e7f-1111-4b2c-9d8e-000000000001");
    private static readonly Guid Event = Guid.Parse("0c3a5e7f-2222-4b2c-9d8e-000000000002");
    private static readonly Guid Note = Guid.Parse("0c3a5e7f-3333-4b2c-9d8e-000000000003");

    [Fact]
    public void A_request_without_an_event_is_refused()
    {
        var result = AttachmentRequest.Create(Request, Guid.Empty, Note, "Keynote");

        Assert.Equal("attachment_request.incomplete", result.Error.Code);
    }

    [Fact]
    public void A_request_without_a_note_is_refused()
    {
        var result = AttachmentRequest.Create(Request, Event, Guid.Empty, "Keynote");

        Assert.Equal("attachment_request.incomplete", result.Error.Code);
    }

    [Fact]
    public void A_request_naming_one_event_and_one_note_keeps_both_and_the_title_trimmed()
    {
        var request = AttachmentRequest.Create(Request, Event, Note, "  Keynote: Aspire in production ").Value;

        Assert.Equal(Request, request.Id);
        Assert.Equal(Event, request.EventId);
        Assert.Equal(Note, request.NoteId);
        Assert.Equal("Keynote: Aspire in production", request.NoteTitle);
    }

    [Fact]
    public void A_request_without_a_title_keeps_an_empty_one()
    {
        var request = AttachmentRequest.Create(Request, Event, Note, null).Value;

        Assert.Equal(string.Empty, request.NoteTitle);
    }

    [Fact]
    public void A_request_links_its_note_as_a_talk_note_by_the_note_id()
    {
        var request = AttachmentRequest.Create(Request, Event, Note, "Keynote").Value;

        var link = request.ToNoteLink();

        Assert.Equal(NoteLink.Create(Abstractions.NoteLinkKind.Talk, "0c3a5e7f-3333-4b2c-9d8e-000000000003", "ignored"), link);
        Assert.Equal("Keynote", link.Title);
    }
}
