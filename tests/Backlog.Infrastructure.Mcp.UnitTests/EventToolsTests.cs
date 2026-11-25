using Backlog.Modules.Events.Abstractions;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// <c>add_knowledge_event</c>: what the tool reads out of the text a model sends and hands the
/// Events pane's port as one request, and what it answers. The rules themselves are the
/// module's, proved in <c>Backlog.Modules.Events.UnitTests/EventsPaneAddTests</c>; here a fake
/// port stands at the boundary, so a refusal is the port's and only its code is carried.
/// </summary>
public class EventToolsTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly FakeEventsPane _pane = new();

    private EventTools Tools()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.FromHours(1)));
        clock.SetLocalTimeZone(Amsterdam);
        return new EventTools(_pane, clock);
    }

    [Fact]
    public async Task A_title_alone_is_one_request_with_nothing_else_and_answers_the_spotted_event()
    {
        var answer = await Tools().AddAsync("dotNET Friday, 27 November", cancellationToken: Cancel);

        Assert.Equal(new NewKnowledgeEventDto("dotNET Friday, 27 November"), Assert.Single(_pane.Added));
        Assert.Equal(_pane.Answered!.Id, answer.Id);
        Assert.Equal(("dotNET Friday, 27 November", "Spotted"), (answer.Title, answer.State));
        Assert.Null(answer.Start);
        Assert.Null(answer.End);
        Assert.Null(answer.Series);
        Assert.Null(answer.DateSource);
    }

    [Fact]
    public async Task Every_argument_maps_to_one_request_and_the_answer_carries_the_dates_as_iso_8601()
    {
        var answer = await Tools().AddAsync(
            "Techorama Belgium 2027",
            series: "Techorama",
            start: "2027-05-18T09:00+02:00",
            end: "2027-05-19",
            venue: "Kinepolis Antwerp",
            link: "https://techorama.be",
            whatToLearn: "Aspire in production",
            ticket: "not_bought",
            costAmount: 749m,
            costCurrency: "EUR",
            cancellationToken: Cancel);

        Assert.Equal(
            new NewKnowledgeEventDto(
                "Techorama Belgium 2027",
                SeriesName: "Techorama",
                Start: new EventDateDto(new DateOnly(2027, 5, 18), new TimeOnly(9, 0), TimeSpan.FromHours(2)),
                End: new EventDateDto(new DateOnly(2027, 5, 19)),
                Place: new PlaceDto("Kinepolis Antwerp", IsOnline: false),
                Link: "https://techorama.be",
                WhatToLearn: "Aspire in production",
                Ticket: TicketState.NotBought,
                Cost: new CostDto(749m, "EUR")),
            Assert.Single(_pane.Added));
        Assert.Equal(("2027-05-18T09:00+02:00", "2027-05-19"), (answer.Start, answer.End));
        Assert.Equal(("Techorama", "Typed"), (answer.Series, answer.DateSource));
    }

    [Theory]
    [InlineData("2026-11-27", 2026, 11, 27, null, null)]
    [InlineData("2026-11-27T16:30+01:00", 2026, 11, 27, "16:30", 60)]
    [InlineData("2026-11-27T16:30:15-05:00", 2026, 11, 27, "16:30:15", -300)]
    [InlineData("2026-11-27T16:30Z", 2026, 11, 27, "16:30", 0)]
    // No offset: the machine's zone for that day, as the editor's time field reads it — on
    // either side of the switch to summer time on 28 March 2027.
    [InlineData("2026-11-27T16:30", 2026, 11, 27, "16:30", 60)]
    [InlineData("2027-03-27T10:00", 2027, 3, 27, "10:00", 60)]
    [InlineData("2027-03-28T10:00", 2027, 3, 28, "10:00", 120)]
    public async Task A_start_is_read_as_iso_8601(string text, int year, int month, int day, string? time, int? offsetMinutes)
    {
        await Tools().AddAsync("dotNET Friday", start: text, cancellationToken: Cancel);

        Assert.Equal(
            new EventDateDto(
                new DateOnly(year, month, day),
                time is null ? null : TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture),
                offsetMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null),
            Assert.Single(_pane.Added).Start);
    }

    [Theory]
    [InlineData("27-11-2026")]
    [InlineData("tomorrow")]
    [InlineData("2026-11-27T16")]
    [InlineData("2026-02-30")]
    public async Task A_date_that_is_not_iso_8601_is_refused_and_nothing_is_asked_of_the_pane(string text)
    {
        var refused = await Assert.ThrowsAsync<McpException>(() => Tools().AddAsync("dotNET Friday", end: text, cancellationToken: Cancel));

        Assert.Contains("events.date_unreadable", refused.Message, StringComparison.Ordinal);
        Assert.Empty(_pane.Added);
    }

    [Theory]
    [InlineData("bought", TicketState.Bought)]
    [InlineData("NotBought", TicketState.NotBought)]
    [InlineData("NOT_NEEDED", TicketState.NotNeeded)]
    [InlineData("not-bought", TicketState.NotBought)]
    public async Task A_ticket_word_is_read_without_regard_to_case(string word, TicketState expected)
    {
        await Tools().AddAsync("dotNET Friday", ticket: word, cancellationToken: Cancel);

        Assert.Equal(expected, Assert.Single(_pane.Added).Ticket);
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("1")]
    public async Task An_unknown_ticket_word_is_refused_and_nothing_is_asked_of_the_pane(string word)
    {
        var refused = await Assert.ThrowsAsync<McpException>(() => Tools().AddAsync("dotNET Friday", ticket: word, cancellationToken: Cancel));

        Assert.Contains("events.unknown_ticket", refused.Message, StringComparison.Ordinal);
        Assert.Empty(_pane.Added);
    }

    [Fact]
    public async Task Online_and_a_venue_are_a_place_and_both_are_handed_on_for_the_module_to_refuse()
    {
        await Tools().AddAsync("Online", online: true, cancellationToken: Cancel);
        await Tools().AddAsync("Neither", online: false, cancellationToken: Cancel);
        await Tools().AddAsync("Both", online: true, venue: "Nieuwegein", cancellationToken: Cancel);

        Assert.Equal(
            [new PlaceDto(null, true), null, new PlaceDto("Nieuwegein", true)],
            _pane.Added.Select(request => request.Place));
    }

    [Fact]
    public async Task A_currency_with_no_amount_is_refused_as_an_invalid_cost()
    {
        var refused = await Assert.ThrowsAsync<McpException>(() => Tools().AddAsync("Techorama", costCurrency: "EUR", cancellationToken: Cancel));

        Assert.Contains("cost.invalid", refused.Message, StringComparison.Ordinal);
        Assert.Empty(_pane.Added);
    }

    [Fact]
    public async Task An_amount_with_no_currency_is_handed_on_for_the_module_to_refuse()
    {
        await Tools().AddAsync("Techorama", costAmount: 749m, cancellationToken: Cancel);

        Assert.Equal(new CostDto(749m, string.Empty), Assert.Single(_pane.Added).Cost);
    }

    [Fact]
    public async Task A_refusal_from_the_module_reaches_the_caller_with_its_code()
    {
        _pane.RefuseWith = EventsErrors.DatesOutOfOrder();

        var refused = await Assert.ThrowsAsync<McpException>(() => Tools().AddAsync(
            "NDC Oslo", start: "2027-06-09", end: "2027-06-07", cancellationToken: Cancel));

        Assert.Contains("knowledge_event.dates_out_of_order", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>The Events pane's port at the boundary: it records each add and answers the
    /// event as the module would read it, or the refusal it is set to.</summary>
    internal sealed class FakeEventsPane : IEventsPane
    {
        public List<NewKnowledgeEventDto> Added { get; } = [];

        public Error? RefuseWith { get; set; }

        public KnowledgeEventDto? Answered { get; private set; }

        public Task<Result<KnowledgeEventDto>> AddAsync(NewKnowledgeEventDto request, CancellationToken cancellationToken = default)
        {
            Added.Add(request);
            if (RefuseWith is { } error) return Task.FromResult(Result.Failure<KnowledgeEventDto>(error));

            Answered = new KnowledgeEventDto(
                Guid.NewGuid(),
                request.Title.Trim(),
                request.SeriesName is { } name ? new EventSeriesDto(Guid.NewGuid(), name.Trim(), null, null) : null,
                request.Start,
                request.End,
                request.Start is null ? null : DateSource.Typed,
                request.Place,
                request.Link,
                KnowledgeEventState.Spotted,
                null,
                request.WhatToLearn,
                [],
                request.Ticket,
                request.Cost,
                [],
                [],
                null,
                [],
                RemoveOffered: true);
            return Task.FromResult(Result.Success(Answered));
        }

        public string? BrainUnavailableReason => null;

        public event Action? Changed { add { } remove { } }

        public Task<IReadOnlyList<KnowledgeEventSectionDto>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<KnowledgeEventDto>> GetAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<KnowledgeEventDto>> AddAsync(string title, Guid? seriesId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<KnowledgeEventDto>> RenameAsync(Guid id, string title, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<KnowledgeEventDto>> MoveToAsync(Guid id, KnowledgeEventState target, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<KnowledgeEventDto>> SkipAsync(Guid id, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result> RemoveAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<KnowledgeEventDto>> SetDatesAsync(Guid id, EventDateDto? start, EventDateDto? end, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<KnowledgeEventDto>> SetPlaceAsync(Guid id, PlaceDto? place, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AddressLookUpDto>> LookUpAddressAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AddressLookupPassDto> LookUpPendingAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public bool IsLookingUp(Guid id) => false;

        public Task<Result<KnowledgeEventDto>> SetLinkAsync(Guid id, string? link, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<KnowledgeEventDto>> SetWhatToLearnAsync(Guid id, string? whatToLearn, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<KnowledgeEventDto>> SetKeyDatesAsync(Guid id, IReadOnlyList<KeyDateDto> keyDates, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<KnowledgeEventDto>> SetTicketAsync(Guid id, TicketState? ticket, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<KnowledgeEventDto>> SetCostAsync(Guid id, CostDto? cost, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<KnowledgeEventDto>> SetVerdictAsync(Guid id, string? verdict, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<EventSeriesDto>> ListSeriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<EventSeriesDto>> StartSeriesAsync(string name, string? organiser = null, string? link = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<KnowledgeEventDto>> SetSeriesAsync(Guid id, Guid? seriesId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<KnowledgeEventDto>> LinkBrainNoteAsync(Guid id, string path, string? title = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<KnowledgeEventDto>> RelinkBrainNoteAsync(Guid id, string oldPath, string newPath, string? title = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<KnowledgeEventDto>> UnlinkNoteAsync(Guid id, NoteLinkKind kind, string reference, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<KnowledgeEventDto>> AddPrepTaskAsync(Guid id, string title, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<KnowledgeEventDto>> LinkExistingTaskAsync(Guid id, string taskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<EventBrainNoteDto>> BrainNoteCandidatesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
