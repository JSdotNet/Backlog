using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Modules.Tasks.Features.CompleteLinkedTask;

/// <summary>
/// Finishes a linked task's item at its source, because the person finished the
/// task here: GitHub closes the issue as completed, spec-manager moves the item to
/// the product's first end status. The write-back ADR 0020 planned as a later
/// slice behind a setting (Consequences, §8).
/// <para>
/// Asked by <see cref="LinkedTaskWriteBack"/> after a save took the task to Done,
/// never by the sync: a Done the source brought is already Done there.
/// </para>
/// </summary>
public sealed record CompleteLinkedTaskCommand(Guid TaskId);

/// <summary>What one write-back did.</summary>
public enum LinkedTaskWriteBackOutcome
{
    /// <summary>The source was not asked: the task is not a linked task that is
    /// done here and open there, its target is not connected or has
    /// <see cref="ConnectedTarget.CompleteAtSource"/> off, or its connector is not
    /// installed or cannot complete.</summary>
    NotAsked,

    /// <summary>The source finished the item.</summary>
    Completed,

    /// <summary>The source refused, and the task says why.</summary>
    Refused,
}

/// <summary>
/// Asks the task's connector to finish its item, and records a refusal on the task.
/// <para>
/// <b>When the source is asked.</b> Only when every one of these holds when the
/// handler runs, not when the save asked: the task is a linked task and is Done;
/// the source state the last sync recorded is neither Done nor Dropped, so the item
/// is still open there as far as anything here knows; the connector by the stored
/// id is installed and says <see cref="TaskConnectorCapabilities.CanComplete"/>;
/// and the target the task came through is connected with
/// <see cref="ConnectedTarget.CompleteAtSource"/> on. The target need not be
/// <see cref="ConnectedTarget.Enabled"/>: switching the sync off stops new items
/// arriving, and says nothing about whether the person still wants the ones they
/// finish closed — the opt-in for that is its own switch.
/// </para>
/// <para>
/// <b>What is written.</b> The source reference's
/// <see cref="SourceRef.WriteBackRefusal"/> and nothing else. The status stays where
/// the person put it whatever the source answers, and the normalised state is left
/// for the sync to record, because a fetch that lags behind the write would
/// otherwise read as the source reopening the item. The task is read again after
/// the source answers, so an edit the person made while the request was out is
/// kept, and a task reopened meanwhile is not given a refusal it no longer has a
/// use for.
/// </para>
/// <para>
/// <b>What a refusal says.</b> The source's answer, or the message of what it threw,
/// cut to its first line and to <see cref="RefusalLimit"/> characters: it is shown on
/// a row's metadata line, and a stack of text from a source is not a reason. A
/// cancellation the caller did not ask for is a source that did not answer in time —
/// an <c>HttpClient</c> timeout throws one — and is a refusal too; one the
/// caller asked for records nothing.
/// </para>
/// <para>
/// A success is answered with <see cref="LinkedTaskWriteBackOutcome.Completed"/> and
/// nothing more: the sync that brings the source's Done in is the requester's to ask
/// for, once for a burst of completions rather than once for each.
/// </para>
/// </summary>
public sealed class CompleteLinkedTaskCommandHandler(
    IEnumerable<ITaskConnector> connectors,
    ITaskRepository tasks,
    IConnectedTargets targets,
    ILogger<CompleteLinkedTaskCommandHandler>? log = null)
    : ICommandHandler<CompleteLinkedTaskCommand, Result<LinkedTaskWriteBackOutcome>>
{
    /// <summary>The longest refusal recorded, in characters, ellipsis included.</summary>
    internal const int RefusalLimit = 300;

    /// <summary>What a source that did not answer in time is recorded as.</summary>
    internal const string TimedOut = "The source did not answer in time; the item may not have been completed there.";

    private readonly ILogger _log = log ?? NullLogger<CompleteLinkedTaskCommandHandler>.Instance;

    public async Task<Result<LinkedTaskWriteBackOutcome>> Handle(
        CompleteLinkedTaskCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var task = await tasks.GetAsync(command.TaskId, cancellationToken);
        if (task?.SourceRef is not { } source || task.Status != EntryStatus.Done) return LinkedTaskWriteBackOutcome.NotAsked;
        if (source.NormalisedState is NormalisedSourceState.Done or NormalisedSourceState.Dropped) return LinkedTaskWriteBackOutcome.NotAsked;

        var connector = connectors.FirstOrDefault(candidate =>
            string.Equals(candidate.Descriptor.Id, source.ConnectorId, StringComparison.Ordinal));
        if (connector is null || !connector.Capabilities.CanComplete) return LinkedTaskWriteBackOutcome.NotAsked;

        if (targets.Get(source.ConnectorId, source.Target) is not { CompleteAtSource: true }) return LinkedTaskWriteBackOutcome.NotAsked;

        string? refusal;
        try
        {
            refusal = await connector.CompleteAsync(source, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A connector should answer a refusal rather than throw one, but a source
            // that could not be reached is a refusal all the same: the person needs to
            // see that their item is still open there. A cancellation nobody here asked
            // for is a timeout, and the source may have acted on the request regardless.
            _log.LogWarning(ex, "Completing {Connector} item {Item} at the source threw.", source.ConnectorId, source.DisplayKey);
            refusal = ex is OperationCanceledException ? TimedOut
                : string.IsNullOrWhiteSpace(ex.Message) ? "The source could not be reached."
                : ex.Message;
        }

        refusal = Shorten(refusal);

        await RecordAsync(command.TaskId, source, refusal, cancellationToken);

        if (refusal is not null)
        {
            _log.LogInformation("{Connector} refused to complete {Item}: {Refusal}", source.ConnectorId, source.DisplayKey, refusal);
            return LinkedTaskWriteBackOutcome.Refused;
        }

        return LinkedTaskWriteBackOutcome.Completed;
    }

    /// <summary>The first line of <paramref name="refusal"/>, trimmed, and cut to
    /// <see cref="RefusalLimit"/> with an ellipsis; null for none. A refusal that is
    /// blank on its first line is read from its first line that is not.</summary>
    internal static string? Shorten(string? refusal)
    {
        if (refusal is null) return null;

        var line = refusal
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrEmpty(line)) return "The source refused.";

        return line.Length <= RefusalLimit ? line : string.Concat(line.AsSpan(0, RefusalLimit - 1).TrimEnd(), "…");
    }

    /// <summary>
    /// Writes the answer onto the task as it is now, not as it was read before the
    /// source was asked. A task deleted meanwhile, or no longer following the same
    /// item, is left alone; a refusal is not recorded on a task the person has
    /// reopened, because reopening is what clears one.
    /// </summary>
    private async Task RecordAsync(Guid taskId, SourceRef asked, string? refusal, CancellationToken cancellationToken)
    {
        var current = await tasks.GetAsync(taskId, cancellationToken);
        if (current?.SourceRef is not { } held) return;
        if (!string.Equals(held.ConnectorId, asked.ConnectorId, StringComparison.Ordinal)
            || !string.Equals(held.ExternalId, asked.ExternalId, StringComparison.Ordinal))
        {
            return;
        }

        if (refusal is not null && current.Status != EntryStatus.Done) return;

        var recorded = held with { WriteBackRefusal = refusal };

        // Nothing to say is nothing to save: a success with no refusal to clear must
        // not restamp the task, or this machine would win last-write-wins for nothing.
        if (Equals(recorded, held)) return;

        current.SetSourceRef(recorded);
        await tasks.SaveAsync(current, cancellationToken);
    }
}
