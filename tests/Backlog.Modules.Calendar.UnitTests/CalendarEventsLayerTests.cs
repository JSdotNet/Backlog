using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;
using Backlog.Modules.Calendar.Abstractions.Services;
using Backlog.Modules.Calendar.Extensions;
using Backlog.Modules.Calendar.Services;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Calendar.UnitTests;

/// <summary>
/// The Events layer's read policy (local ADR 0055 §3): the Calendar reads the knowledge
/// events only when a read is owed — on arriving, after a feature switched, and on a new
/// day — and fails closed: no Show events box before the first read says the Events area
/// is on, and a read that fails leaves the tasks on the Calendar with no bars.
/// </summary>
public sealed class CalendarEventsLayerTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_a_port_nothing_is_offered_or_read()
    {
        using var layer = new CalendarEventsLayer(null, null);

        Assert.False(layer.Offered);
        Assert.False(layer.Available);
        await layer.ReadIfOwedAsync(Today, Ct);
        await layer.ReadAsync(Today, Ct);
        Assert.Same(CalendarEventsDto.Off, layer.Events);
    }

    /// <summary>Until the first read lands the layer does not know whether the Events area
    /// is on, so it offers nothing: a box drawn in that gap could be ticked while the area
    /// is off.</summary>
    [Fact]
    public void Before_the_first_read_the_layer_is_off()
    {
        using var layer = new CalendarEventsLayer(new FakeEvents(), null);

        Assert.True(layer.Offered);
        Assert.Same(CalendarEventsDto.Off, layer.Events);
        Assert.False(layer.Available);
    }

    [Fact]
    public async Task The_first_read_is_owed_and_the_same_day_reads_once()
    {
        var port = new FakeEvents();
        using var layer = new CalendarEventsLayer(port, null);

        await layer.ReadIfOwedAsync(Today, Ct);
        await layer.ReadIfOwedAsync(Today, Ct);

        Assert.Equal(1, port.Reads);
        Assert.Equal("Techorama", Assert.Single(layer.Events.Events).Title);
        Assert.True(layer.Available);
    }

    [Fact]
    public async Task A_new_day_reads_again()
    {
        var port = new FakeEvents();
        using var layer = new CalendarEventsLayer(port, null);
        await layer.ReadIfOwedAsync(Today, Ct);

        await layer.ReadIfOwedAsync(Today.AddDays(1), Ct);

        Assert.Equal(2, port.Reads);
    }

    [Fact]
    public async Task Arriving_reads_again_whatever_was_read_before()
    {
        var port = new FakeEvents();
        using var layer = new CalendarEventsLayer(port, null);
        await layer.ReadIfOwedAsync(Today, Ct);

        layer.Arrive();

        await layer.ReadIfOwedAsync(Today, Ct);
        Assert.Equal(2, port.Reads);
    }

    /// <summary>The layer lives as long as the window, so a Calendar it arrives at again
    /// must not offer the box from the read it left with: the Events area may have been
    /// switched off meanwhile. Each arrival is Off until its own read lands.</summary>
    [Fact]
    public async Task Arriving_offers_no_events_until_its_own_read_lands()
    {
        var port = new FakeEvents();
        using var layer = new CalendarEventsLayer(port, null);
        await layer.ReadIfOwedAsync(Today, Ct);
        Assert.True(layer.Available);

        layer.Arrive();

        Assert.Same(CalendarEventsDto.Off, layer.Events);
        Assert.False(layer.Available);

        port.Answer = CalendarEventsDto.Off;
        await layer.ReadIfOwedAsync(Today, Ct);
        Assert.False(layer.Available);
    }

    /// <summary>A feature switch is how the box comes and goes as the Events area is
    /// switched: it owes a read, and says so.</summary>
    [Fact]
    public async Task A_feature_switch_owes_a_read_and_says_so()
    {
        var port = new FakeEvents();
        var features = new FakeFeatures();
        using var layer = new CalendarEventsLayer(port, features);
        await layer.ReadIfOwedAsync(Today, Ct);
        var said = 0;
        layer.Changed += () => said++;

        features.SetEnabled("events", false);

        Assert.Equal(1, said);
        await layer.ReadIfOwedAsync(Today, Ct);
        Assert.Equal(2, port.Reads);
    }

    [Fact]
    public async Task Reading_now_reads_whether_or_not_one_is_owed()
    {
        var port = new FakeEvents();
        using var layer = new CalendarEventsLayer(port, null);
        await layer.ReadIfOwedAsync(Today, Ct);

        await layer.ReadAsync(Today, Ct);

        Assert.Equal(2, port.Reads);
    }

    [Fact]
    public async Task With_the_events_area_off_the_layer_offers_no_events()
    {
        var port = new FakeEvents { Answer = CalendarEventsDto.Off };
        using var layer = new CalendarEventsLayer(port, null);

        await layer.ReadIfOwedAsync(Today, Ct);

        Assert.True(layer.Offered);
        Assert.False(layer.Available);
    }

    /// <summary>The Tasks pane's behaviour, kept: a failed read is
    /// <see cref="CalendarEventsDto.Empty"/> — the bars and the diamonds go missing, not the
    /// month. Empty still says the area is on, as the adapter only reads while it is, so
    /// the box stays.</summary>
    [Fact]
    public async Task A_read_that_fails_leaves_the_events_empty_and_still_available()
    {
        var port = new FakeEvents();
        using var layer = new CalendarEventsLayer(port, null);
        await layer.ReadIfOwedAsync(Today, Ct);

        port.Failure = new IOException("The events could not be read.");
        await layer.ReadAsync(Today, Ct);

        Assert.Same(CalendarEventsDto.Empty, layer.Events);
        Assert.True(layer.Available);
    }

    [Fact]
    public async Task A_cancelled_read_is_not_swallowed()
    {
        var port = new FakeEvents { Failure = new OperationCanceledException() };
        using var layer = new CalendarEventsLayer(port, null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => layer.ReadAsync(Today, Ct));
    }

    [Fact]
    public void Disposing_stops_listening()
    {
        var features = new FakeFeatures();
        var layer = new CalendarEventsLayer(new FakeEvents(), features);

        layer.Dispose();

        Assert.Equal(0, features.Listeners);
    }

    /// <summary>One layer per window, as the Plans layer is: Scoped, resolved through a
    /// validating container with and without a port composed.</summary>
    [Fact]
    public void AddCalendarModule_resolves_one_events_layer_per_scope_and_is_safe_to_call_twice()
    {
        var services = new ServiceCollection().AddCalendarModule();
        var once = services.Count;
        services.AddCalendarModule();
        Assert.Equal(once, services.Count);

        services.AddSingleton<ICalendarEvents, FakeEvents>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var layer = first.ServiceProvider.GetRequiredService<ICalendarEventsLayer>();

        Assert.True(layer.Offered);
        Assert.Same(layer, first.ServiceProvider.GetRequiredService<ICalendarEventsLayer>());
        Assert.NotSame(layer, second.ServiceProvider.GetRequiredService<ICalendarEventsLayer>());
    }

    [Fact]
    public void AddCalendarModule_without_a_port_resolves_an_events_layer_that_offers_no_events()
    {
        using var provider = new ServiceCollection().AddCalendarModule()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        Assert.False(scope.ServiceProvider.GetRequiredService<ICalendarEventsLayer>().Offered);
    }

    private sealed class FakeEvents : ICalendarEvents
    {
        public int Reads { get; private set; }

        public CalendarEventsDto Answer { get; set; } = new(
            [new CalendarEventDto("Techorama", "Going", "Antwerp", Today, Today.AddDays(2), Tentative: false)],
            [new CalendarKeyDateDto("Early bird ends", "Techorama", Today)]);

        public Exception? Failure { get; set; }

        public Task<CalendarEventsDto> ReadAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Failure is { } failure ? Task.FromException<CalendarEventsDto>(failure) : Task.FromResult(Answer);
        }
    }

    private sealed class FakeFeatures : IAppFeatureSettings
    {
        public event Action? Changed;

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "features.json";

        public int Listeners => Changed?.GetInvocationList().Length ?? 0;

        public bool IsEnabled(string key) => true;

        public string? SetEnabled(string key, bool enabled)
        {
            Changed?.Invoke();
            return null;
        }
    }
}
