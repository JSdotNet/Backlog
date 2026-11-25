using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>One repository an entry could start a session in, and its clone folder
/// on this machine when one is set.</summary>
/// <param name="Repository">The repository, as <c>owner/name</c>.</param>
/// <param name="CloneFolder">Its clone folder, or null when none is set.</param>
public sealed record SessionStartTarget(string Repository, string? CloneFolder)
{
    /// <summary>Whether a session can start here: only in a folder this machine has.</summary>
    public bool HasCloneFolder => !string.IsNullOrWhiteSpace(CloneFolder);

    /// <summary>Why a session cannot start here, naming the missing clone folder, or
    /// null when it can.</summary>
    public string? MissingReason => HasCloneFolder ? null : SessionStartOffer.NoCloneFolder([Repository]);
}

/// <summary>
/// Whether an entry offers Start session, and on what terms (local ADR 0038 §1;
/// <c>.devbook/domain/sessions/features.md#start-a-session-from-an-entry</c>).
/// <para>
/// Only a prompt entry offers it: a prompt is the type written to be run by an AI
/// session, and a task, a test or an idea is the person's own. A prompt that has not
/// been saved yet has no id for a request to name, so it offers nothing either.
/// </para>
/// <para>
/// A prompt whose repositories have no clone folder on this machine — or that names
/// no repository — offers the action disabled, with the reason, rather than hiding it,
/// on the terms <c>.devbook/design/interaction-guidelines.md#action-density-and-overflow</c>
/// sets: an act that vanishes reads as one the product cannot do, and the reason says
/// what would make it possible.
/// </para>
/// </summary>
/// <param name="Targets">Every repository the entry names that resolves, in the order
/// it names them, each with its clone folder.</param>
/// <param name="DisabledReason">Why the action cannot be taken, or null when it can.</param>
/// <param name="PlanTag">The plan the entry belongs to, <c>+</c> and all, when the
/// action is refused because a plan is queued only as a whole (local ADR 0062 §4).</param>
public sealed record SessionStartOffer(IReadOnlyList<SessionStartTarget> Targets, string? DisabledReason, string? PlanTag = null)
{
    /// <summary>The reason given when the entry names no repository.</summary>
    public const string NoRepository = "This prompt names no repository, so there is no clone folder to start a session in.";

    /// <summary>Whether the action can be taken.</summary>
    public bool IsEnabled => DisabledReason is null;

    /// <summary>Whether pressing it asks which repository first: the entry names
    /// several.</summary>
    public bool AsksWhichRepository => IsEnabled && Targets.Count > 1;

    /// <summary>The one repository the press starts the session in, when there is no
    /// choice to make.</summary>
    public SessionStartTarget? Only => IsEnabled && Targets.Count == 1 ? Targets[0] : null;

    /// <summary>The offer for an entry, or null when it offers none.</summary>
    /// <param name="type">The entry's type.</param>
    /// <param name="persisted">Whether the entry has been saved and has an id.</param>
    /// <param name="repositories">The configured repositories the entry names. An
    /// archived one is left out: nobody works in it any more.</param>
    /// <param name="planTag">The plan the entry belongs to — a plan tag a roadmap item
    /// carries — or null. A plan entry's own
    /// queue is refused: the plan is queued only as a whole, so the action is offered
    /// disabled with the sentence that says so (local ADR 0062 §4).</param>
    public static SessionStartOffer? For(EntryType type, bool persisted, IEnumerable<GitHubRepositoryRef?> repositories, string? planTag = null)
    {
        ArgumentNullException.ThrowIfNull(repositories);

        if (type != EntryType.Prompt || !persisted) return null;

        if (PlanQueueOffer.Bare(planTag) is { Length: > 0 } plan)
        {
            return new SessionStartOffer([], PlanQueueOffer.QueueThePlanAsAWhole, "+" + plan);
        }

        IReadOnlyList<SessionStartTarget> targets =
        [
            .. repositories
                .OfType<GitHubRepositoryRef>()
                .Where(repository => !repository.Archived)
                .DistinctBy(repository => repository.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(repository => new SessionStartTarget(
                    repository.FullName,
                    string.IsNullOrWhiteSpace(repository.CloneDirectory) ? null : repository.CloneDirectory))
        ];

        if (targets.Count == 0) return new SessionStartOffer(targets, NoRepository);

        return targets.Any(target => target.HasCloneFolder)
            ? new SessionStartOffer(targets, null)
            : new SessionStartOffer(targets, NoCloneFolder([.. targets.Select(target => target.Repository)]));
    }

    /// <summary>The sentence that names the repositories with no clone folder.</summary>
    internal static string NoCloneFolder(IReadOnlyList<string> repositories)
    {
        var named = repositories.Count switch
        {
            1 => repositories[0],
            _ => string.Join(", ", repositories.Take(repositories.Count - 1)) + " or " + repositories[^1]
        };

        return $"No clone folder is set for {named}. Set one in Settings › Repositories to start a session there.";
    }
}
