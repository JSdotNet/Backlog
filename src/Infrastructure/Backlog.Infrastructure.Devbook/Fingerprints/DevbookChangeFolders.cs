using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Devbook.Fingerprints;

/// <summary>One change under <c>openspec/changes/&lt;name&gt;/</c> and its current
/// <c>devbook.chapter.fingerprint@1</c>, null when its files could not be
/// read.</summary>
public sealed record DevbookChangeReading(string Name, string? Fingerprint);

/// <summary>
/// The change proposals of a repository on disk, for the devbook view's Proposals
/// tab (local ADR 0052 §7). Here rather than in the panel's module because a module
/// UI does no file I/O (<c>ModuleUiIoTests</c>).
/// <para>
/// Every read answers rather than throws: a folder that cannot be listed is no
/// proposals, and a change whose files cannot be read has no fingerprint, so the
/// tab leaves it undecidable instead of taking the panel down.
/// </para>
/// </summary>
public static class DevbookChangeFolders
{
    private const string ChangesFolder = "openspec/changes";
    private const string ProposalFile = "proposal.md";

    /// <summary>Every folder directly under <c>openspec/changes/</c> holding a
    /// <c>proposal.md</c>, never <c>archive/</c>, ordered by name, with its
    /// fingerprint. None when the root is not on disk or cannot be read.</summary>
    public static IReadOnlyList<DevbookChangeReading> Read(string? repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot)) return [];

        var changes = Path.Combine([repositoryRoot, .. ChangesFolder.Split('/')]);

        try
        {
            if (!Directory.Exists(changes)) return [];

            return
            [
                .. Directory.EnumerateDirectories(changes)
                    .Select(folder => Path.GetFileName(folder))
                    .Where(DevbookSubject.IsProposalName)
                    .Where(name => File.Exists(Path.Combine(changes, name, ProposalFile)))
                    .Order(StringComparer.Ordinal)
                    .Select(name => new DevbookChangeReading(name, Fingerprint(repositoryRoot, name)))
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The change's fingerprint as its files stand now, or null when it
    /// has no proposal or cannot be read.</summary>
    public static string? Fingerprint(string? repositoryRoot, string name)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot) || !DevbookSubject.IsProposalName(name)) return null;

        try
        {
            return DevbookFingerprint.ReadChange(repositoryRoot, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
