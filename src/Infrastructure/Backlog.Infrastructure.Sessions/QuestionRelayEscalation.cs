using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// The question relay's one scheduled step (local ADR 0041 §1): every
/// <see cref="Interval"/> it publishes the waiting questions whose notify delay has passed
/// while they are still InSession — a dialog left open in the session included.
/// <para>
/// A plain singleton the head resolves once at startup, as <c>BlockedTaskWatcher</c> is:
/// neither desktop head has a generic host to start an <c>IHostedService</c>. A tick that
/// finds the previous one still running is skipped, and a tick that fails is logged and
/// never stops the timer.
/// </para>
/// </summary>
public sealed class QuestionRelayEscalation : IDisposable
{
    /// <summary>How often the relay looks for a question whose delay has passed: well
    /// under the shortest "notify after" setting of one minute.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly QuestionRelay _relay;
    private readonly ILogger _logger;
    private readonly ITimer _timer;
    private readonly Lock _gate = new();
    private Task _running = Task.CompletedTask;

    public QuestionRelayEscalation(QuestionRelay relay, TimeProvider? clock = null, ILogger<QuestionRelayEscalation>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(relay);

        _relay = relay;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _timer = (clock ?? TimeProvider.System).CreateTimer(_ => Tick(), null, Interval, Interval);
    }

    /// <summary>The tick under way, or the last one — for a test to await.</summary>
    public Task Idle
    {
        get
        {
            lock (_gate) return _running;
        }
    }

    public void Dispose() => _timer.Dispose();

    private void Tick()
    {
        lock (_gate)
        {
            if (!_running.IsCompleted) return;

            _running = Task.Run(RunAsync);
        }
    }

    private async Task RunAsync()
    {
        try
        {
            var published = await _relay.EscalateDueAsync().ConfigureAwait(false);

            if (published > 0) _logger.LogDebug("Published {Count} waiting question(s) whose notify delay passed.", published);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Publishing the waiting questions whose notify delay passed failed; the next tick tries again.");
        }
    }
}
