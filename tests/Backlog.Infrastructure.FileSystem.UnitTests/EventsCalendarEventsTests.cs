using Backlog.Infrastructure.FileSystem.Events;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Events.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The Tasks Calendar's events layer, answered from the events read contract (local ADR 0050
/// §7, <c>.devbook/domain/events/requirements.md#events-on-the-calendar</c>): each decided event
/// in the Calendar's words, Considering drawn tentative and the rest firm, and each key date
/// with the event it belongs to. With the <c>events</c> area off — or no feature settings to
/// ask — nothing is read and the Calendar offers no events.
/// </summary>
public sealed class EventsCalendarEventsTests : IDisposable
{
    private static readonly DateOnly May12 = new(2026, 5, 12);
    private static readonly DateOnly May14 = new(2026, 5, 14);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "events-calendar-events-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        var path = NavigationPath();
        ShellNavigationStore.WaitForPendingWrites(path);
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // --- The area's flag ------------------------------------------------------------------

    [Fact]
    public async Task With_the_events_area_off_nothing_is_read_and_no_events_are_offered()
    {
        var calendar = new EventsCalendarEvents(new UnreadableReader(), new Features(events: false));

        var read = await calendar.ReadAsync(TestContext.Current.CancellationToken);

        Assert.False(read.Available);
        Assert.Empty(read.Events);
        Assert.Empty(read.KeyDates);
    }

    [Fact]
    public async Task With_no_feature_settings_to_ask_no_events_are_offered()
    {
        var calendar = new EventsCalendarEvents(new UnreadableReader());

        var read = await calendar.ReadAsync(TestContext.Current.CancellationToken);

        Assert.False(read.Available);
    }

    // --- Each event in the Calendar's words ---------------------------------------------------

    [Fact]
    public async Task A_going_event_is_a_firm_bar_across_its_days_named_with_its_state_and_venue()
    {
        var calendar = On(Decided("Techorama", KnowledgeEventState.Going, May12, May14, venue: "Antwerp"));

        var read = await calendar.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(read.Available);
        Assert.Equal(
            [new CalendarEventDto("Techorama", "Going", "Antwerp", May12, May14, Tentative: false)],
            read.Events);
    }

    [Theory]
    [InlineData(KnowledgeEventState.Considering, true)]
    [InlineData(KnowledgeEventState.Going, false)]
    [InlineData(KnowledgeEventState.Attended, false)]
    [InlineData(KnowledgeEventState.Harvested, false)]
    public async Task Only_a_considering_event_is_drawn_tentative(KnowledgeEventState state, bool tentative)
    {
        var calendar = On(Decided("Techorama", state, May12, May14));

        var techorama = Assert.Single((await calendar.ReadAsync(TestContext.Current.CancellationToken)).Events);

        Assert.Equal(tentative, techorama.Tentative);
        Assert.Equal(state.ToString(), techorama.State);
    }

    [Fact]
    public async Task An_online_event_is_placed_online_and_one_with_no_place_has_none()
    {
        var calendar = On(
            Decided("Build", KnowledgeEventState.Going, May12, May12, online: true),
            Decided("Local meetup", KnowledgeEventState.Going, May14, May14));

        var read = await calendar.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Online", null], read.Events.Select(knowledgeEvent => knowledgeEvent.Place));
    }

    [Fact]
    public async Task An_event_with_a_start_and_no_end_is_a_one_day_bar()
    {
        var calendar = On(Decided("dotNET Friday", KnowledgeEventState.Going, May12, end: null));

        var friday = Assert.Single((await calendar.ReadAsync(TestContext.Current.CancellationToken)).Events);

        Assert.Equal(May12, friday.Start);
        Assert.Equal(May12, friday.End);
    }

    // --- Key dates --------------------------------------------------------------------------

    [Fact]
    public async Task Each_key_date_names_its_event_in_the_order_the_events_and_their_dates_come()
    {
        var calendar = On(
            Decided("Techorama", KnowledgeEventState.Going, May12, May14,
                keyDates: [new(new DateOnly(2026, 3, 3), "Early bird ends"), new(new DateOnly(2026, 4, 1), "Registration closes")]),
            Decided("NDC Oslo", KnowledgeEventState.Considering, new DateOnly(2026, 6, 8), new DateOnly(2026, 6, 12),
                keyDates: [new(new DateOnly(2026, 2, 15), "Call for speakers closes")]));

        var read = await calendar.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new CalendarKeyDateDto("Early bird ends", "Techorama", new DateOnly(2026, 3, 3)),
                new CalendarKeyDateDto("Registration closes", "Techorama", new DateOnly(2026, 4, 1)),
                new CalendarKeyDateDto("Call for speakers closes", "NDC Oslo", new DateOnly(2026, 2, 15))
            ],
            read.KeyDates);
    }

    [Fact]
    public async Task An_event_with_no_start_draws_no_bar_but_keeps_its_key_dates()
    {
        var calendar = On(Decided("Techorama", KnowledgeEventState.Considering, start: null, end: null,
            keyDates: [new(new DateOnly(2026, 3, 3), "Early bird ends")]));

        var read = await calendar.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(read.Events);
        Assert.Equal([new CalendarKeyDateDto("Early bird ends", "Techorama", new DateOnly(2026, 3, 3))], read.KeyDates);
    }

    // --- The "Show events" box ----------------------------------------------------------------

    [Fact]
    public void Without_a_store_the_box_is_off_and_holds_its_choice_for_the_run()
    {
        var calendar = new EventsCalendarEvents(new UnreadableReader(), new Features(events: true));

        Assert.False(calendar.Shown);

        calendar.SetShown(true);
        Assert.True(calendar.Shown);

        calendar.SetShown(false);
        Assert.False(calendar.Shown);
    }

    [Fact]
    public void With_a_store_the_box_is_off_until_ticked_and_the_choice_is_kept_on_the_device()
    {
        var store = new ShellNavigationStore(NavigationPath());
        var calendar = new EventsCalendarEvents(new UnreadableReader(), new Features(events: true), store);

        Assert.False(calendar.Shown);

        calendar.SetShown(true);

        Assert.True(store.CalendarEventsShown);
        Assert.False(store.CalendarPlansShown);
        store.Flush();
        var restarted = new EventsCalendarEvents(new UnreadableReader(), new Features(events: true), new ShellNavigationStore(NavigationPath()));
        Assert.True(restarted.Shown);
    }

    private string NavigationPath() => Path.Combine(_root, "shell-navigation.json");

    private static EventsCalendarEvents On(params DecidedEventDto[] events) =>
        new(new ListedReader(events), new Features(events: true));

    private static DecidedEventDto Decided(
        string title,
        KnowledgeEventState state,
        DateOnly? start,
        DateOnly? end,
        string? venue = null,
        bool online = false,
        IReadOnlyList<KeyDateDto>? keyDates = null) =>
        new(Guid.NewGuid(), title, state, start, end, venue, online, keyDates ?? []);

    private sealed class ListedReader(IReadOnlyList<DecidedEventDto> events) : IKnowledgeEventReader
    {
        public Task<IReadOnlyList<DecidedEventDto>> ListDecidedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(events);
    }

    /// <summary>A reader the area's switch must keep the Calendar from: off, nothing is read.</summary>
    private sealed class UnreadableReader : IKnowledgeEventReader
    {
        public Task<IReadOnlyList<DecidedEventDto>> ListDecidedAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The events were read while the area is off.");
    }

    private sealed class Features(bool events) : IAppFeatureSettings
    {
        public event Action? Changed { add { } remove { } }

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "features.json";

        public bool IsEnabled(string key) =>
            key == EventsFeatures.Events ? events : throw new ArgumentException("Unknown feature " + key, nameof(key));

        public string? SetEnabled(string key, bool enabled) => null;
    }
}
