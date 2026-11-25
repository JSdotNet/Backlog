using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.SharedKernel.Results;
using Backlog.UI.Components.Goals;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Progress.UI.Goals;

/// <summary>
/// Cascades the <see cref="PlanImpedimentSource"/> the Tasks pane names a Blocked plan's
/// impediment by (local ADR 0035 §5; local ADR 0062 §2–§3), the way
/// <see cref="GoalControlRegistration"/> cascades the goal control: Tasks asks the
/// shared library's hook and never this module (local ADR 0035 §6).
/// </summary>
public static class PlanImpedimentRegistration
{
    public static IServiceCollection AddPlanImpediments(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Scoped, so the container lets go of its Changed subscription with the window.
        services.AddScoped(provider => new PlanImpedimentReader(provider.GetService<IImpediments>()));
        services.AddCascadingValue(provider => provider.GetRequiredService<PlanImpedimentReader>().Source);

        return services;
    }
}

/// <summary>
/// The open impediments as last read, kept for the Tasks pane's renders to ask without
/// waiting: read once at the start and again each time an impediment is stored, the
/// source raised after each reading lands.
/// </summary>
internal sealed class PlanImpedimentReader : IDisposable
{
    private readonly IImpediments? _impediments;
    private readonly object _gate = new();
    private IReadOnlyList<ImpedimentDto> _open = [];
    private int _readings;
    private event Action? Read;

    public PlanImpedimentReader(IImpediments? impediments)
    {
        _impediments = impediments;
        Source = new PlanImpedimentSource(TitleOf, Watch);

        if (_impediments is null) return;

        _impediments.Changed += OnImpedimentsChanged;
        _ = RefreshAsync();
    }

    /// <summary>The hook the host cascades.</summary>
    public PlanImpedimentSource Source { get; }

    /// <summary>The first-raised open impediment holding back the plan <paramref name="plan"/>
    /// (bare tag) or one of <paramref name="entryIds"/>, by title; null when none does.</summary>
    internal string? TitleOf(string plan, IReadOnlyCollection<Guid> entryIds)
    {
        IReadOnlyList<ImpedimentDto> open;
        lock (_gate) open = _open;

        return open.FirstOrDefault(impediment => impediment.IsOpen && impediment.HoldsBack.Any(item => Holds(item, plan, entryIds)))?.Title;
    }

    /// <summary>Reads the open impediments again; a reading that lands after a later one
    /// started is dropped, so the newest answer wins.</summary>
    internal async Task RefreshAsync()
    {
        if (_impediments is null) return;

        int reading;
        lock (_gate) reading = ++_readings;

        Result<IReadOnlyList<ImpedimentDto>> listed;
        try
        {
            listed = await _impediments.ListAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A failed reading keeps the last answer: a plan reads plain Blocked at worst.
            return;
        }

        if (listed is not Ok<IReadOnlyList<ImpedimentDto>>(var open)) return;

        lock (_gate)
        {
            if (reading != _readings) return;
            _open = open;
        }

        Read?.Invoke();
    }

    public void Dispose()
    {
        if (_impediments is not null) _impediments.Changed -= OnImpedimentsChanged;
        Read = null;
    }

    private static bool Holds(HeldBackItemDto item, string plan, IReadOnlyCollection<Guid> entryIds) => item.Kind switch
    {
        HeldBackKind.Plan => item.PlanTag is { } tag && string.Equals(tag.TrimStart('+'), plan, StringComparison.OrdinalIgnoreCase),
        HeldBackKind.Task => item.TaskId is { } id && entryIds.Contains(id),
        _ => false,
    };

    private void OnImpedimentsChanged() => _ = RefreshAsync();

    private IDisposable Watch(Action changed)
    {
        Read += changed;
        return new Subscription(() => Read -= changed);
    }

    private sealed class Subscription(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
