using Backlog.Infrastructure.FileSystem.Inbox;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The join behind To Event (<c>.devbook/domain/inbox/features.md#route-to-event</c>):
/// offered while Events' flag is on, and an item's title, link and found facts handed
/// to Events' published contract part for part.
/// </summary>
public sealed class InboxEventTargetTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void The_route_is_offered_while_the_events_flag_is_on()
    {
        Assert.True(new InboxEventTarget(new RecordingEvents(), new Flags(on: true)).IsOffered);
    }

    [Fact]
    public void The_route_is_withdrawn_while_the_flag_is_off_or_nothing_is_composed()
    {
        Assert.False(new InboxEventTarget(new RecordingEvents(), new Flags(on: false)).IsOffered);
        Assert.False(new InboxEventTarget(events: null, new Flags(on: true)).IsOffered);
        Assert.False(new InboxEventTarget(new RecordingEvents(), features: null).IsOffered);
    }

    [Fact]
    public async Task The_items_title_link_and_facts_reach_Events_part_for_part()
    {
        var events = new RecordingEvents();
        var offset = TimeSpan.FromHours(1);
        var request = new InboxEventRequestDto(
            Guid.CreateVersion7(),
            "dotNET Friday Q4",
            "https://dotnetfriday.nl/q4",
            new InboxEventFactsDto(
                new InboxEventDateDto(new DateOnly(2026, 11, 27), new TimeOnly(16, 30), offset),
                new InboxEventDateDto(new DateOnly(2026, 11, 27)),
                "Nieuwegein",
                "https://dotnetfriday.nl",
                "dotNET Friday"));

        var spotted = await new InboxEventTarget(events, new Flags(on: true)).SpotEventAsync(request, Cancellation);

        Assert.Equal(events.EventId, spotted.Value);
        var asked = Assert.Single(events.Requests);
        Assert.Equal(new SpotKnowledgeEventDto(
            "dotNET Friday Q4",
            "https://dotnetfriday.nl/q4",
            new EventDateDto(new DateOnly(2026, 11, 27), new TimeOnly(16, 30), offset),
            new EventDateDto(new DateOnly(2026, 11, 27)),
            "Nieuwegein",
            "https://dotnetfriday.nl",
            "dotNET Friday"), asked);
    }

    [Fact]
    public async Task An_item_with_no_facts_asks_for_an_event_with_none()
    {
        var events = new RecordingEvents();

        await new InboxEventTarget(events, new Flags(on: true))
            .SpotEventAsync(new InboxEventRequestDto(Guid.CreateVersion7(), "Some meetup", null, null), Cancellation);

        Assert.Equal(new SpotKnowledgeEventDto("Some meetup", null, null, null, null, null, null), Assert.Single(events.Requests));
    }

    [Fact]
    public async Task A_refusal_from_Events_is_the_event_not_made_in_Events_own_words()
    {
        var events = new RecordingEvents { RefuseWith = EventsErrors.TitleMissing() };

        var spotted = await new InboxEventTarget(events, new Flags(on: true))
            .SpotEventAsync(new InboxEventRequestDto(Guid.CreateVersion7(), "x", null, null), Cancellation);

        Assert.Equal(InboxErrors.EventNotMadeCode, spotted.Error.Code);
        Assert.Contains("A knowledge event needs a title.", spotted.Error.Message, StringComparison.Ordinal);
    }

    /// <summary>The requirement's last scenario: an Events store that fails to write.</summary>
    [Fact]
    public async Task A_store_that_throws_is_the_event_not_made()
    {
        var events = new RecordingEvents { Throw = new IOException("database is locked") };

        var spotted = await new InboxEventTarget(events, new Flags(on: true))
            .SpotEventAsync(new InboxEventRequestDto(Guid.CreateVersion7(), "x", null, null), Cancellation);

        Assert.Equal(InboxErrors.EventNotMadeCode, spotted.Error.Code);
        Assert.Contains("database is locked", spotted.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task While_the_flag_is_off_Events_is_not_asked()
    {
        var events = new RecordingEvents();

        var spotted = await new InboxEventTarget(events, new Flags(on: false))
            .SpotEventAsync(new InboxEventRequestDto(Guid.CreateVersion7(), "x", null, null), Cancellation);

        Assert.Equal(InboxErrors.EventsUnavailableCode, spotted.Error.Code);
        Assert.Empty(events.Requests);
    }

    private sealed class RecordingEvents : IKnowledgeEvents
    {
        public Guid EventId { get; } = Guid.CreateVersion7();

        public List<SpotKnowledgeEventDto> Requests { get; } = [];

        public Error? RefuseWith { get; init; }

        public Exception? Throw { get; init; }

        public Task<Result<SpottedKnowledgeEventDto>> SpotAsync(SpotKnowledgeEventDto request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Throw is { } failure) throw failure;
            return Task.FromResult(RefuseWith is { } refusal
                ? Result.Failure<SpottedKnowledgeEventDto>(refusal)
                : Result.Success(new SpottedKnowledgeEventDto(EventId, null, false)));
        }
    }

    private sealed class Flags(bool on) : IAppFeatureSettings
    {
        public event Action? Changed { add { } remove { } }

        public AppFeatureSettings Current => new();

        public string SettingsPath => string.Empty;

        public bool IsEnabled(string key) => key == EventsFeatures.Events && on;

        public string? SetEnabled(string key, bool enabled) => null;
    }
}
