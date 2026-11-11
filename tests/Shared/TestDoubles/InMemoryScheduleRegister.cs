using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Tests;

/// <summary>A Schedule Register held in memory, applying <see cref="ScheduleReports.Apply"/>
/// the way the SQLite register applies it in one write.</summary>
internal sealed class InMemoryScheduleRegister : IScheduleRegister
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Schedule> _schedules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScheduledRun> _runs = new(StringComparer.Ordinal);

    /// <summary>How many reports were recorded.</summary>
    public int Recorded { get; private set; }

    /// <summary>When set, every call throws it, for a register that cannot be written.</summary>
    public Exception? Failure { get; set; }

    public Task<ScheduleRecord> RecordAsync(ScheduledRunReport report, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (Failure is not null) throw Failure;

        lock (_gate)
        {
            var held = _runs.GetValueOrDefault(report.SessionId);
            var applied = held is null || held.ScheduleId == report.ScheduleId
                ? report
                : report with { ScheduleId = held.ScheduleId, Title = null, Repository = null, Cron = null };
            var (schedule, run) = ScheduleReports.Apply(_schedules.GetValueOrDefault(applied.ScheduleId), held, applied, at);

            _schedules[schedule.Id] = schedule;
            _runs[run.SessionId] = run;
            Recorded++;

            return Task.FromResult(Record(schedule));
        }
    }

    public Task<IReadOnlyList<ScheduleRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (Failure is not null) throw Failure;

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ScheduleRecord>>([.. _schedules.Values.OrderBy(schedule => schedule.Id, StringComparer.Ordinal).Select(Record)]);
        }
    }

    private ScheduleRecord Record(Schedule schedule) =>
        new(schedule, [.. _runs.Values.Where(run => run.ScheduleId == schedule.Id).OrderBy(run => run.FirstReportedAt)]);
}
