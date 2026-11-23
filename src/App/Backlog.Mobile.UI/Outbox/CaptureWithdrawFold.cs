using Backlog.Mobile.UI.TalkNotes;

namespace Backlog.Mobile.UI.Outbox;

/// <summary>
/// A capture the service refused leaves the outbox, unsent, with the withdrawal of
/// it waiting behind it.
/// <para>
/// A withdrawal is queued behind a capture only while the capture is being sent
/// (<see cref="CaptureEdits"/>). When that send ends in a refusal the service never
/// had the capture — so the withdrawal, sent on its own, would find nothing (404
/// <c>inbox.item_not_found</c>), count as delivered, tell the person the desktop had
/// dealt with it, and leave the capture they deleted on the phone, set aside.
/// Folded, the person's Delete is done on the phone, as it is for a capture that
/// never left: the capture and the withdrawal go, neither sent, and a talk note's
/// files with them.
/// </para>
/// <para>
/// The withdrawal wins over any edit of the capture waiting with it — there is
/// nothing left for an edit to change — so this fold takes those in too, and is
/// asked before <see cref="CaptureEditFold"/>.
/// </para>
/// </summary>
public sealed class CaptureWithdrawFold(TalkNoteFiles files) : IOutboxFold
{
    public OutboxFold? Fold(OutboxEntry setAside, IReadOnlyList<OutboxEntry> waiting)
    {
        ArgumentNullException.ThrowIfNull(setAside);
        ArgumentNullException.ThrowIfNull(waiting);

        if (!CaptureEdits.IsCapture(setAside)) return null;

        var withdrawn = waiting.Any(entry =>
            entry.Kind == CaptureWithdrawOutboxKind.Token && CaptureEdits.ReadWithdrawal(entry)?.CaptureId == setAside.Id);

        if (!withdrawn) return null;

        var absorbed = waiting
            .Where(entry => entry.Kind switch
            {
                CaptureWithdrawOutboxKind.Token => CaptureEdits.ReadWithdrawal(entry)?.CaptureId == setAside.Id,
                CaptureEditOutboxKind.Token => CaptureEdits.ReadEdit(entry)?.CaptureId == setAside.Id,
                _ => false,
            })
            .Select(entry => entry.Id)
            .ToList();

        return new OutboxFold(PayloadJson: null, absorbed);
    }

    public void Removed(OutboxEntry setAside) => CaptureEdits.ReleaseFiles(setAside, files);
}
