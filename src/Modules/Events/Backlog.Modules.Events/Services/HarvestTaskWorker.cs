using Backlog.Modules.Events.Abstractions;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Modules.Events.Services;

/// <summary>
/// The Desktop clock (<c>.devbook/domain/events/context.md#desktop-clock</c>): runs the
/// <see cref="HarvestTaskPolicy"/>'s filing shortly after the app starts, and again each time
/// the local day turns while it runs.
/// <para>
/// A plain object with a timer in it, resolved by the desktop heads after <c>Build()</c>, for
/// the reason <c>LinkedTaskSyncWorker</c> gives: the MAUI head has no generic host to start an
/// <c>IHostedService</c>. The timer is a one-shot re-armed after every check, as
/// <c>BackupWorker</c>'s is, because the day turns at a time of day rather than on a period.
/// The policy is resolved from a fresh scope per check, because the task target it holds is
/// scoped.
/// </para>
/// <para>
/// With the <c>events</c> area off, or no feature settings to ask, a check files nothing: the
/// flag starts off, so off is the fail-closed answer, as it is for the Calendar's events layer.
/// The flag is read at each check, so switching the area on takes effect the next day.
/// </para>
/// </summary>
public sealed class HarvestTaskWorker : IDisposable
{
    /// <summary>How long after the app starts the first check runs. Not zero: the first
    /// seconds of a launch belong to drawing the window.</summary>
    internal static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(30);

    /// <summary>A margin past midnight, so a timer that fires a few milliseconds early still
    /// finds the new day.</summary>
    private static readonly TimeSpan TimerMargin = TimeSpan.FromSeconds(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly IAppFeatureSettings? _features;
    private readonly ILogger _log;

    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _stopping;

    /// <summary>Guards <see cref="_timer"/> and <see cref="_disposed"/> together, so a check
    /// ending on a pool thread cannot arm a timer after the head disposed the worker.</summary>
    private readonly Lock _gate = new();
    private ITimer? _timer;
    private bool _disposed;

    public HarvestTaskWorker(
        IServiceScopeFactory scopes,
        TimeProvider time,
        IAppFeatureSettings? features = null,
        ILogger<HarvestTaskWorker>? log = null)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(time);

        _scopes = scopes;
        _time = time;
        _features = features;
        _log = log ?? NullLogger<HarvestTaskWorker>.Instance;
        _stopping = _lifetime.Token;

        Arm(StartDelay);
    }

    /// <summary>The check the timer last set off, for a test to await.</summary>
    internal Task LastCheck { get; private set; } = Task.CompletedTask;

    /// <summary>Stops the timer and asks a check in flight to stop.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }

        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void Arm(TimeSpan dueIn)
    {
        lock (_gate)
        {
            if (_disposed) return;

            _timer?.Dispose();

            // TimeProvider.CreateTimer so a test moves the clock rather than waiting for it.
            _timer = _time.CreateTimer(_ => LastCheck = CheckAsync(), state: null, dueIn, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>One check, which never throws: it runs on a pool thread with nobody to hand a
    /// failure to. Whatever happens, the timer is re-armed for the next local day.</summary>
    private async Task CheckAsync()
    {
        try
        {
            if (_features?.IsEnabled(EventsFeatures.Events) != true) return;

            var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

            var scope = _scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var filed = await scope.ServiceProvider.GetRequiredService<HarvestTaskPolicy>()
                    .FileDueAsync(today, _stopping)
                    .ConfigureAwait(false);

                if (filed > 0) _log.LogInformation("Filed {Count} harvest task(s) for events that ended before {Today}.", filed, today);
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // The app is closing. Nothing failed.
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "The harvest task check threw; it runs again when the local day turns.");
        }
        finally
        {
            Arm(UntilNextDay());
        }
    }

    /// <summary>From now to just past the next local midnight.</summary>
    private TimeSpan UntilNextDay()
    {
        var now = _time.GetLocalNow();
        var midnight = now.Date.AddDays(1);
        var next = new DateTimeOffset(midnight, _time.LocalTimeZone.GetUtcOffset(midnight));
        var wait = next - now;

        return (wait > TimeSpan.Zero ? wait : TimeSpan.Zero) + TimerMargin;
    }
}
