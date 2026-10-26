using System.Security.Cryptography;
using System.Text;

namespace Backlog.Infrastructure.SpecManager.Questions;

/// <summary>
/// The id a spec-manager question's notification carries, derived from the question —
/// never drawn fresh.
/// <para>
/// A name-based UUID, version 5 (RFC 9562 §5.5, SHA-1), in two steps: the installation's
/// root names a namespace under the URL namespace, and the question's id is the name in
/// it. So the same question on the same installation is the same notification on every
/// desktop signed in to it: the first to put it on the replica owns it, and a second
/// desktop that publishes it finds it held and stays a viewer. Two installations never
/// share an id, even for a question id they happen to share.
/// </para>
/// <para>
/// The scheme is pinned by a test. Changing it would give every open question a new
/// notification beside the old.
/// </para>
/// </summary>
internal static class SpecManagerQuestionIds
{
    /// <summary>RFC 9562's namespace for names that are URLs.</summary>
    private static readonly Guid UrlNamespace = new("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

    /// <summary>The notification id of <paramref name="questionId"/> on the installation at
    /// <paramref name="root"/>.</summary>
    public static Guid For(string root, string questionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(questionId);

        var installation = Version5(UrlNamespace, root.Trim().TrimEnd('/'));
        return Version5(installation, questionId.Trim().ToLowerInvariant());
    }

    /// <summary>The notification id of the alert that says an answer to
    /// <paramref name="questionId"/> was not recorded — derived too, so a replayed answer
    /// that is refused again raises the alert once.</summary>
    public static Guid RefusalFor(string root, string questionId) =>
        Version5(For(root, questionId), "refused");

    /// <summary>RFC 9562 §5.5: SHA-1 over the namespace's bytes and the name's UTF-8,
    /// the first sixteen bytes of the digest with the version and variant set.</summary>
    internal static Guid Version5(Guid @namespace, string name)
    {
        var space = @namespace.ToByteArray(bigEndian: true);
        var named = Encoding.UTF8.GetBytes(name);

        var input = new byte[space.Length + named.Length];
        space.CopyTo(input, 0);
        named.CopyTo(input, space.Length);

        Span<byte> hash = stackalloc byte[SHA1.HashSizeInBytes];
#pragma warning disable CA5350 // SHA-1 is what version 5 is defined over; this names, it does not protect.
        SHA1.HashData(input, hash);
#pragma warning restore CA5350

        var id = hash[..16];
        id[6] = (byte)((id[6] & 0x0F) | 0x50);
        id[8] = (byte)((id[8] & 0x3F) | 0x80);

        return new Guid(id, bigEndian: true);
    }
}
