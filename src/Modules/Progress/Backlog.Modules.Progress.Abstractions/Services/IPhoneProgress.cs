using Backlog.Modules.Progress.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Progress.Abstractions.Services;

/// <summary>The stand-up context as its document's text and the stamp it was written at.</summary>
/// <param name="Content">The <see cref="StandUpContextDto"/> as <see cref="ProgressDocuments.WriteContext"/> writes it.</param>
/// <param name="UpdatedAt">When what it says last changed — the stamp last-write-wins is decided on.</param>
public sealed record StandUpContextCopyDto(string Content, DateTimeOffset UpdatedAt);

/// <summary>
/// What the sync client sends the phone so it can read the stand-up without GitHub
/// (<c>.devbook/domain/progress/features.md#goals-on-the-phone</c>): the
/// <see cref="StandUpContextDto"/> this desktop reads now. The stamp moves only when what the
/// document says changes, so an unchanged reading is not sent again.
/// </summary>
public interface IStandUpContextSource
{
    /// <summary>The context as it reads now, or null when there is nothing to send: no
    /// goal is set, or the <see cref="ProgressFeatures.Goals"/> switch is off on this desktop.</summary>
    Task<StandUpContextCopyDto?> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>What <see cref="IImpedimentEditIntake.ApplyAsync"/> did with one edit the phone asked for.</summary>
public enum ImpedimentEditOutcome
{
    /// <summary>The impediment was changed and saved.</summary>
    Applied,

    /// <summary>The impediment already says what the edit asks for. Nothing was written.</summary>
    Unchanged,

    /// <summary>The field no longer says what the phone saw — it was changed elsewhere since —
    /// so the newer change stands and the edit was dropped.</summary>
    Stale,

    /// <summary>The impediment refused the change — a move its states do not allow, such as
    /// one out of Resolved. Nothing was written.</summary>
    Refused,

    /// <summary>This device holds no impediment by that id. Nothing was written.</summary>
    Unknown
}

/// <summary>
/// Where the sync client hands an <c>impediment-edit</c> document the phone wrote: one change
/// to an impediment's state, check-again date or note, applied through the module's own
/// impediment features (<c>.devbook/domain/progress/features.md#goals-on-the-phone</c>, local
/// ADR 0035 §8). Not behind the <see cref="ProgressFeatures.Goals"/> switch, for the reason
/// replication is not: what the person asked for while it is off is kept.
/// </summary>
public interface IImpedimentEditIntake
{
    /// <summary>Applies <paramref name="edit"/> today, while the field it changes still says
    /// what the phone saw (<see cref="ImpedimentEditDto"/>'s <c>Was</c> values).</summary>
    Task<ImpedimentEditOutcome> ApplyAsync(ImpedimentEditDto edit, CancellationToken cancellationToken = default);
}
