using System.Security.Cryptography;
using System.Text;

namespace Backlog.Modules.Tasks.Abstractions.Connectors;

/// <summary>
/// The id a linked task carries, derived from the item it follows — never drawn
/// fresh.
/// <para>
/// This is how two desktops syncing the same repository create one task rather
/// than two, and how a re-run finds the task it made last time without keeping a
/// map. It is a pure function of the item: a name-based UUID over
/// <c>linked:{connectorId}:{externalId}</c>, on the scheme Capture's ids use — RFC
/// 9562 version 8 with SHA-256, the first sixteen bytes of the digest with the
/// version and variant bits set. The connector is in the name because two sources
/// can use the same external id for two different things.
/// </para>
/// <para>
/// Public because the sync is not its only reader: a connector, or Promote to
/// plan, has to name the task an item became. The scheme is pinned by a test.
/// Changing it would give every linked task a new id, and the next sync would
/// create every one of them again beside the old.
/// </para>
/// </summary>
public static class LinkedTaskIds
{
    public static Guid For(string connectorId, string externalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        var name = Encoding.UTF8.GetBytes($"linked:{connectorId}:{externalId}");

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(name, hash);

        var id = hash[..16];
        id[6] = (byte)((id[6] & 0x0F) | 0x80);
        id[8] = (byte)((id[8] & 0x3F) | 0x80);

        return new Guid(id, bigEndian: true);
    }
}
