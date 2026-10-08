using System.Text.Json;

using Backlog.Mobile.UI.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Outbox;

/// <summary>
/// A task added or edited on the phone, as an outbox entry: a <see cref="TaskChange"/>
/// pushed to <c>POST /api/sync/tasks</c>.
/// <para>
/// The same queue, order, backoff and waiting marker as a capture; only the
/// endpoint and the body differ. No idempotency key is needed: the replica upserts
/// whole documents under last-write-wins, so a retry that sends the same change
/// under the same id lands the same document (.devbook/arc42/adr/0005).
/// </para>
/// </summary>
public sealed class TaskOutboxKind(CloudSyncClient sync) : IOutboxKind
{
    /// <summary>A stored value — the <c>kind</c> column of every queued task.</summary>
    public const string Token = "task";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Kind => Token;

    public static string Write(TaskChange change) => JsonSerializer.Serialize(change, Json);

    public static TaskChange Read(OutboxEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return JsonSerializer.Deserialize<TaskChange>(entry.PayloadJson, Json)
            ?? throw new InvalidOperationException($"Outbox entry {entry.Id} holds no task.");
    }

    public Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        // The task's own id, as queued. An added task is queued under an entry
        // id equal to it; an edit gets an entry id of its own and must still name
        // the task it edits, so the entry's id is never written over it. A retry
        // repeats the same document either way.
        var change = Read(entry);

        return OutboxDelivery.AttemptAsync(() => sync.PushTaskAsync(change, cancellationToken), cancellationToken);
    }
}
