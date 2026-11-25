using System.Globalization;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Backlog.Infrastructure.FileSystem.Tasks;

/// <summary>
/// The Tasks pane's <see cref="IPlanEntryStages"/> (local ADR 0062 §2), answered out of the
/// delivery runs this machine recorded: each run under way or parked that names a Backlog
/// entry by its stored id gives that entry the stage it is on. A cross-context join, here
/// for the reason <see cref="SessionQueueAttentionReader"/> is: the Tasks pane may not
/// reference Sessions. A mapping and nothing else.
/// </summary>
/// <param name="runs">This machine's delivery runs, or null when none is composed.</param>
/// <param name="changes">The signal a run write raises, or null when none is composed.</param>
/// <param name="logger">Where an unreadable source is said; nothing when null.</param>
public sealed class PlanEntryStageReader(
    IDeliveryRunSource? runs,
    IDeliveryRunChangeSignal? changes,
    ILogger<PlanEntryStageReader>? logger = null) : IPlanEntryStages
{
    private static readonly IReadOnlyDictionary<Guid, string> Nothing = new Dictionary<Guid, string>();

    public event Action? Changed
    {
        add { if (changes is not null) changes.Changed += value; }
        remove { if (changes is not null) changes.Changed -= value; }
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (runs is null) return Nothing;

        try
        {
            var catalog = await runs.GetRunsAsync(cancellationToken).ConfigureAwait(false);

            return StagesOf(catalog.Runs);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogWarning(exception, "The delivery runs could not be read for the plan entries' stages.");
            return Nothing;
        }
    }

    /// <summary>
    /// The stage of each entry a run reports, by entry id. Only a run under way or parked
    /// counts — a finished one has nothing left to be at — and only a reference that names
    /// the entry by id, never by label. Where two runs name one entry the most recently
    /// updated wins. A run with no stages, or none it is at, reports nothing.
    /// </summary>
    public static IReadOnlyDictionary<Guid, string> StagesOf(IEnumerable<DeliveryRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var stages = new Dictionary<Guid, string>();

        foreach (var run in runs.OrderByDescending(run => run.UpdatedAt))
        {
            if (StageOf(run) is not { } stage) continue;

            foreach (var reference in run.References)
            {
                if (reference is { Kind: DeliveryRunReferenceKind.Task, EntryId: { } id }) stages.TryAdd(id, stage);
            }
        }

        return stages;
    }

    /// <summary>
    /// Where one run stands, as <c>Review · 8 of 9</c>: the stage it is on and its place
    /// among the run's stages, or null when the run is finished or is at no stage. A run
    /// under way is at its stage in progress, else its first not yet reached; a parked one
    /// at its blocked stage, else its first not done.
    /// </summary>
    public static string? StageOf(DeliveryRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var stages = run.Stages;
        if (stages.Count == 0) return null;

        int at;

        if (run.InProgress)
        {
            at = IndexOf(stages, stage => Is(stage, DeliveryStageStatuses.InProgress));
            if (at < 0) at = IndexOf(stages, stage => Is(stage, DeliveryStageStatuses.Pending));
        }
        else if (string.Equals(run.Status, DeliveryRunStatuses.Parked, StringComparison.OrdinalIgnoreCase))
        {
            at = IndexOf(stages, stage => Is(stage, DeliveryStageStatuses.Blocked));
            if (at < 0) at = IndexOf(stages, stage => !Is(stage, DeliveryStageStatuses.Done) && !Is(stage, DeliveryStageStatuses.Skipped));
        }
        else
        {
            return null;
        }

        if (at < 0) return null;

        var name = string.IsNullOrWhiteSpace(stages[at].Name)
            ? $"Stage {(at + 1).ToString(CultureInfo.InvariantCulture)}"
            : stages[at].Name.Trim();

        return $"{name} · {(at + 1).ToString(CultureInfo.InvariantCulture)} of {stages.Count.ToString(CultureInfo.InvariantCulture)}";
    }

    private static bool Is(DeliveryRunStage stage, string status) =>
        string.Equals(stage.Status, status, StringComparison.OrdinalIgnoreCase);

    private static int IndexOf(IReadOnlyList<DeliveryRunStage> stages, Func<DeliveryRunStage, bool> predicate)
    {
        for (var index = 0; index < stages.Count; index++)
        {
            if (predicate(stages[index])) return index;
        }

        return -1;
    }
}
