using System.Security.Cryptography;
using System.Text;

using Backlog.Modules.Capture.Abstractions;

namespace Backlog.Modules.Capture.Features.RunCapture;

/// <summary>
/// The id a capture is delivered under, derived from where it came from and
/// what the source calls it — never drawn fresh.
/// <para>
/// This is the whole of how a re-run avoids adding the same entry twice. The
/// run keeps no record of what it delivered; it hands every entry over every
/// time, and the receiving side answers "already known" by id. For that to
/// work the id has to be a pure function of the entry, so it is a name-based
/// UUID over <c>capture:{slug}:{externalId}</c> — the RFC 9562 version 8
/// name-based shape its Appendix B lays out, with SHA-256 rather than version
/// 5's SHA-1, so nothing in the build has to explain away a weak hash. The
/// kind is in the name because two sources can legitimately use the same
/// external id — a URL, say — for two different things.
/// </para>
/// <para>
/// The scheme is pinned by a test. Changing it — the prefix, the hash, the
/// slug spelling — would give every entry ever captured a new id and re-deliver
/// all of them on the next run.
/// </para>
/// </summary>
internal static class CaptureIds
{
    public static Guid For(CaptureSourceKind kind, string externalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        var name = Encoding.UTF8.GetBytes($"capture:{CaptureSourceKinds.Slug(kind)}:{externalId}");

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(name, hash);

        // The version and variant bits are what make this a well-formed UUID
        // rather than sixteen bytes of digest.
        var id = hash[..16];
        id[6] = (byte)((id[6] & 0x0F) | 0x80);
        id[8] = (byte)((id[8] & 0x3F) | 0x80);

        return new Guid(id, bigEndian: true);
    }
}
