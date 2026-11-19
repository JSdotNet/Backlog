using Backlog.Mobile.UI.TalkNotes;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Outbox;

/// <summary>
/// The Inbox tab's two changes to one of the phone's captures until the desktop
/// decides it: rewording it, and deleting it. The one service the tab's sheet
/// calls; it decides where each change goes and keeps no state of its own.
/// <para>
/// <b>Still waiting in the outbox</b> — a capture or talk note not yet sent and
/// not being sent — the change is made where it waits: the payload rewritten, or
/// the entry taken out, so the service only ever hears the last word, and a
/// capture deleted before it left never reaches it. <b>Already left, or leaving
/// right now,</b> the change is queued behind it as an entry of its own
/// (<see cref="CaptureEditOutboxKind"/>, <see cref="CaptureWithdrawOutboxKind"/>),
/// so it arrives after the capture and survives having no signal. Queuing behind
/// a capture still waiting would let a set-aside capture be overtaken by its own
/// edit; changing it in place is what avoids that.
/// </para>
/// <para>
/// The list shows a change at once: <see cref="Overlay"/> lays every change still
/// in the outbox over what the service last said, and
/// <see cref="IsChangeWaiting"/> says the row is still waiting for it.
/// </para>
/// </summary>
public sealed class CaptureEdits(DeviceOutbox outbox, TalkNoteFiles files)
{
    /// <summary>
    /// Rewords the capture <paramref name="captureId"/>: a new title, and a body,
    /// null for none. Rewrites the capture where it waits, or the edit of it
    /// still waiting, and otherwise queues an edit. A blank title is not an edit —
    /// a capture cannot be without one — and is refused; an auto-save should not
    /// call this until there is a title.
    /// </summary>
    public async Task EditAsync(Guid captureId, string title, string? bodyMd, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var entries = outbox.Entries;

        if (entries.FirstOrDefault(entry => entry.Id == captureId && IsCapture(entry)) is { } waiting
            && await outbox.ReplacePayloadAsync(captureId, Reworded(waiting, title, bodyMd), cancellationToken))
        {
            await RequeueIfSetAsideAsync(waiting, cancellationToken);
            return;
        }

        var edit = new CaptureEdit(captureId, title, bodyMd);

        // One pause, one entry: the edit before this one has not left yet, so it
        // carries these words instead.
        foreach (var queued in entries.Where(entry => entry.Kind == CaptureEditOutboxKind.Token && Names(entry, captureId)))
        {
            if (await outbox.ReplacePayloadAsync(queued.Id, CaptureEditOutboxKind.Write(edit), cancellationToken))
            {
                await RequeueIfSetAsideAsync(queued, cancellationToken);
                return;
            }
        }

        await outbox.EnqueueAsync(CaptureEditOutboxKind.Token, Guid.CreateVersion7(), CaptureEditOutboxKind.Write(edit), cancellationToken);
    }

    /// <summary>
    /// Deletes the capture <paramref name="captureId"/> from the phone and the
    /// inbox. Takes it out of the outbox where it waits — with a talk note's
    /// files — and otherwise queues one withdrawal, which the desktop reads as
    /// the capture dismissed and archives. Any edit of it still waiting is
    /// dropped: there is nothing left for it to change.
    /// </summary>
    public async Task WithdrawAsync(Guid captureId, CancellationToken cancellationToken = default)
    {
        var entries = outbox.Entries;

        foreach (var queued in entries.Where(entry => entry.Kind == CaptureEditOutboxKind.Token && Names(entry, captureId)))
        {
            await outbox.RemoveAsync(queued.Id, cancellationToken);
        }

        if (entries.FirstOrDefault(entry => entry.Id == captureId && IsCapture(entry)) is { } waiting
            && await outbox.RemoveAsync(captureId, cancellationToken))
        {
            ReleaseFiles(waiting);
            return;
        }

        if (entries.Any(entry => entry.Kind == CaptureWithdrawOutboxKind.Token && Names(entry, captureId))) return;

        await outbox.EnqueueAsync(
            CaptureWithdrawOutboxKind.Token,
            Guid.CreateVersion7(),
            CaptureWithdrawOutboxKind.Write(new CaptureWithdrawal(captureId)),
            cancellationToken);
    }

    /// <summary>
    /// <paramref name="item"/> as the phone should show it while its changes are
    /// on their way: null when a withdrawal of it is waiting, the last waiting
    /// edit's title and body over it otherwise, and the item itself when nothing
    /// of it is waiting. Pass the outbox's entries, and any just delivered that a
    /// pull has not yet caught up with.
    /// </summary>
    public static InboxItem? Overlay(InboxItem item, IEnumerable<OutboxEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(entries);

        CaptureEdit? last = null;

        foreach (var entry in entries)
        {
            if (entry.Kind == CaptureWithdrawOutboxKind.Token && Names(entry, item.Id)) return null;
            if (entry.Kind == CaptureEditOutboxKind.Token && ReadEdit(entry) is { } edit && edit.CaptureId == item.Id) last = edit;
        }

        return last is null ? item : item with { Title = last.Title, BodyMd = last.BodyMd };
    }

    /// <summary>Whether an edit or withdrawal of <paramref name="captureId"/> is
    /// still waiting in <paramref name="entries"/> — the row reads "Waiting".</summary>
    public static bool IsChangeWaiting(Guid captureId, IEnumerable<OutboxEntry> entries) =>
        WaitingChange(captureId, entries) is not null;

    /// <summary>The newest edit or withdrawal of <paramref name="captureId"/> still
    /// in <paramref name="entries"/>, or null — the entry a row reads its stuck
    /// reason from and retries.</summary>
    public static OutboxEntry? WaitingChange(Guid captureId, IEnumerable<OutboxEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return entries.LastOrDefault(entry =>
            entry.Kind is CaptureEditOutboxKind.Token or CaptureWithdrawOutboxKind.Token && Names(entry, captureId));
    }

    /// <summary>A rewritten entry the service had set aside — a capture refused for
    /// its words, an edit an older service could not take — goes again: rewording
    /// it is the person's answer to the refusal, and it should not wait for a tap.</summary>
    private Task RequeueIfSetAsideAsync(OutboxEntry rewritten, CancellationToken cancellationToken) =>
        rewritten.IsParked ? outbox.RequeueAsync(rewritten.Id, cancellationToken) : Task.CompletedTask;

    private static bool IsCapture(OutboxEntry entry) =>
        entry.Kind is CaptureOutboxKind.Token or TalkNoteOutboxKind.Token;

    private static string Reworded(OutboxEntry entry, string title, string? bodyMd)
    {
        if (entry.Kind == TalkNoteOutboxKind.Token)
        {
            var payload = TalkNoteOutboxKind.Read(entry);
            return TalkNoteOutboxKind.Write(payload with { Capture = payload.Capture with { Title = title, BodyMd = bodyMd } });
        }

        return CaptureOutboxKind.Write(CaptureOutboxKind.Read(entry) with { Title = title, BodyMd = bodyMd });
    }

    private void ReleaseFiles(OutboxEntry entry)
    {
        if (entry.Kind != TalkNoteOutboxKind.Token) return;

        foreach (var attachment in TalkNoteOutboxKind.Read(entry).Capture.Attachments ?? []) files.Release(attachment.Id);
    }

    /// <summary>Whether a capture-edit or capture-withdraw entry names
    /// <paramref name="captureId"/>. One this build cannot read names nothing; the
    /// outbox still sends or sets it aside on its own.</summary>
    private static bool Names(OutboxEntry entry, Guid captureId) => entry.Kind switch
    {
        CaptureEditOutboxKind.Token => ReadEdit(entry)?.CaptureId == captureId,
        CaptureWithdrawOutboxKind.Token => ReadWithdrawal(entry)?.CaptureId == captureId,
        _ => false,
    };

    private static CaptureEdit? ReadEdit(OutboxEntry entry)
    {
        try
        {
            return CaptureEditOutboxKind.Read(entry);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static CaptureWithdrawal? ReadWithdrawal(OutboxEntry entry)
    {
        try
        {
            return CaptureWithdrawOutboxKind.Read(entry);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
