using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>Schedule readings built by hand, for the Schedules page and its rules: the
/// states come as given rather than through <see cref="ScheduleReading"/>, whose own tests
/// decide them.</summary>
internal static class ScheduleFixtures
{
    /// <summary>Wednesday 11 November 2026, 07:12 UTC.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 11, 7, 12, 0, TimeSpan.Zero);

    public static ScheduledRunReading Run(
        string session,
        ScheduledRunState state,
        DateTimeOffset began,
        string? verdict = null,
        IReadOnlyList<string>? needsYou = null,
        bool skipped = false,
        string? heldBy = null,
        bool failed = false,
        bool finished = true) =>
        new(
            new ScheduledRun(
                session,
                "schedule",
                began,
                began,
                finished && !skipped ? began.AddMinutes(20) : null,
                verdict,
                needsYou ?? [],
                failed,
                skipped,
                heldBy),
            state);

    public static ScheduleReadingResult Schedule(
        string id,
        string? title,
        string? repository,
        ScheduledRunState? state,
        string? cron = "30 5 * * 1-5",
        DateTimeOffset? nextRunAt = null,
        DateTimeOffset? dueAt = null,
        params ScheduledRunReading[] runs) =>
        new(
            new Schedule(id, title, repository, cron, Now.AddDays(-30), Now.AddHours(-1)),
            state,
            [.. runs.Select(run => run with { Run = run.Run with { ScheduleId = id } })],
            dueAt,
            nextRunAt ?? Now.AddDays(1));
}
