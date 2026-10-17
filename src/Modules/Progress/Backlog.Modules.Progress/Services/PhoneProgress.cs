using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.Features.TrackImpediment;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// The published <see cref="IStandUpContextSource"/>: every pull request the goals count,
/// read through <see cref="IGoalPullRequestReader"/> and its cache, with the pace, the
/// working week and the iteration choice, as the phone needs them to read the stand-up
/// (local ADR 0035 §8). A singleton, because it keeps the last reading to tell a changed one
/// from a repeat; what it reads through is scoped, so each reading opens a scope of its own.
/// </summary>
public sealed class StandUpContextSource(
    IServiceScopeFactory scopes,
    IGoalRepository goals,
    IIterationSettings iteration,
    TimeProvider clock,
    IAppFeatureSettings? features = null,
    IWorkingHoursSettings? workingHours = null)
    : IStandUpContextSource
{
    private readonly Lock _gate = new();
    private StandUpContextCopyDto? _last;
    private string? _lastSays;

    public async Task<StandUpContextCopyDto?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!(features?.IsEnabled(ProgressFeatures.Goals) ?? false)) return null;

        IReadOnlyList<GoalDto> all = [.. (await goals.ListAsync(cancellationToken).ConfigureAwait(false)).Select(goal => goal.ToDto())];
        if (all.Count == 0) return null;

        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        var tasks = services.GetRequiredService<ISprintTaskSource>();
        var pullRequests = services.GetRequiredService<IGoalPullRequestReader>();
        var pace = services.GetService<IStandUpPace>();

        var read = await tasks.ReadAsync(GoalItems.TaskQuery(all), cancellationToken).ConfigureAwait(false);
        var asked = GoalItems.PullRequestsOf(all, read);
        var answers = asked.Count == 0
            ? []
            : await pullRequests.ReadAsync(asked, cancellationToken).ConfigureAwait(false);
        var points = pace is null ? null : await pace.PointsPerWeekAsync(cancellationToken).ConfigureAwait(false);

        var context = new StandUpContextDto(
            DateTimeOffset.MinValue,
            [.. answers.OrderBy(pull => GoalItems.Key(pull.Repository, pull.Number), StringComparer.OrdinalIgnoreCase)],
            points,
            workingHours?.Current,
            iteration.Length,
            iteration.TwoWeekStart);

        // What it says, without when it was read: the same reading is the same document.
        var says = ProgressDocuments.WriteContext(context);

        lock (_gate)
        {
            if (_last is not null && string.Equals(says, _lastSays, StringComparison.Ordinal)) return _last;

            var now = clock.GetUtcNow();
            _last = new StandUpContextCopyDto(ProgressDocuments.WriteContext(context with { ReadAt = now }), now);
            _lastSays = says;
            return _last;
        }
    }
}

/// <summary>
/// The published <see cref="IImpedimentEditIntake"/>: one edit the phone asked for, handed to
/// <see cref="ImpedimentCommandHandler"/> — the impediment features a person's own changes on
/// the desktop go through — as one compare-and-set under the progress gate. A singleton over
/// a scope per edit, for the reason <see cref="StandUpContextSource"/> gives.
/// </summary>
public sealed class ImpedimentEditIntake(IServiceScopeFactory scopes) : IImpedimentEditIntake
{
    public async Task<ImpedimentEditOutcome> ApplyAsync(ImpedimentEditDto edit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ImpedimentCommandHandler>()
            .Handle(new ApplyImpedimentEditCommand(edit), cancellationToken)
            .ConfigureAwait(false);
    }
}
