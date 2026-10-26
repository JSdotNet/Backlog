using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// Whether a roadmap item offers its plan's queue icon, and where the session would start
/// (local ADR 0037 §2; <c>.devbook/domain/sessions/features.md#start-a-session-from-an-entry</c>).
/// <para>
/// Only a stored item with a tag offers it: the request names the plan by its tag, and
/// the session it starts runs <c>execute-plan</c> over every entry carrying it. The
/// session starts in the item's first repository — in the order the item names them —
/// that has a clone folder on this machine, so there is nothing to choose: a plan's
/// sub-agents work each entry in its own repository whichever folder the plan session
/// starts in.
/// </para>
/// <para>
/// An item whose repositories have no clone folder, or that names none, offers the
/// action disabled with the reason rather than hiding it, as a prompt entry's queue
/// icon does.
/// </para>
/// </summary>
/// <param name="Tag">The plan's tag, without its sigil.</param>
/// <param name="Repository">The repository the session starts in, as <c>owner/name</c>,
/// or null when none has a clone folder.</param>
/// <param name="CloneFolder">That repository's clone folder.</param>
/// <param name="DisabledReason">Why the action cannot be taken, or null when it can.</param>
public sealed record PlanSessionOffer(string Tag, string? Repository, string? CloneFolder, string? DisabledReason)
{
    /// <summary>The reason given when the item names no repository.</summary>
    public const string NoRepository = "This plan names no repository, so there is no clone folder to start a session in.";

    /// <summary>Whether the action can be taken.</summary>
    public bool IsEnabled => DisabledReason is null;

    /// <summary>The offer for <paramref name="item"/>, or null when it offers none.</summary>
    /// <param name="item">The item, or null while new work is being planned.</param>
    /// <param name="resolve">Finds a configured repository by the alias the item
    /// stores, or null when it is no longer configured.</param>
    public static PlanSessionOffer? For(RoadmapItemDto? item, Func<string, GitHubRepositoryRef?> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);

        if (item is null || string.IsNullOrWhiteSpace(item.Tag)) return null;

        var tag = item.Tag.Trim().TrimStart('+', '#').Trim();
        if (tag.Length == 0) return null;

        var repositories = item.RepositoryAliases
            .Select(resolve)
            .OfType<GitHubRepositoryRef>()
            .Where(repository => !repository.Archived)
            .DistinctBy(repository => repository.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (repositories.Count == 0) return new PlanSessionOffer(tag, null, null, NoRepository);

        if (repositories.FirstOrDefault(repository => !string.IsNullOrWhiteSpace(repository.CloneDirectory)) is { } ready)
        {
            return new PlanSessionOffer(tag, ready.FullName, ready.CloneDirectory, null);
        }

        var named = repositories.Count == 1
            ? repositories[0].FullName
            : string.Join(", ", repositories.Take(repositories.Count - 1).Select(repository => repository.FullName))
              + " or " + repositories[^1].FullName;

        return new PlanSessionOffer(
            tag,
            null,
            null,
            $"No clone folder is set for {named}. Set one in Settings › Repositories to start a session there.");
    }
}
