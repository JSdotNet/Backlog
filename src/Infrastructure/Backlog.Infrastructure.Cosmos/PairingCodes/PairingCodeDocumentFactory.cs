using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Infrastructure.Cosmos.PairingCodes;

/// <summary>
/// Between the domain record and the stored document, in both directions.
/// <para>
/// Separate from <see cref="CosmosPairingCodeStore"/> so the mapping can be
/// tested without a store: the two things that are easy to get silently wrong
/// here are the spelling of the partition key and the TTL rule, and neither
/// needs an emulator to pin.
/// </para>
/// </summary>
internal static class PairingCodeDocumentFactory
{
    /// <summary>
    /// How long past its own expiry a code document is kept before Cosmos reaps
    /// it, in seconds. Ten minutes — the length of the window itself.
    /// <para>
    /// The grace exists for the person, not the store. A code typed a minute
    /// after its window closed is answered "that code has expired" for as long
    /// as the document is there and "that code is not one this service issued"
    /// once it is gone; the first tells them what to do and the second reads
    /// like a typo. Reaping is lazy on top of that, so the real horizon is
    /// somewhat longer and not exactly knowable — which is fine, because the
    /// handler's <c>HasExpired</c> is what decides and the TTL only tidies up.
    /// </para>
    /// </summary>
    public const int ReapGraceSeconds = 600;

    /// <summary>
    /// The document to write for one issued code, with its <c>ttl</c> computed
    /// from <paramref name="now"/>.
    /// <para>
    /// The TTL is relative to the write, so it has to be computed at the write:
    /// seconds from now until the code expires, plus the grace. It is floored
    /// at the grace alone rather than allowed to reach zero, because Cosmos
    /// reads a non-positive <c>ttl</c> as "expire on write" and a code that
    /// vanished the moment it was issued would report as never issued.
    /// </para>
    /// <para>
    /// <strong>The emulator does not honour TTL at all</strong>: reaping is
    /// deployed-only behaviour, so no test in this repository covers it and
    /// none should pretend to. What the tests pin is that every code carries
    /// the property and that the number is the right one.
    /// </para>
    /// </summary>
    public static PairingCodeDocument From(PairingCode code, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(code);

        var untilExpiry = (int)Math.Ceiling((code.ExpiresAt - now).TotalSeconds);

        return new PairingCodeDocument
        {
            Id = code.CodeHash,
            OwnerId = ReplicaDocumentSerialization.Key(code.OwnerId.Value),
            IssuedBy = ReplicaDocumentSerialization.Key(code.IssuedBy.Value),
            ExpiresAt = code.ExpiresAt,
            Redeemed = code.Redeemed,
            Ttl = Math.Max(untilExpiry, 0) + ReapGraceSeconds,
        };
    }

    /// <summary>
    /// The record to hand back for one stored document, or null for a document
    /// whose owner or issuer is not a GUID — not one this service wrote, and
    /// answered as "no such code" rather than thrown through the handler.
    /// </summary>
    public static PairingCode? ToPairingCode(PairingCodeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Guid.TryParse(document.OwnerId, out var ownerId) || !Guid.TryParse(document.IssuedBy, out var issuedBy))
        {
            return null;
        }

        return new PairingCode(
            document.Id,
            new OwnerId(ownerId),
            new DeviceId(issuedBy),
            document.ExpiresAt,
            document.Redeemed);
    }
}
