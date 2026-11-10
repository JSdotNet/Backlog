using Backlog.Infrastructure.Capture.Events;
using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Events.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.Capture.UnitTests;

/// <summary>
/// The event page source is offered while the Events area's flag is on, and
/// every other source always — the requirement "no event page source while
/// the area is off", answered where both contexts may be seen.
/// </summary>
public sealed class EventsAreaSourceAvailabilityTests
{
    [Fact]
    public void The_event_page_is_offered_only_while_the_events_flag_is_on()
    {
        var features = new FakeFeatures();
        var availability = new EventsAreaSourceAvailability(features);

        Assert.False(availability.IsOffered(CaptureSourceKind.EventPage));

        features.SetEnabled(EventsFeatures.Events, true);

        Assert.True(availability.IsOffered(CaptureSourceKind.EventPage));
    }

    [Theory]
    [InlineData(CaptureSourceKind.YouTube)]
    [InlineData(CaptureSourceKind.Website)]
    [InlineData(CaptureSourceKind.Email)]
    [InlineData(CaptureSourceKind.Import)]
    public void Every_other_source_is_offered_whatever_the_flag_says(CaptureSourceKind kind)
    {
        Assert.True(new EventsAreaSourceAvailability(new FakeFeatures()).IsOffered(kind));
    }

    /// <summary>With no feature settings to ask, the source stays withdrawn:
    /// the flag starts off.</summary>
    [Fact]
    public void Without_feature_settings_the_event_page_stays_withdrawn()
    {
        var availability = new EventsAreaSourceAvailability();

        Assert.False(availability.IsOffered(CaptureSourceKind.EventPage));
        Assert.True(availability.IsOffered(CaptureSourceKind.Website));

        // Nothing to listen to, and nothing thrown for listening.
        availability.Changed += () => { };
    }

    [Fact]
    public void A_flag_switched_is_passed_on_to_whoever_listens_and_stops_when_they_leave()
    {
        var features = new FakeFeatures();
        var availability = new EventsAreaSourceAvailability(features);
        var raised = 0;
        void Listener() => raised++;

        availability.Changed += Listener;
        features.SetEnabled(EventsFeatures.Events, true);
        availability.Changed -= Listener;
        features.SetEnabled(EventsFeatures.Events, false);

        Assert.Equal(1, raised);
    }

    private sealed class FakeFeatures : IAppFeatureSettings
    {
        public event Action? Changed;

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "memory";

        public bool IsEnabled(string key) => Current.EnabledFeatures.Contains(key);

        public string? SetEnabled(string key, bool enabled)
        {
            if (enabled) Current.EnabledFeatures.Add(key);
            else Current.EnabledFeatures.Remove(key);

            Changed?.Invoke();
            return null;
        }
    }
}
