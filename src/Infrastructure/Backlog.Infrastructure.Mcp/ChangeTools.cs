using System.ComponentModel;
using System.Globalization;
using System.Text;

using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel.Changes;
using Backlog.SharedKernel.Results;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// What an agent may do with the change journal (local ADR 0061 §5): read the change sets
/// back, and propose that the person restore one. <c>list_change_sets</c> reads; and
/// <c>propose_restore</c> asks the person a question and nothing more.
/// <para>
/// <b>No tool restores.</b> A proposal is an <c>ask</c> notification with the answers
/// "Restore" and "Keep". "Restore" opens the Changes view on that change set's confirmation
/// panel, where the person still confirms or cancels; "Keep" does nothing at all. The
/// answer's handler is the host's (<see cref="RestoreProposals"/> reads its key), because what
/// opens a view lives under <c>src/App</c>, which this project may not reference.
/// </para>
/// <para>
/// <b>The question is the <c>mcp</c> source's,</b> keyed and about what <c>ask</c> keys and
/// names its own, so <c>get_answer</c> and <c>close</c> take the id it answers exactly as they
/// take one <c>ask</c> answered.
/// </para>
/// <para>
/// <b>Its own group, behind no key,</b> beside the tracker tools: the journal is not
/// switchable. <c>propose_restore</c> publishes through the Notifications port, and with
/// notifications off that port refuses rather than the tool being absent.
/// </para>
/// <para><b>An instance per call</b>, for the reason <see cref="WorkTools"/> states.</para>
/// </summary>
/// <param name="notifications">The Notifications port the proposal is published through.</param>
/// <param name="journal">The change journal, or null when the host composes none; both tools
/// then refuse as <c>change_journal.not_available</c>.</param>
[McpServerToolType]
public sealed class ChangeTools(INotifications notifications, IChangeJournal? journal = null)
{
    internal const string ListChangeSets = "list_change_sets";
    internal const string ProposeRestore = "propose_restore";

    /// <summary>The most change sets one <c>list_change_sets</c> answers.</summary>
    internal const int MaxLimit = 200;

    /// <summary>
    /// This class as a group, always listed as <see cref="BacklogMcpTools.Tracker"/> is: the
    /// journal is not switchable. Declared here, so adding it to
    /// <see cref="BacklogMcpTools.Groups"/> is the one shared line this class touches.
    /// </summary>
    internal static BacklogMcpTools.McpToolGroup Group { get; } = new(
        FeatureKey: null,
        typeof(ChangeTools),
        [ListChangeSets, ProposeRestore]);

    [McpServerTool(Name = ListChangeSets, Title = "List the change sets", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "The change sets of Backlog's change journal, newest first: the writes one agent session, one gesture of the "
        + "person, one sync page or one background pass made together. Each has its id, label, actor (person, agent, "
        + "sync, system or unknown), the MCP caller, the session id, when it started, how many documents it changed, "
        + "and whether it was restored. Narrow by sessionId, caller, or a since/until window. Read-only; "
        + "propose_restore takes an id from here.")]
    public async Task<ChangeSetListPayload> ListChangeSetsAsync(
        [Description("Only the change sets made in this Claude session, e.g. your own session id. Optional.")]
        string? sessionId = null,
        [Description("Only the change sets this MCP caller made, as the list names it - a grant as \"name (id)\", or unauthenticated. Optional.")]
        string? caller = null,
        [Description("Only the change sets started at or after this instant, ISO 8601, e.g. 2026-11-25T09:00:00Z. Optional.")]
        DateTimeOffset? since = null,
        [Description("Only the change sets started before this instant, ISO 8601. Optional.")]
        DateTimeOffset? until = null,
        [Description("The most change sets to answer, 1 to 200. Defaults to 50.")]
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var read = Journal();

        if (since is { } from && until is { } to && to <= from)
            throw RepositoryScope.Failure(Error.Validation("change_sets.window_invalid", "until must come after since."));

        if (limit is < 1)
            throw RepositoryScope.Failure(Error.Validation("change_sets.limit_invalid", $"limit is 1 to {MaxLimit}."));

        var entries = await read.QueryAsync(
            new ChangeSetQuery(sessionId, caller, since, until, Math.Min(limit ?? 50, MaxLimit)),
            cancellationToken).ConfigureAwait(false);

        return new ChangeSetListPayload([.. entries.Select(ChangeSetPayload.From)]);
    }

    [McpServerTool(Name = ProposeRestore, Title = "Propose restoring a change set", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description(
        "Asks the person whether to restore one change set: \"Restore <n> changes made by <caller> (<label>)?\", "
        + "answered Restore or Keep. It restores nothing. Restore opens Backlog's Changes view on that change set's "
        + "confirmation panel, where the person still confirms; Keep leaves everything as it is. Answers the "
        + "notification's id at once, as ask does: poll get_answer with it. Refused for a change set the journal "
        + "does not hold, one already restored, or one that changed nothing. Pass sessionId - your Claude session id "
        + "- so the question is about your session; then pass it to get_answer and close too.")]
    public async Task<NotificationPublishedPayload> ProposeRestoreAsync(
        [Description("The change set's id, as list_change_sets answers it.")]
        Guid changeSetId,
        [Description("Your Claude session id, so the question is about this session. Optional.")]
        string? sessionId = null,
        CancellationToken cancellationToken = default,
        McpServer? server = null)
    {
        var read = Journal();

        var entry = await read.FindAsync(changeSetId, cancellationToken).ConfigureAwait(false)
            ?? throw RepositoryScope.Failure(Error.NotFound("change_set.not_found", $"The journal holds no change set {changeSetId}."));

        if (entry.ChangeSet.UndoneBy is { } undoneBy)
        {
            throw RepositoryScope.Failure(Error.Conflict(
                "change_set.already_restored", $"Change set {changeSetId} was already restored, by change set {undoneBy}."));
        }

        if (entry.DocumentCount == 0)
        {
            throw RepositoryScope.Failure(Error.Validation(
                "change_set.nothing_to_restore", $"Change set {changeSetId} changed no document the journal keeps."));
        }

        var key = RestoreProposals.Key(changeSetId);
        var subject = string.IsNullOrWhiteSpace(sessionId)
            ? new NotificationSubject(NotificationTools.SourceContext, NotificationTools.AskSubjectType, key)
            : new NotificationSubject(NotificationTools.SessionsContext, NotificationTools.SessionSubjectType, sessionId.Trim());

        var draft = new NotificationDraft(
            new NotificationSource(NotificationTools.SourceContext, key),
            subject,
            NotificationKind.Question,
            NotificationSeverity.Info,
            RestoreProposals.Title(entry),
            RestoreProposals.Summary,
            RestoreProposals.Body(entry),
            new AnswerSpec([
                new AnswerChoice(RestoreProposals.RestoreAnswer, RestoreProposals.RestoreAnswer),
                new AnswerChoice(RestoreProposals.KeepAnswer, RestoreProposals.KeepAnswer),
            ]),
            Origin: NotificationTools.Origin(server?.ClientInfo, sessionId));

        var id = (await notifications.PublishAsync(draft, cancellationToken).ConfigureAwait(false)).ValueOrThrow();

        return new NotificationPublishedPayload(id);
    }

    private IChangeJournal Journal() =>
        journal ?? throw RepositoryScope.Failure(Error.Unexpected(
            "change_journal.not_available", "This Backlog records no change journal."));
}

/// <summary>
/// The restore proposal's spelling (local ADR 0061 §5), in one place for the tool that asks
/// and the host's handler that answers: the two answers, the source key that carries the
/// change set's id, and the question's words.
/// </summary>
public static class RestoreProposals
{
    /// <summary>The source context a proposal is asked under: <c>mcp</c>, as for <c>ask</c>.</summary>
    public const string Source = NotificationTools.SourceContext;

    /// <summary>The answer that opens the Changes view on the change set's confirmation panel.</summary>
    public const string RestoreAnswer = "Restore";

    /// <summary>The answer that leaves everything as it is.</summary>
    public const string KeepAnswer = "Keep";

    /// <summary>What a proposal's source key starts with.</summary>
    internal const string KeyPrefix = "restore/";

    internal const string Summary =
        "Restore opens the Changes view, where you confirm what is restored. Keep leaves everything as it is.";

    /// <summary>The source key of one proposal: the change set's id, then a fresh id, so two
    /// proposals of one change set are two questions with an answer each.</summary>
    public static string Key(Guid changeSetId) =>
        KeyPrefix + changeSetId.ToString("D") + "/" + Guid.NewGuid().ToString("D");

    /// <summary>The change set a proposal's source key names, or null when the key is no
    /// proposal's — every other <c>ask</c> of the <c>mcp</c> source.</summary>
    public static Guid? ChangeSetOf(string? key)
    {
        if (key is null || !key.StartsWith(KeyPrefix, StringComparison.Ordinal)) return null;

        var rest = key[KeyPrefix.Length..];
        var slash = rest.IndexOf('/', StringComparison.Ordinal);

        return Guid.TryParse(slash < 0 ? rest : rest[..slash], out var id) ? id : null;
    }

    /// <summary>
    /// "Restore &lt;n&gt; changes made by &lt;caller&gt; (&lt;label&gt;)?", within the
    /// notification title's 80 characters: the label is shortened first, then the caller,
    /// each with an ellipsis, and the body carries both whole.
    /// </summary>
    internal static string Title(ChangeSetEntry entry)
    {
        const int max = 80;
        const string ellipsis = "…";

        var changes = entry.DocumentCount == 1 ? "1 change" : $"{entry.DocumentCount.ToString(CultureInfo.InvariantCulture)} changes";
        var who = Who(entry.ChangeSet);
        var label = string.IsNullOrWhiteSpace(entry.ChangeSet.Label) ? null : entry.ChangeSet.Label.Trim();

        string Compose(string by, string? what) =>
            what is null ? $"Restore {changes} made by {by}?" : $"Restore {changes} made by {by} ({what})?";

        var title = Compose(who, label);
        if (title.Length <= max) return title;

        if (label is not null)
        {
            var room = max - Compose(who, string.Empty).Length - ellipsis.Length;
            if (room >= 8) return Compose(who, label[..room].TrimEnd() + ellipsis);

            label = null;
            title = Compose(who, null);
            if (title.Length <= max) return title;
        }

        var callerRoom = max - Compose(string.Empty, null).Length - ellipsis.Length;
        return Compose(who[..Math.Max(1, callerRoom)].TrimEnd() + ellipsis, null);
    }

    /// <summary>The change set's details as Markdown, every value whole.</summary>
    internal static string Body(ChangeSetEntry entry)
    {
        var changeSet = entry.ChangeSet;
        var body = new StringBuilder();

        body.Append("An agent proposes restoring this change set. Nothing is restored until you confirm it in the Changes view.\n\n");
        body.Append("- **Change set:** `").Append(changeSet.Id.ToString("D")).Append("`\n");
        body.Append("- **Label:** ").Append(string.IsNullOrWhiteSpace(changeSet.Label) ? "none" : changeSet.Label.Trim()).Append('\n');
        body.Append("- **Made by:** ").Append(Who(changeSet)).Append('\n');
        if (!string.IsNullOrWhiteSpace(changeSet.SessionId)) body.Append("- **Session:** `").Append(changeSet.SessionId).Append("`\n");
        body.Append("- **Started:** ").Append(changeSet.StartedAt.ToString("u", CultureInfo.InvariantCulture)).Append('\n');
        body.Append("- **Documents:** ").Append(entry.DocumentCount.ToString(CultureInfo.InvariantCulture)).Append('\n');

        return body.ToString();
    }

    /// <summary>Who a change set's writes came from: an agent's caller, or the actor.</summary>
    internal static string Who(ChangeSet changeSet) =>
        !string.IsNullOrWhiteSpace(changeSet.Caller) ? changeSet.Caller.Trim() : changeSet.Actor switch
        {
            ChangeActor.Person => "you",
            ChangeActor.Agent => "an unnamed agent",
            ChangeActor.Sync => "sync",
            ChangeActor.System => "Backlog",
            _ => "an unknown writer",
        };
}

/// <summary>What <c>list_change_sets</c> answers.</summary>
/// <param name="ChangeSets">The change sets, newest first.</param>
public sealed record ChangeSetListPayload(IReadOnlyList<ChangeSetPayload> ChangeSets);

/// <summary>One change set as <c>list_change_sets</c> answers it.</summary>
/// <param name="Id">The id <c>propose_restore</c> takes.</param>
/// <param name="Label">What it is called, e.g. "transition on Ship the journal", or null.</param>
/// <param name="Actor">person, agent, sync, system or unknown.</param>
/// <param name="Caller">The MCP caller an agent's change set came from, or null.</param>
/// <param name="SessionId">The session an agent named, or null.</param>
/// <param name="StartedAt">When its first write was made.</param>
/// <param name="DocumentCount">How many documents it changed.</param>
/// <param name="Restored">True once a later change set restored it.</param>
/// <param name="RestoredBy">The change set that restored it, or null.</param>
public sealed record ChangeSetPayload(
    Guid Id,
    string? Label,
    string Actor,
    string? Caller,
    string? SessionId,
    DateTimeOffset StartedAt,
    int DocumentCount,
    bool Restored,
    Guid? RestoredBy)
{
    internal static ChangeSetPayload From(ChangeSetEntry entry) =>
        new(
            entry.ChangeSet.Id,
            entry.ChangeSet.Label,
            ChangeSet.ToWire(entry.ChangeSet.Actor),
            entry.ChangeSet.Caller,
            entry.ChangeSet.SessionId,
            entry.ChangeSet.StartedAt,
            entry.DocumentCount,
            entry.ChangeSet.UndoneBy is not null,
            entry.ChangeSet.UndoneBy);
}
