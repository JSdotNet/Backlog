using System.Globalization;

namespace Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;

/// <summary>
/// One pull request by its repository's <c>owner/name</c> and its number — how every
/// reader names the pull request it asks about (local ADR 0042).
/// </summary>
public sealed record PullRequestReference(string RepositoryFullName, int Number)
{
    /// <summary><c>owner/name#number</c>: the key a pull request is kept and compared
    /// under. Compare it without regard to case, the way GitHub compares the
    /// repository.</summary>
    public string Key => $"{RepositoryFullName.Trim()}#{Number.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Whether this names the given pull request: the repository compared
    /// without regard to case, the way GitHub compares it.</summary>
    public bool Names(string repositoryFullName, int number) =>
        Number == number
        && string.Equals(RepositoryFullName.Trim(), repositoryFullName?.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>An issue by its repository's <c>owner/name</c> and its number — what a pull
/// request names as the issues it closes.</summary>
public sealed record IssueReference(string RepositoryFullName, int Number);
