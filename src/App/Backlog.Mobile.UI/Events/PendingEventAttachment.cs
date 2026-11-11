using Backlog.Mobile.UI.Outbox;

namespace Backlog.Mobile.UI.Events;

/// <summary>An attachment request still on the phone (<see cref="EventNoteAttachments.PendingFor"/>):
/// the event it names, and the outbox entry that carries it.</summary>
public sealed record PendingEventAttachment(Guid EventId, OutboxEntry Entry);
