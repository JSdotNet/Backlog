namespace Backlog.SharedKernel;

/// <summary>
/// How an archived repository is named wherever show archived offers it (local ADR 0029,
/// §3): its alias and the word, so the mark is never a shade alone. One place, so the
/// header's chip and every picker say it the same way.
/// </summary>
public static class ArchivedRepositoryLabel
{
    /// <summary>What follows an archived repository's alias, its leading space included.</summary>
    public const string Suffix = " (archived)";

    /// <summary>The alias, followed by <see cref="Suffix"/> when the repository is
    /// archived.</summary>
    public static string Of(string alias, bool archived) => archived ? alias + Suffix : alias;
}
