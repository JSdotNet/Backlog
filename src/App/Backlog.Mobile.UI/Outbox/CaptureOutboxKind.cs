using System.Text.Json;

using Backlog.Mobile.UI.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Outbox;

/// <summary>
/// The capture, as an outbox entry: a <see cref="CaptureRequest"/> carrying the
/// entry's own id, posted to <c>POST /api/sync/inbox</c>.
/// <para>
/// The id is the point. It is minted when the capture is queued and sent on
/// every attempt, and the service answers a second post of the same id with the
/// capture it already has (200) instead of a second one — so a phone that lost
/// the first 201 to a dropped connection can send again without doubling it.
/// </para>
/// </summary>
public sealed class CaptureOutboxKind(CloudSyncClient sync) : IOutboxKind
{
    /// <summary>A stored value — the <c>kind</c> column of every queued capture.</summary>
    public const string Token = "capture";

    /// <summary>What the phone names itself as, which the desktop files a capture
    /// under as its channel.</summary>
    public const string Source = "mobile";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Kind => Token;

    /// <summary>A new capture: its id minted now, so every attempt sends the same one.</summary>
    public static CaptureRequest Create(string title, string? bodyMd = null, IReadOnlyList<string>? tags = null, string? person = null) =>
        new(title, Source, Guid.CreateVersion7(), bodyMd, tags, person);

    public static string Write(CaptureRequest request) => JsonSerializer.Serialize(request, Json);

    public static CaptureRequest Read(OutboxEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return JsonSerializer.Deserialize<CaptureRequest>(entry.PayloadJson, Json)
            ?? throw new InvalidOperationException($"Outbox entry {entry.Id} holds no capture.");
    }

    public Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        // The entry's id wins over whatever the payload says: it is the one the
        // outbox keys on, and the one every retry has to repeat.
        var request = Read(entry) with { Id = entry.Id };

        return OutboxDelivery.AttemptAsync(() => sync.PostCaptureAsync(request, cancellationToken), cancellationToken);
    }
}
