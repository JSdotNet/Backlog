using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// Linking a note again and removing a wrong one (<c>.devbook/domain/events/domain.md#note-link</c>):
/// a Brain note that moved shows as missing, and the person points the link at where it went. The
/// rule "a note is linked once" holds through both.
/// </summary>
public sealed class KnowledgeEventRelinkTests
{
    private static readonly NoteLink Moved = NoteLink.Create(NoteLinkKind.Brain, "areas/learning/techorama.md", "Techorama");
    private static readonly NoteLink Found = NoteLink.Create(NoteLinkKind.Brain, "archive/techorama-2026.md", "Techorama 2026");
    private static readonly NoteLink Other = NoteLink.Create(NoteLinkKind.Brain, "areas/learning/aspire.md", "Aspire");

    [Fact]
    public void A_missing_link_is_replaced_in_its_place()
    {
        var techorama = WithNotes(Moved, Other);

        var result = techorama.ReplaceNoteLink(Moved, Found);

        Assert.True(result.IsSuccess);
        Assert.Equal(["archive/techorama-2026.md", "areas/learning/aspire.md"], techorama.Notes.Select(note => note.Reference));
        Assert.Equal("Techorama 2026", techorama.Notes[0].Title);
    }

    [Fact]
    public void Replacing_with_a_note_already_linked_keeps_it_once()
    {
        var techorama = WithNotes(Moved, Other);

        var result = techorama.ReplaceNoteLink(Moved, Other);

        Assert.True(result.IsSuccess);
        Assert.Equal(["areas/learning/aspire.md"], techorama.Notes.Select(note => note.Reference));
    }

    [Fact]
    public void Replacing_a_note_the_event_does_not_link_is_refused_and_changes_nothing()
    {
        var techorama = WithNotes(Other);

        var result = techorama.ReplaceNoteLink(Moved, Found);

        Assert.Equal("note_link.not_linked", result.Error.Code);
        Assert.Equal(["areas/learning/aspire.md"], techorama.Notes.Select(note => note.Reference));
    }

    [Fact]
    public void Unlinking_removes_the_note_and_unlinking_again_changes_nothing()
    {
        var techorama = WithNotes(Moved, Other);

        techorama.UnlinkNote(Moved);
        techorama.UnlinkNote(Moved);

        Assert.Equal(["areas/learning/aspire.md"], techorama.Notes.Select(note => note.Reference));
    }

    private static KnowledgeEvent WithNotes(params NoteLink[] notes)
    {
        var knowledgeEvent = KnowledgeEvent.Create(Guid.NewGuid(), "Techorama").Value;
        foreach (var note in notes) knowledgeEvent.LinkNote(note);
        return knowledgeEvent;
    }
}
