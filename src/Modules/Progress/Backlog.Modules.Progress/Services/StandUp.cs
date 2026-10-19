using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.Features.ReadStandUp;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// The published <see cref="IStandUp"/> port over <see cref="ReadStandUpQueryHandler"/>,
/// always on as <see cref="Goals"/> is. "Today" is the host's clock in local time.
/// </summary>
public sealed class StandUp(
    ReadStandUpQueryHandler read,
    IIterationSettings iteration,
    TimeProvider clock)
    : IStandUp
{
    public async Task<Result<StandUpDto>> ReadAsync(DateOnly today, IterationLength length, CancellationToken cancellationToken = default) =>
        Result.Success(await read.Handle(new ReadStandUpQuery(today, length), cancellationToken).ConfigureAwait(false));

    public Task<Result<StandUpDto>> ReadAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(DateOnly.FromDateTime(clock.GetLocalNow().DateTime), iteration.Length, cancellationToken);
}

/// <summary>The module's own <see cref="IStandUpPace"/>: no pace known, so the stand-up
/// shows no capacity figure. A host that carries Roadmap Planning's pace replaces it.</summary>
public sealed class NoStandUpPace : IStandUpPace
{
    public Task<decimal?> PointsPerWeekAsync(CancellationToken cancellationToken = default) => Task.FromResult<decimal?>(null);
}

/// <summary>
/// The module's own <see cref="IIterationSettings"/>, held for the life of the host and
/// forgotten on restart: Week until chosen, and no two-week start until two weeks is first
/// chosen. A host that keeps the person's settings replaces it.
/// </summary>
public sealed class InMemoryIterationSettings : IIterationSettings
{
    private readonly Lock _gate = new();

    // Both choices as one snapshot, replaced whole, so a reader never pairs a length from
    // one change with a start from another.
    private volatile Snapshot _current = new(IterationLength.Week, null);

    public event Action? Changed;

    public IterationLength Length => _current.Length;

    public DateOnly? TwoWeekStart => _current.TwoWeekStart;

    public void SetLength(IterationLength length, DateOnly today)
    {
        if (!Enum.IsDefined(length)) throw new ArgumentOutOfRangeException(nameof(length), length, "An iteration is a day, a week, two weeks or a month.");

        Change(current => current with
        {
            Length = length,
            TwoWeekStart = length == IterationLength.TwoWeeks && current.TwoWeekStart is null
                ? Iterations.MondayOf(today)
                : current.TwoWeekStart
        });
    }

    public void SetTwoWeekStart(DateOnly start) => Change(current => current with { TwoWeekStart = start });

    private void Change(Func<Snapshot, Snapshot> change)
    {
        bool changed;
        lock (_gate)
        {
            var after = change(_current);
            changed = after != _current;
            _current = after;
        }

        if (changed) Changed?.Invoke();
    }

    private sealed record Snapshot(IterationLength Length, DateOnly? TwoWeekStart);
}
