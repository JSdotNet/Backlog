namespace Backlog.Modules.Capture.Abstractions.Services;

/// <summary>
/// What the run chose not to deliver, per monitored target: the entries that
/// were already in a feed the first time the run looked at it, and the ones
/// beyond what one run takes from a target.
/// <para>
/// The run's one memory, and deliberately a narrow one. What the Inbox already
/// holds is still decided by id at delivery; this answers only "has the run
/// looked at this target before", and which entries it passed over there, so a
/// site whose feed lists every page it has is not news on the day it is added.
/// A port for the reason <see cref="ICaptureRunLog"/> is: where it is kept is
/// the host's business.
/// </para>
/// </summary>
public interface ICaptureTargetLedger
{
    /// <summary>The capture ids passed over at this target, or null when the
    /// run has never had an entry from it — which is what makes the next run
    /// its first look.</summary>
    IReadOnlySet<Guid>? PassedOverAt(CaptureSourceKind kind, string target);

    /// <summary>Replaces what is kept for this target with the whole set the
    /// run hands over. Never throws: a ledger that could not be saved costs a
    /// second first look, not the run.</summary>
    void Record(CaptureSourceKind kind, string target, IReadOnlyCollection<Guid> passedOver);
}
