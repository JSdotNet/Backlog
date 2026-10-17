using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// The one place a saved impediment is announced, on the terms <see cref="GoalSaved"/>
/// gives a goal: every change that saves one raises it once the gate is let go, and a
/// pulled copy raises it too. A singleton, because the commands run in scopes of their own.
/// </summary>
public sealed class ImpedimentSaved
{
    public event Action? Changed;

    public void Raise() => Changed?.Invoke();
}

/// <summary>From the aggregate to what a caller receives.</summary>
public static class ImpedimentMapper
{
    public static ImpedimentDto ToDto(this Impediment impediment)
    {
        ArgumentNullException.ThrowIfNull(impediment);

        return new ImpedimentDto(
            impediment.Id,
            impediment.Title,
            impediment.State,
            impediment.ReportedTo,
            impediment.HandledBy,
            impediment.CheckAgain,
            impediment.Note,
            [.. impediment.History.Select(step => new ImpedimentStateChangeDto(step.State, step.On, step.With))],
            [.. impediment.HoldsBack]);
    }
}

/// <summary>
/// The load–change–save every command over impediments shares, as <see cref="GoalChanges"/>
/// is for goals: under the <see cref="ProgressGate"/>, load, apply the change, and save only
/// when it succeeded — a refused change leaves the stored impediment as it was.
/// </summary>
public sealed class ImpedimentChanges(IImpedimentRepository repository, ProgressGate gate, ImpedimentSaved saved)
{
    /// <summary>Changes impediment <paramref name="impedimentId"/>.</summary>
    public async Task<Result<ImpedimentDto>> ChangeAsync(
        Guid impedimentId,
        Func<Impediment, Result> change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        using var held = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        var impediment = await repository.GetAsync(impedimentId, cancellationToken).ConfigureAwait(false);
        if (impediment is null) return ProgressErrors.ImpedimentNotFound(impedimentId);

        var changed = change(impediment);
        if (changed.IsFailure) return changed.Error;

        await repository.SaveAsync(impediment, cancellationToken).ConfigureAwait(false);
        held.Dispose(); // Let go first, so nothing that hears the notice waits on this write.
        saved.Raise();
        return impediment.ToDto();
    }

    /// <summary>Saves a new impediment under the gate and announces it.</summary>
    public async Task<Result<ImpedimentDto>> AddAsync(Impediment impediment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(impediment);

        using var held = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await repository.SaveAsync(impediment, cancellationToken).ConfigureAwait(false);
        held.Dispose();
        saved.Raise();
        return impediment.ToDto();
    }

    /// <summary>
    /// Reads every impediment and saves the new ones <paramref name="raise"/> answers, in
    /// one hold of the gate, so two runs of the blocked-task policy can never both decide
    /// that a task needs one. Announces once when it saved anything.
    /// </summary>
    public async Task<IReadOnlyList<ImpedimentDto>> RaiseAllAsync(
        Func<IReadOnlyList<Impediment>, IReadOnlyList<Impediment>> raise,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(raise);

        using var held = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        var all = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var raised = raise(all);

        foreach (var impediment in raised)
        {
            await repository.SaveAsync(impediment, cancellationToken).ConfigureAwait(false);
        }

        held.Dispose();
        if (raised.Count > 0) saved.Raise();

        return [.. raised.Select(impediment => impediment.ToDto())];
    }

    /// <summary>Every impediment, read under the gate.</summary>
    public async Task<IReadOnlyList<Impediment>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var held = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        return await repository.ListAsync(cancellationToken).ConfigureAwait(false);
    }
}
