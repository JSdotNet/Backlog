using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// Whether the pressed plan filter offers its queue icon, and where the session would
/// start (local ADR 0038 §2; <c>.devbook/domain/sessions/features.md#start-a-session-from-an-entry</c>).
/// The roadmap item dialog's plan icon on the same terms — the filter bar asks its own
/// port for the item's repositories rather than Roadmap's offer.
/// <para>
/// One session runs the whole plan by its tag, so there is nothing to choose: it starts
/// in the plan's first repository — in the order its roadmap item names them — that has
/// a clone folder on this machine, and the plan's sub-agents work each entry in its own
/// repository. Without a clone folder, or with no repository, the icon is offered
/// disabled with the reason.
/// </para>
/// </summary>
/// <param name="Tag">The plan's tag, without its sigil.</param>
/// <param name="Repository">The repository the session starts in, as <c>owner/name</c>,
/// or null when none has a clone folder.</param>
/// <param name="CloneFolder">That repository's clone folder.</param>
/// <param name="DisabledReason">Why the action cannot be taken, or null when it can.</param>
public sealed record PlanQueueOffer(string Tag, string? Repository, string? CloneFolder, string? DisabledReason)
{
    /// <summary>The reason given when the plan names no repository — the roadmap
    /// dialog's own words.</summary>
    public const string NoRepository = "This plan names no repository, so there is no clone folder to start a session in.";

    /// <summary>Whether the action can be taken.</summary>
    public bool IsEnabled => DisabledReason is null;

    /// <summary>The offer for the plan tagged <paramref name="tag"/> filed under
    /// <paramref name="repositories"/>, or null when the tag names no plan.</summary>
    /// <param name="tag">The plan tag, with or without its sigil.</param>
    /// <param name="repositories">The configured repositories its roadmap item names,
    /// in order; one no longer configured is null. An archived one is left out.</param>
    public static PlanQueueOffer? For(string? tag, IEnumerable<GitHubRepositoryRef?> repositories)
    {
        ArgumentNullException.ThrowIfNull(repositories);

        var bare = (tag ?? string.Empty).Trim().TrimStart('+', '#').Trim();
        if (bare.Length == 0) return null;

        var named = repositories
            .OfType<GitHubRepositoryRef>()
            .Where(repository => !repository.Archived)
            .DistinctBy(repository => repository.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (named.Count == 0) return new PlanQueueOffer(bare, null, null, NoRepository);

        if (named.FirstOrDefault(repository => !string.IsNullOrWhiteSpace(repository.CloneDirectory)) is { } ready)
        {
            return new PlanQueueOffer(bare, ready.FullName, ready.CloneDirectory, null);
        }

        return new PlanQueueOffer(bare, null, null, SessionStartOffer.NoCloneFolder([.. named.Select(repository => repository.FullName)]));
    }
}
