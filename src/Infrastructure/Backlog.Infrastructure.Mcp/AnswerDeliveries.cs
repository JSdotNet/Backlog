using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// One answer delivery as the Manager is given it (local ADR 0041 §3): the waiting
/// question's id, the session that asked, the question as it asked it and the answer the
/// owner accepted — nothing else it could act on. The wait route and the delivery tools
/// both answer with it.
/// </summary>
/// <param name="Kind">Always <c>delivery</c>, so one wait answer tells a delivery from a
/// session request.</param>
/// <param name="Id">The waiting question's id, which <c>claim_delivery</c> and
/// <c>mark_delivery</c> take.</param>
/// <param name="SessionId">The session that asked, as it named itself when it asked.</param>
/// <param name="DesktopSessionId">The Claude desktop app's id for that session — the id
/// <c>send_message</c> takes — while the desktop app holds it open; null when it holds no
/// record of it, or archived it. Null means there is nobody to send the answer to.</param>
/// <param name="Question">The question as the session asked it; several, numbered one per line.</param>
/// <param name="Answer">The accepted answer; several, numbered one per line.</param>
/// <param name="State"><c>pending</c>, <c>claimed</c> or <c>delivered</c>, as it reads now:
/// a claim whose lease ran out reads <c>pending</c>.</param>
/// <param name="ClaimedBy">The dispatcher holding it, while claimed.</param>
/// <param name="LeaseEndsAt">When the claim runs out, while claimed.</param>
/// <param name="AnsweredAt">When the answer was accepted.</param>
public sealed record AnswerDeliveryPayload(
    string Kind,
    Guid Id,
    string SessionId,
    string? DesktopSessionId,
    string Question,
    string Answer,
    string State,
    string? ClaimedBy,
    DateTimeOffset? LeaseEndsAt,
    DateTimeOffset? AnsweredAt)
{
    /// <summary>The <see cref="Kind"/> every delivery carries.</summary>
    public const string DeliveryKind = "delivery";

    /// <summary>The delivery of <paramref name="question"/> as it reads at <paramref name="now"/>.</summary>
    public static AnswerDeliveryPayload From(WaitingQuestion question, string? desktopSessionId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(question);

        var state = question.DeliveryAt(now);
        var claimed = state == AnswerDeliveryState.Claimed;
        var (asked, answer) = WaitingQuestions.DeliveryText(question);

        return new AnswerDeliveryPayload(
            DeliveryKind,
            question.Id,
            question.SessionId,
            desktopSessionId,
            asked,
            answer,
            state is { } known ? SessionRequestPayload.WireName(known) : "none",
            claimed ? question.DeliveryClaimedBy : null,
            claimed ? question.DeliveryLeaseEndsAt : null,
            question.EndedAt);
    }
}

/// <summary>What <c>claim_delivery</c> answered.</summary>
/// <param name="Claimed">Whether this dispatcher now holds the delivery under the
/// 2-minute lease. False when another holds it or it was delivered already.</param>
/// <param name="Refusal">Why it was not claimed, or null.</param>
/// <param name="Delivery">The delivery as it now stands.</param>
public sealed record DeliveryClaimPayload(bool Claimed, string? Refusal, AnswerDeliveryPayload Delivery);

/// <summary>What <c>mark_delivery</c> answered.</summary>
/// <param name="Outcome"><c>delivered</c> or <c>released</c>, as asked.</param>
/// <param name="Marked">Whether the delivery moved. False when this dispatcher no longer
/// holds the claim.</param>
/// <param name="Refusal">Why it did not move, or null.</param>
/// <param name="Delivery">The delivery as it now stands.</param>
public sealed record DeliveryMarkPayload(string Outcome, bool Marked, string? Refusal, AnswerDeliveryPayload Delivery);

/// <summary>What <c>list_deliveries</c> answered: the deliveries not yet delivered, oldest
/// answer first.</summary>
/// <param name="State">The state the list was narrowed to, or null for both.</param>
public sealed record DeliveriesPayload(string? State, int Count, IReadOnlyList<AnswerDeliveryPayload> Deliveries);

/// <summary>
/// Finds the Claude desktop app's id for the sessions answer deliveries are for, by
/// reading the session list once and keeping what it found for this instance's life — one
/// wait call, or one tool call. A session the desktop app holds no record of, or archived,
/// has none: a CLI session gets its answer only through the hook or in the session
/// (local ADR 0041 §3), and an ended one has nobody to send it to.
/// </summary>
/// <param name="sessions">The session list; null when the host composed none, and then no
/// session has a desktop id.</param>
public sealed class DesktopSessionIds(IAgentSessionSource? sessions)
{
    private readonly Dictionary<string, string?> _found = new(StringComparer.Ordinal);

    /// <summary>The desktop id of each of <paramref name="sessionIds"/>, reading the session
    /// list again only when one of them was not looked up before.</summary>
    public async Task<IReadOnlyDictionary<string, string?>> ResolveAsync(
        IEnumerable<string> sessionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        var wanted = sessionIds.Distinct(StringComparer.Ordinal).ToList();
        var unknown = wanted.Where(id => !_found.ContainsKey(id)).ToList();

        if (unknown.Count > 0)
        {
            var open = new Dictionary<string, string>(StringComparer.Ordinal);

            if (sessions is not null)
            {
                var catalog = await sessions.GetSessionsAsync(cancellationToken).ConfigureAwait(false);

                foreach (var session in catalog.Sessions)
                {
                    if (session.DesktopSessionId is { Length: > 0 } desktopId && session.Archived is not true)
                        open.TryAdd(session.Id, desktopId);
                }
            }

            foreach (var id in unknown) _found[id] = open.GetValueOrDefault(id);
        }

        return wanted.ToDictionary(id => id, id => _found[id], StringComparer.Ordinal);
    }
}
