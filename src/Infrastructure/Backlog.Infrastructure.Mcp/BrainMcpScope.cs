using Backlog.SharedKernel;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The Brain as a session names it on the wire: <c>#brain</c>, in the place a
/// devbook tool otherwise takes a repository's <c>owner/name</c>
/// (<c>.devbook/domain/brain/features.md#mcp-brain-scope</c>).
/// <para>
/// Not <see cref="BrainScope.Value"/>, the empty string the shell stores for the
/// Brain: on this wire an empty <c>repository</c> is already the
/// <c>repository.required</c> refusal, and a session that forgot the argument
/// must not find itself reading somebody's private notes instead. Not
/// <c>brain</c> either, which a repository could be called (local ADR 0027,
/// <em>Rejected</em>). <c>#brain</c> cannot be a repository: a GitHub owner or
/// name cannot hold a <c>#</c>, and <c>GitHubRepositoryRef.TryParse</c> reads a
/// line that starts with one as a comment, so no configured alias does either.
/// </para>
/// <para>
/// It is matched before <see cref="RepositoryScope.Resolve"/> asks the
/// directory, so not even a registration could shadow it. Case and the
/// surrounding whitespace are forgiven, as they are for a repository name.
/// </para>
/// </summary>
internal static class BrainMcpScope
{
    /// <summary>The Brain's name in a tool's <c>repository</c> argument, and the
    /// <c>repository</c> every Brain answer carries.</summary>
    internal const string Value = "#brain";

    /// <summary>Whether <paramref name="repository"/> names the Brain.</summary>
    internal static bool Is(string? repository) =>
        string.Equals(repository?.Trim(), Value, StringComparison.OrdinalIgnoreCase);

    /// <summary>A note's repository alias as a refusal puts it into words: the
    /// Brain by its wire name, else the alias itself.</summary>
    internal static string Display(string? repositoryAlias) =>
        string.IsNullOrEmpty(repositoryAlias) ? $"the Brain ({Value})" : repositoryAlias;
}
