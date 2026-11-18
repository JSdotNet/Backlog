using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Events.Abstractions.Services;
using Backlog.Modules.Events.DomainModels;
using Backlog.Modules.Events.Extensions;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// The published port a desktop applies the phone's attachment requests through
/// (<c>.devbook/domain/events/domain.md#attachment-request</c>,
/// <c>.devbook/domain/events/features.md#attach-a-talk-note</c>): the event gains a Talk Note
/// Link with the note's title, a request applied twice leaves one link, an incomplete request is
/// refused, and an event this desktop does not hold is answered rather than thrown. Resolved
/// through <c>AddEventsModule()</c> over an in-memory repository.
/// </summary>
public sealed class EventAttachmentRequestsTests : IDisposable
{
    private static readonly Guid NoteId = Guid.Parse("6f1c2d3e-0000-0000-0000-000000000042");

    private readonly InMemoryKnowledgeEvents _events = new();
    private readonly ServiceProvider _provider;
    private readonly IEventAttachmentRequests _requests;

    public EventAttachmentRequestsTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IKnowledgeEventRepository>(_events);
        services.AddSingleton<IEventSeriesRepository>(new InMemoryEventSeries());

        _provider = services.AddEventsModule().BuildServiceProvider();
        _requests = _provider.GetRequiredService<IEventAttachmentRequests>();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private async Task<KnowledgeEvent> SavedAsync(params NoteLink[] notes)
    {
        var knowledgeEvent = KnowledgeEvent.Create(Guid.NewGuid(), "Techorama 2026").Value;
        foreach (var note in notes) knowledgeEvent.LinkNote(note);

        await _events.SaveAsync(knowledgeEvent, Cancellation);
        return knowledgeEvent;
    }

    private static EventAttachmentRequestDto Request(Guid eventId, Guid noteId, string? title = "Keynote: Aspire in production") =>
        new(Guid.NewGuid(), eventId, noteId, title);

    [Fact]
    public async Task The_event_gains_a_talk_note_link_with_the_note_title()
    {
        var saved = await SavedAsync(NoteLink.Create(NoteLinkKind.Brain, "knowledge/aspire.md", "Aspire"));

        var outcome = await _requests.ApplyAsync(Request(saved.Id, NoteId), Cancellation);

        Assert.Equal(EventAttachmentOutcome.Attached, outcome);
        var held = (await _events.GetAsync(saved.Id, Cancellation))!;
        Assert.Equal(2, held.Notes.Count);
        Assert.Equal(NoteLinkKind.Talk, held.Notes[1].Kind);
        Assert.Equal("6f1c2d3e-0000-0000-0000-000000000042", held.Notes[1].Reference);
        Assert.Equal("Keynote: Aspire in production", held.Notes[1].Title);
    }

    [Fact]
    public async Task Attached_twice_the_event_holds_one_link_and_is_not_saved_again()
    {
        var saved = await SavedAsync();
        var request = Request(saved.Id, NoteId);

        await _requests.ApplyAsync(request, Cancellation);
        var savesAfterFirst = _events.Saves;
        var again = await _requests.ApplyAsync(request, Cancellation);
        var fromAnotherPhoneTap = await _requests.ApplyAsync(Request(saved.Id, NoteId, "Keynote, retitled"), Cancellation);

        Assert.Equal(EventAttachmentOutcome.AlreadyAttached, again);
        Assert.Equal(EventAttachmentOutcome.AlreadyAttached, fromAnotherPhoneTap);
        Assert.Equal(savesAfterFirst, _events.Saves);
        var link = Assert.Single((await _events.GetAsync(saved.Id, Cancellation))!.Notes);
        Assert.Equal("Keynote: Aspire in production", link.Title);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task A_request_missing_the_event_or_the_note_is_refused_and_changes_nothing(bool namesEvent, bool namesNote)
    {
        var saved = await SavedAsync();
        var savesBefore = _events.Saves;

        var outcome = await _requests.ApplyAsync(
            Request(namesEvent ? saved.Id : Guid.Empty, namesNote ? NoteId : Guid.Empty),
            Cancellation);

        Assert.Equal(EventAttachmentOutcome.Incomplete, outcome);
        Assert.Equal(savesBefore, _events.Saves);
        Assert.Empty((await _events.GetAsync(saved.Id, Cancellation))!.Notes);
    }

    [Fact]
    public async Task A_request_for_an_event_this_desktop_does_not_hold_is_answered_and_writes_nothing()
    {
        var outcome = await _requests.ApplyAsync(Request(Guid.NewGuid(), NoteId), Cancellation);

        Assert.Equal(EventAttachmentOutcome.UnknownEvent, outcome);
        Assert.Equal(0, _events.Saves);
    }

    public void Dispose() => _provider.Dispose();
}
