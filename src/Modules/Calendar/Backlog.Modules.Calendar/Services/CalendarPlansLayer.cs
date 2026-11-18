using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;
using Backlog.Modules.Calendar.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Modules.Calendar.Services;

/// <summary>
/// The Plans layer's read policy (local ADR 0055 §3): the last read of
/// <see cref="ICalendarPlans"/>, and when the next one is owed.
/// <para>
/// A read gathers the backlog per plan, so it is never taken on every render: only on
/// arriving at the Calendar, after the port said the plans, their work or the pace
/// changed, after a feature switched — the roadmap switched off means no plans — and on a
/// new day, when the windows the roadmap draws move on. Show plans ticked reads at once.
/// </para>
/// <para>
/// Without a port — a host that composed no roadmap — nothing is offered, read or started.
/// </para>
/// </summary>
internal sealed class CalendarPlansLayer : ICalendarPlansLayer, IDisposable
{
    private readonly ICalendarPlans? _plans;
    private readonly IAppFeatureSettings? _features;
    private bool _owed = true;
    private DateOnly? _readOn;

    public CalendarPlansLayer(ICalendarPlans? plans, IAppFeatureSettings? features)
    {
        _plans = plans;
        _features = features;
        if (_plans is not null) _plans.Changed += OnOwed;
        if (_features is not null) _features.Changed += OnOwed;
    }

    public event Action? Changed;

    public bool Offered => _plans is not null;

    public CalendarPlansDto Plans { get; private set; } = CalendarPlansDto.Empty;

    public bool Available => Offered && Plans.Available;

    public void Arrive() => _owed = true;

    public Task ReadIfOwedAsync(DateOnly today, CancellationToken cancellationToken = default) =>
        _plans is null || (!_owed && _readOn == today) ? Task.CompletedTask : ReadAsync(today, cancellationToken);

    public async Task ReadAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        if (_plans is null) return;

        // Settled before the await, so a second caller arriving while this read is out
        // does not read again.
        _owed = false;
        _readOn = today;

        try
        {
            Plans = await _plans.ReadAsync(today, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A plan that cannot be read leaves the tasks on the Calendar; the bars are
            // what goes missing, not the view.
            Plans = CalendarPlansDto.Empty;
        }
    }

    public Task<string?> StartAsync(string tag, DateOnly start, CancellationToken cancellationToken = default) =>
        _plans?.StartAsync(tag, start, cancellationToken) ?? Task.FromResult<string?>(null);

    private void OnOwed()
    {
        _owed = true;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_plans is not null) _plans.Changed -= OnOwed;
        if (_features is not null) _features.Changed -= OnOwed;
    }
}
