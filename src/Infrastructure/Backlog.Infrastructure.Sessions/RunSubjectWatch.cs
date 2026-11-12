using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// Closes an open Personal Validation question once the run it waits on ended while the app
/// runs (<c>.devbook/domain/notifications/domain.md#subject</c>, <c>sessions / run</c>): every
/// <see cref="Interval"/> it lists the open questions this owner holds, and asks the Sessions
/// probe (<see cref="SessionSubjectProbe"/>) about each one's run — whose pull request may
/// have merged or closed, or whose sessions may all be archived, without the run file ever
/// finishing. The startup pass asks the same probe once; this asks it again while the app runs,
/// so a question about a merged pull request does not stay open for days.
/// <para>
/// It reads nothing while no validation question is open, and the pull request reads go
/// through Repository Management's one cache, so it adds no GitHub load beyond what the
/// Sessions pane causes. A reading that fails, or answers nothing, leaves the question open,
/// and one that outlasts <see cref="ReadBound"/> is abandoned for the tick, so a read that
/// never answers cannot stop every later tick.
/// </para>
/// <para>
/// A plain singleton the head resolves once at startup, as <see cref="QuestionRelayEscalation"/>
/// is: neither desktop head has a generic host to start an <c>IHostedService</c>. A tick that
/// finds the previous one still running is skipped, and a tick that fails is logged and never
/// stops the timer.
/// </para>
/// </summary>
public sealed class RunSubjectWatch : IDisposable
{
    /// <summary>How often it looks: a question about a run that ended is moot, not urgent.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    /// <summary>How long one tick waits on its readings, all of them together.</summary>
    public static readonly TimeSpan ReadBound = TimeSpan.FromMinutes(1);

    private readonly Func<INotificationReplication?> _open;
    private readonly INotificationSubjectProbe _probe;
    private readonly Func<INotifications?> _notifications;
    private readonly ILogger _logger;
    private readonly TimeProvider _clock;
    private readonly ITimer _timer;
    private readonly Lock _gate = new();
    private Task _running = Task.CompletedTask;

    /// <param name="open">The owner's open notifications; null on a host that owns none.</param>
    /// <param name="probe">The Sessions context's subject probe.</param>
    /// <param name="notifications">Where a moot question is closed; null on a host with none.</param>
    public RunSubjectWatch(
        Func<INotificationReplication?> open,
        INotificationSubjectProbe probe,
        Func<INotifications?> notifications,
        TimeProvider? clock = null,
        ILogger<RunSubjectWatch>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(notifications);

        _open = open;
        _probe = probe;
        _notifications = notifications;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _clock = clock ?? TimeProvider.System;
        _timer = _clock.CreateTimer(_ => Tick(), null, Interval, Interval);
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
        using var bound = new CancellationTokenSource(ReadBound, _clock);

        try
        {
            if (_open() is not { } open) return;

            var runs = (await open.ListOpenAsync(bound.Token).ConfigureAwait(false))
                .Where(IsValidationQuestion)
                .Select(notification => notification.Subject!)
                .Distinct()
                .ToList();

            foreach (var run in runs)
            {
                await CloseWhenEndedAsync(run, bound.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Looking for validation questions whose run ended failed; the next tick tries again.");
        }
    }

    /// <summary>One run's question closes when the probe says the run ended, with the reason
    /// it names. A probe that fails leaves it open for the next tick.</summary>
    private async Task CloseWhenEndedAsync(NotificationSubject run, CancellationToken bound)
    {
        string? reason;

        try
        {
            if (await _probe.IsLiveAsync(run, bound).ConfigureAwait(false)) return;

            reason = await _probe.EndReasonAsync(run, bound).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (bound.IsCancellationRequested)
        {
            _logger.LogDebug(exception, "Whether run {Run} ended was not read within {Bound}; its validation question stays open until the next tick.", run.Id, ReadBound);
            return;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Whether run {Run} ended could not be read; its validation question stays open.", run.Id);
            return;
        }

        await SessionSubjectEnds.CloseAsync(
            _notifications,
            run,
            string.IsNullOrWhiteSpace(reason) ? SessionNotificationSubjects.RunEndedReason : reason,
            _logger,
            CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>A run's Personal Validation question, as <see cref="ValidationQuestions"/>
    /// publishes it: a question from Sessions about a run, its source keyed by run and
    /// worktree — not a relayed session question, nor an alert about the run.</summary>
    private static bool IsValidationQuestion(NotificationSnapshot notification) =>
        notification.Kind is NotificationKind.Question
        && notification.Subject is { Context: SessionNotificationSubjects.Context, Type: SessionNotificationSubjects.Run }
        && string.Equals(notification.Source.Context, SessionNotificationSubjects.Context, StringComparison.Ordinal)
        && SessionNotificationSubjects.ParseValidationSourceKey(notification.Source.Key) is not null;
}
