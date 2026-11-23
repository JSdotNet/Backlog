using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;
using Backlog.Modules.Calendar.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Modules.Calendar.Services;

/// <summary>
/// The Events layer's read policy (local ADR 0055 §3): the last read of
/// <see cref="ICalendarEvents"/>, and when the next one is owed.
/// <para>
/// No change signal: nothing writes an event from the Calendar, so the events are read on
/// arriving at the Calendar, after a feature switched — which is how the Show events box
/// comes and goes as the Events area is switched — and on a new day, never on every
/// render. Show events ticked reads at once.
/// </para>
/// <para>
/// It starts <see cref="CalendarEventsDto.Off"/>, and each arrival puts it back there — fail
/// closed, as the adapter does — so no Show events box is offered before the arrival's own
/// read says whether the Events area is on: a box drawn in that gap could be ticked, and
/// the tick kept, while the area is off — as every arrival was when the Tasks pane read
/// the events itself.
/// Without a port — a host that composed no events — nothing is offered or read.
/// </para>
/// </summary>
internal sealed class CalendarEventsLayer : ICalendarEventsLayer, IDisposable
{
    private readonly ICalendarEvents? _events;
    private readonly IAppFeatureSettings? _features;
    private bool _owed = true;
    private DateOnly? _readOn;

    public CalendarEventsLayer(ICalendarEvents? events, IAppFeatureSettings? features)
    {
        _events = events;
        _features = features;
        if (_features is not null) _features.Changed += OnOwed;
    }

    public event Action? Changed;

    public bool Offered => _events is not null;

    public CalendarEventsDto Events { get; private set; } = CalendarEventsDto.Off;

    public bool Available => Offered && Events.Available;

    /// <summary>Owes a read, and is Off again until it lands: the layer outlives the
    /// surface, and the Events area may have been switched off while the Calendar was
    /// away, so the read it left with must not offer the box.</summary>
    public void Arrive()
    {
        _owed = true;
        Events = CalendarEventsDto.Off;
    }

    public Task ReadIfOwedAsync(DateOnly today, CancellationToken cancellationToken = default) =>
        _events is null || (!_owed && _readOn == today) ? Task.CompletedTask : ReadAsync(today, cancellationToken);

    public async Task ReadAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        if (_events is null) return;

        // Settled before the await, so a second caller arriving while this read is out
        // does not read again.
        _owed = false;
        _readOn = today;

        try
        {
            Events = await _events.ReadAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Events that cannot be read leave the tasks on the Calendar; the bars and
            // diamonds are what goes missing, not the month.
            Events = CalendarEventsDto.Empty;
        }
    }

    private void OnOwed()
    {
        _owed = true;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_features is not null) _features.Changed -= OnOwed;
    }
}
