using System.Security.Cryptography;
using System.Text;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Turning a name into something a cache can put on disk.
/// <para>
/// Promoted out of <see cref="PullRequestDetailCache"/>, where it had already been
/// copied once into <see cref="KnowledgeSnapshotCache"/>, rather than copied a third
/// time for <see cref="AgentActivityCache"/>. Three caches in one assembly folding
/// names the same way is one rule; three copies of it is three chances for a cache to
/// change where it files things and take everything already filed there out of reach
/// without saying so.
/// </para>
/// </summary>
internal static class CachePaths
{
    /// <summary>
    /// One path segment standing for a name that may contain anything — a branch name
    /// carrying slashes and colons, a repository, a session id. The readable part is
    /// what makes the folder something a person can look at and understand; the digest
    /// of the original is what keeps two names that fold to the same readable form in
    /// different folders, and <c>fix/a</c> apart from <c>fix-a</c>.
    /// <para>
    /// Trimmed because a long name plus the configured cache root can otherwise pass the
    /// path limit on Windows before a single entry is written under it.
    /// </para>
    /// </summary>
    /// <param name="name">What the segment should read as.</param>
    /// <param name="digestOf">
    /// What the digest is taken over, when that is not the name itself.
    /// <para>
    /// The two separate for exactly one caller and one reason: a transcript's segment
    /// should read as the session id, while two copies of one session filed under two
    /// project folders are two different files and must not collapse into one entry.
    /// Digesting the whole path is what keeps them apart while the readable half still
    /// says which session it is.
    /// </para>
    /// </param>
    internal static string Safe(string name, string? digestOf = null)
    {
        var readable = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            readable.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '-');
        }

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digestOf ?? name)))[..8].ToLowerInvariant();

        var trimmed = readable.ToString().Trim('-');
        if (trimmed.Length > 40) trimmed = trimmed[..40];

        return trimmed.Length == 0 ? digest : $"{trimmed}-{digest}";
    }
}
