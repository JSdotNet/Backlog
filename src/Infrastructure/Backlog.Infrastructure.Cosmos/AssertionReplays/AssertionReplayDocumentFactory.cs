using System.Security.Cryptography;
using System.Text;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Infrastructure.Cosmos.AssertionReplays;

/// <summary>
/// The document for one spent assertion: its id and its TTL rule, separate from
/// <see cref="CosmosAssertionReplayCache"/> so both can be pinned without a store.
/// </summary>
internal static class AssertionReplayDocumentFactory
{
    /// <summary>
    /// How long past the moment it could last be taken a spent id is kept, in seconds.
    /// TTL reaping is lazy, so an id is usually kept longer still; a lapsed id that is
    /// not yet reaped reads as spent, a refusal in the safe direction for an id no honest
    /// device reuses.
    /// </summary>
    public const int ReapGraceSeconds = 60;

    /// <summary>
    /// The document to create for <paramref name="assertionId"/> spent by
    /// <paramref name="deviceId"/>, with its <c>ttl</c> computed from
    /// <paramref name="now"/>: the seconds until <paramref name="acceptableUntil"/>, plus
    /// the grace. A verified assertion is acceptable for at most its lifetime and a clock
    /// skew on either end, so that bounds the number too. Floored at the grace, because
    /// Cosmos reads a non-positive <c>ttl</c> as "expire on write".
    /// </summary>
    public static AssertionReplayDocument From(DeviceId deviceId, string assertionId, DateTimeOffset acceptableUntil, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(assertionId);

        var untilLapsed = (int)Math.Ceiling((acceptableUntil - now).TotalSeconds);

        return new AssertionReplayDocument
        {
            Id = DocumentId(deviceId, assertionId),
            DeviceId = ReplicaDocumentSerialization.Key(deviceId.Value),
            AcceptableUntil = acceptableUntil,
            Ttl = Math.Max(untilLapsed, 0) + ReapGraceSeconds,
        };
    }

    /// <summary>SHA-256 hex of <c>"&lt;device id&gt;:&lt;jti&gt;"</c>: one id per device
    /// and assertion, of a fixed length whatever the device sent as its <c>jti</c>.</summary>
    private static string DocumentId(DeviceId deviceId, string assertionId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{ReplicaDocumentSerialization.Key(deviceId.Value)}:{assertionId}")));
}
