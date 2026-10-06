namespace Backlog.Modules.Tasks.Abstractions.Connectors;

/// <summary>
/// The one contract every external task source is reached through — GitHub
/// issues, a spec-manager product's backlog, and whatever comes after them.
/// <para>
/// Everything that differs between two systems sits behind it: how to fetch, what
/// an item is called, how its status reads. Everything that is the same for every
/// system — which task an item becomes, which fields the source owns, how a status
/// maps — is the sync's, in the Tasks module, which never names a connector. See
/// <c>.devbook/arc42/adr/0020-external-items-arrive-as-linked-tasks.md</c>, §3.
/// </para>
/// <para>
/// A connector holds no settings of its own. What a person connected — the
/// repositories, the products, how each behaves — is an
/// <see cref="IConnectedTargets"/> entry; the credentials it signs in with are the
/// connector's adapter's business and never pass through here. A connector a person
/// has to sign in to also implements <see cref="ITaskConnectorSignIn"/>.
/// </para>
/// </summary>
public interface ITaskConnector
{
    /// <summary>What the source badge, the Source filter and the settings page are
    /// drawn from. <see cref="TaskConnectorDescriptor.Id"/> is the stable id a
    /// linked task's <see cref="SourceRef.ConnectorId"/> carries.</summary>
    TaskConnectorDescriptor Descriptor { get; }

    /// <summary>What the sync and the screens may ask of this source.</summary>
    TaskConnectorCapabilities Capabilities { get; }

    /// <summary>
    /// What a person may connect through this source — repositories, product slugs —
    /// in the spelling <see cref="FetchAsync"/> takes and a
    /// <see cref="ConnectedTarget.Target"/> stores. What the settings page offers.
    /// <para>
    /// A default body, answering none, so a connector whose targets are typed in
    /// rather than picked need not say so.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<string>> ListTargetsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    /// <summary>
    /// Every open item of <paramref name="target"/>, plus every item closed since
    /// <paramref name="since"/>, each normalised to a <see cref="SourceItem"/>.
    /// <para>
    /// <paramref name="since"/> is null on a target's first sync, when only the open
    /// items are wanted. The closed ones matter after that: an item that closed
    /// between two runs has to be seen closed once, or its task would read as
    /// vanished rather than finished.
    /// </para>
    /// <para>
    /// A failure throws. The sync then writes nothing — in particular it archives
    /// nothing as vanished, because an empty answer from a source that could not be
    /// reached is not an empty source.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken);

    /// <summary>The connected account's name at the source, so "Assigned to me" knows
    /// who "me" is; null when no account is connected.</summary>
    Task<string?> WhoAmIAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Finishes <paramref name="item"/> at the source — GitHub closes the issue as
    /// completed, spec-manager moves the item to the product's first end status.
    /// Answers null when the source took it, and otherwise a sentence for the person
    /// saying why not, which the task then shows.
    /// <para>
    /// Asked only of a connector whose <see cref="TaskConnectorCapabilities.CanComplete"/>
    /// is set, and only for a target the person turned
    /// <see cref="ConnectedTarget.CompleteAtSource"/> on for. A refusal is an answer,
    /// not a failure, the way <see cref="ITaskConnectorSignIn.SignInAsync"/> answers;
    /// a throw is read as a refusal with the exception's message.
    /// </para>
    /// <para>
    /// A default body, refusing, so a connector that cannot write back need not say
    /// so twice.
    /// </para>
    /// </summary>
    Task<string?> CompleteAsync(SourceRef item, CancellationToken cancellationToken) =>
        Task.FromResult<string?>("This source cannot complete items.");
}

/// <summary>
/// How a connector presents itself.
/// </summary>
/// <param name="Id">Stable and lower case, such as <c>github</c>. Stored on every
/// linked task, so it never changes once shipped.</param>
/// <param name="DisplayName">What a person reads, such as <c>GitHub</c>.</param>
/// <param name="Icon">The icon name the badge is drawn with.</param>
/// <param name="ColorToken">The design token the badge is coloured with.</param>
public sealed record TaskConnectorDescriptor(string Id, string DisplayName, string Icon, string ColorToken);

/// <summary>
/// What a source can do, read by the sync and the screens rather than assumed.
/// </summary>
/// <param name="HasEffort">Items carry a size that maps to effort.</param>
/// <param name="HasDependencies">Items say which other items they wait on.</param>
/// <param name="CanSetStatus">The source accepts a status change — what write-back
/// will need.</param>
/// <param name="CanComment">The source accepts a comment on an item.</param>
/// <param name="CanComplete">The source can finish an item through
/// <see cref="ITaskConnector.CompleteAsync"/>, so the settings page offers
/// "Complete at the source" for its targets.</param>
public sealed record TaskConnectorCapabilities(
    bool HasEffort = false,
    bool HasDependencies = false,
    bool CanSetStatus = false,
    bool CanComment = false,
    bool CanComplete = false);

/// <summary>
/// The four states every source's statuses are normalised to. One map turns each
/// into a task status, the same for every connector, so no connector ever names
/// a task status itself.
/// </summary>
public enum NormalisedSourceState
{
    /// <summary>Open and not started. Maps to Ready.</summary>
    Open,

    /// <summary>Being worked on. Maps to In progress.</summary>
    Active,

    /// <summary>Finished. Maps to Done.</summary>
    Done,

    /// <summary>Closed without being finished. Maps to Archived.</summary>
    Dropped,
}

/// <summary>
/// The spelling a <see cref="NormalisedSourceState"/> is stored and replicated
/// as: <c>open</c>, <c>active</c>, <c>done</c>, <c>dropped</c>. A word rather than
/// a number, so a stored value means the same thing if the enum is reordered.
/// </summary>
public static class NormalisedSourceStates
{
    public static string ToWire(NormalisedSourceState state) => state switch
    {
        NormalisedSourceState.Open => "open",
        NormalisedSourceState.Active => "active",
        NormalisedSourceState.Done => "done",
        NormalisedSourceState.Dropped => "dropped",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Not a normalised source state."),
    };

    /// <summary>The state a stored word names, or null for none or a word this
    /// build does not know — which the sync reads as "the state moved", once,
    /// rather than failing the read.</summary>
    public static NormalisedSourceState? FromWire(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "open" => NormalisedSourceState.Open,
        "active" => NormalisedSourceState.Active,
        "done" => NormalisedSourceState.Done,
        "dropped" => NormalisedSourceState.Dropped,
        _ => null,
    };
}

/// <summary>
/// One item from a source, normalised.
/// <para>
/// The title, the state and the assignee are what the source goes on owning; the
/// body, the labels, the effort and the due date are copied once, when the task is
/// created, and the person owns them after that (ADR 0020, §4).
/// </para>
/// <para>
/// <see cref="WaitsOn"/> is carried but not read by the sync yet: turning it into
/// the task's dependencies is later work.
/// </para>
/// </summary>
/// <param name="ExternalId">The source's stable id for the item.</param>
/// <param name="DisplayKey">How a person names it, such as <c>#412</c>.</param>
/// <param name="Title">The item's title.</param>
/// <param name="Url">Where it opens at the source.</param>
/// <param name="Body">Its description, as Markdown.</param>
/// <param name="State">Its status, normalised.</param>
/// <param name="SourceStateName">Its status as the source names it.</param>
/// <param name="Assignee">Who it is assigned to, or null.</param>
/// <param name="UpdatedAt">When the source last changed it.</param>
/// <param name="Labels">Its labels as the source spells them. One starting with
/// <c>+</c> names a plan.</param>
/// <param name="Effort">Its size, when the source has one.</param>
/// <param name="DueOn">The day it is due, when the source has one.</param>
/// <param name="WaitsOn">The external ids of the items it waits on.</param>
/// <param name="IsBlocked">Whether it cannot be worked on now. The source owns this
/// by its moves, the way it owns the state: the task is marked blocked the day the
/// source blocks the item and unmarked the day it unblocks it.</param>
/// <param name="BlockedReason">Why, when the source says.</param>
/// <param name="References">The devbook pages and chapters the item points at,
/// written <c>path</c> or <c>path#anchor</c>. Added to the task's own references on
/// every sync and never taken away; one that names no page is skipped.</param>
/// <param name="CompletedAt">When the source says the item was finished, if it says.
/// The day the task is ticked off on when the item closes; today when this is
/// null.</param>
public sealed record SourceItem(
    string ExternalId,
    string DisplayKey,
    string Title,
    string Url,
    string Body,
    NormalisedSourceState State,
    string SourceStateName,
    string? Assignee,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> Labels,
    int? Effort = null,
    DateOnly? DueOn = null,
    IReadOnlyList<string>? WaitsOn = null,
    bool IsBlocked = false,
    string? BlockedReason = null,
    IReadOnlyList<string>? References = null,
    DateTimeOffset? CompletedAt = null);
