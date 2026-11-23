namespace Backlog.Mobile.UI.Outbox;

/// <summary>
/// A capture the service refused takes in the edits of it waiting behind it.
/// <para>
/// An edit is queued behind a capture only while the capture is being sent
/// (<see cref="CaptureEdits"/>). When that send ends in a refusal the capture
/// stays on the phone, set aside, and the service never had it — so the edit,
/// sent on its own, would find nothing (404 <c>inbox.item_not_found</c>), count
/// as delivered and tell the person the desktop had dealt with it. Folded, the
/// last edit's title and body become the capture's, the edits leave unsent, and
/// retrying the capture sends the new words.
/// </para>
/// <para>
/// The capture stays set aside with the service's reason: the person reworded it
/// before hearing of the refusal, so the new words are not yet their answer to it.
/// A reword after the refusal is (<see cref="CaptureEdits.EditAsync"/> sends it again).
/// </para>
/// </summary>
public sealed class CaptureEditFold : IOutboxFold
{
    public OutboxFold? Fold(OutboxEntry setAside, IReadOnlyList<OutboxEntry> waiting)
    {
        ArgumentNullException.ThrowIfNull(setAside);
        ArgumentNullException.ThrowIfNull(waiting);

        if (!CaptureEdits.IsCapture(setAside)) return null;

        var edits = waiting
            .Where(entry => entry.Kind == CaptureEditOutboxKind.Token)
            .Select(entry => (entry.Id, Edit: CaptureEdits.ReadEdit(entry)))
            .Where(edit => edit.Edit?.CaptureId == setAside.Id)
            .ToList();

        if (edits.Count == 0) return null;

        var last = edits[^1].Edit!;
        return new OutboxFold(
            CaptureEdits.Reworded(setAside, last.Title, last.BodyMd),
            [.. edits.Select(edit => edit.Id)]);
    }
}
