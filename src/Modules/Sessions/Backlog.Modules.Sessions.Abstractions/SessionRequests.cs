namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>What a <see cref="SessionRequest"/> asks a session to run (local ADR 0037 §2).</summary>
public enum SessionRequestKind
{
    /// <summary>One prompt entry, briefed as <c>/backlog-tools:run-plan-item entry `&lt;id&gt;`:</c>.</summary>
    Entry,

    /// <summary>A whole plan, briefed as <c>/backlog-tools:execute-plan +&lt;tag&gt;</c>.</summary>
    Plan
}

/// <summary>Where a <see cref="SessionRequest"/> stands (local ADR 0037 §3).</summary>
public enum SessionRequestState
{
    /// <summary>Waiting for a dispatcher.</summary>
    Requested,

    /// <summary>One dispatcher holds it under a lease.</summary>
    Claimed,

    /// <summary>The dispatcher reported the session it started.</summary>
    Started,

    /// <summary>That session called <c>link_session</c>. Final.</summary>
    Linked,

    /// <summary>The person cancelled it. Final.</summary>
    Cancelled
}

/// <summary>
/// A person's ask for a session that runs one entry or one plan — the record of the
/// Sessions context's Session Request Queue (local ADR 0037).
/// <para>
/// <b>It carries nothing of the entry's text</b>: no title, body or note, so no field
/// here can become a brief. The brief is fixed by <see cref="Kind"/> and the id alone.
/// </para>
/// <para>
/// <see cref="State"/> is the state as stored. A Claimed request whose lease has run
/// out still reads Claimed there, so every reader asks <see cref="StateAt"/> instead:
/// a lapsed claim reads as Requested again.
/// </para>
/// </summary>
/// <param name="Id">The request's own id.</param>
/// <param name="Kind">An entry request or a plan request.</param>
/// <param name="EntryId">The entry an entry request runs; null on a plan request.</param>
/// <param name="PlanTag">The tag of the plan a plan request runs, without its sigil;
/// null on an entry request.</param>
/// <param name="Repository">The repository, as <c>owner/name</c>.</param>
/// <param name="CloneFolder">That repository's clone folder on this machine.</param>
/// <param name="State">The state as stored; read it through <see cref="StateAt"/>.</param>
/// <param name="ClaimedBy">The dispatcher that claimed it, once claimed.</param>
/// <param name="LeaseEndsAt">When the claim's lease runs out, while Claimed.</param>
/// <param name="SessionId">The session the dispatcher reported, once Started.</param>
/// <param name="CreatedAt">When the person asked; the queue hands out the oldest first.</param>
public sealed record SessionRequest(
    Guid Id,
    SessionRequestKind Kind,
    Guid? EntryId,
    string? PlanTag,
    string Repository,
    string CloneFolder,
    SessionRequestState State,
    string? ClaimedBy,
    DateTimeOffset? LeaseEndsAt,
    string? SessionId,
    DateTimeOffset CreatedAt)
{
    /// <summary>The state the request reads as at <paramref name="now"/>: a claim whose
    /// lease has ended reads as Requested.</summary>
    public SessionRequestState StateAt(DateTimeOffset now) =>
        State == SessionRequestState.Claimed && (LeaseEndsAt is not { } end || end <= now)
            ? SessionRequestState.Requested
            : State;

    /// <summary>Whether the request is still waiting to be answered at
    /// <paramref name="now"/> — Requested, Claimed or Started — and so is the one a
    /// second ask for the same entry or plan gets back.</summary>
    public bool IsOpenAt(DateTimeOffset now) =>
        StateAt(now) is SessionRequestState.Requested or SessionRequestState.Claimed or SessionRequestState.Started;

    /// <summary>Whether this request asks for the same entry or plan as
    /// <paramref name="other"/>.</summary>
    public bool HasSameTargetAs(SessionRequest other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Kind == other.Kind && Kind switch
        {
            SessionRequestKind.Entry => EntryId == other.EntryId,
            _ => string.Equals(PlanTag, other.PlanTag, StringComparison.OrdinalIgnoreCase)
        };
    }
}

/// <summary>What one operation on a request did: the request as it now stands, or
/// the reason it was refused and the request as it was.</summary>
/// <param name="Request">The request after the operation, or unchanged when refused;
/// null when no request has the id asked for.</param>
/// <param name="Refusal">Why the operation was refused, or null when it succeeded.</param>
public sealed record SessionRequestOutcome(SessionRequest? Request, string? Refusal)
{
    public bool Succeeded => Refusal is null;

    public static SessionRequestOutcome Done(SessionRequest request) => new(request, null);

    public static SessionRequestOutcome Refused(SessionRequest? request, string refusal) => new(request, refusal);
}

/// <summary>
/// The Session Request's states and transitions (local ADR 0037 §3), as pure
/// functions of the request and the clock. The store applies them inside one write,
/// so a claim checks and moves the request in the same transaction.
/// <code>
/// Requested → Claimed     a dispatcher claims it (2-minute lease)
/// Claimed   → Requested   the lease runs out, or the dispatcher releases it
/// Claimed   → Started     the dispatcher reports the session
/// Started   → Linked      that session calls link_session
/// Requested → Cancelled   the person cancels it
/// Claimed   → Cancelled   the person cancels it
/// </code>
/// No transition expires a request: it waits until it is answered or cancelled.
/// </summary>
public static class SessionRequests
{
    /// <summary>How long a claim holds a request before it reads as Requested again.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The brief a dispatcher starts the session with (local ADR 0037 §2): the fixed
    /// <c>run-plan-item</c> line for an entry request, the fixed <c>execute-plan</c> line
    /// for a plan request. Built from the kind and the id alone, so no entry text can
    /// become one; a dispatcher passes it on and never writes its own.
    /// <para>
    /// A plan tag is the one piece of a brief that started as text a person typed, so it
    /// is checked against the tag grammar here as well as when the request is made: a
    /// row that holds anything else — a space, a backtick, a line break — gets no brief.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The request has no entry id or no
    /// well-formed plan tag for its kind.</exception>
    public static string BriefFor(SessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Kind switch
        {
            SessionRequestKind.Entry when request.EntryId is { } entryId && entryId != Guid.Empty =>
                $"/backlog-tools:run-plan-item entry `{entryId:D}`:",
            SessionRequestKind.Plan when IsPlanTag(request.PlanTag) =>
                $"/backlog-tools:execute-plan +{request.PlanTag}",
            _ => throw new InvalidOperationException(
                $"Session request {request.Id:D} has no well-formed target for a {request.Kind} brief.")
        };
    }

    /// <summary>Whether <paramref name="tag"/>, without its sigil, is a plan tag as the
    /// entry grammar writes one: a letter, then letters, digits, <c>_</c> or <c>-</c>.</summary>
    public static bool IsPlanTag(string? tag) =>
        !string.IsNullOrEmpty(tag)
        && char.IsAsciiLetter(tag[0])
        && tag.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');

    /// <summary>A new entry request, Requested.</summary>
    public static SessionRequest ForEntry(Guid entryId, string repository, string cloneFolder, DateTimeOffset now)
    {
        if (entryId == Guid.Empty) throw new ArgumentException("An entry request needs the entry's id.", nameof(entryId));

        return New(SessionRequestKind.Entry, entryId, null, repository, cloneFolder, now);
    }

    /// <summary>A new plan request, Requested. The tag is kept without its <c>+</c> or
    /// <c>#</c> sigil, so <c>+plan</c> and <c>plan</c> ask for the same plan.</summary>
    public static SessionRequest ForPlan(string planTag, string repository, string cloneFolder, DateTimeOffset now)
    {
        var tag = NormalizeTag(planTag);

        if (tag.Length == 0) throw new ArgumentException("A plan request needs the plan's tag.", nameof(planTag));
        if (!IsPlanTag(tag)) throw new ArgumentException($"'{tag}' is not a plan tag: a letter, then letters, digits, '_' or '-'.", nameof(planTag));

        return New(SessionRequestKind.Plan, null, tag, repository, cloneFolder, now);
    }

    /// <summary>A plan tag without its sigil and surrounding space.</summary>
    public static string NormalizeTag(string? tag) => (tag ?? string.Empty).Trim().TrimStart('+', '#').Trim();

    /// <summary>Requested → Claimed by <paramref name="dispatcher"/> until
    /// <see cref="Lease"/> from <paramref name="now"/>. Refused on any other state, so
    /// two dispatchers never both hold one request.</summary>
    public static SessionRequestOutcome Claim(SessionRequest request, string dispatcher, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        var who = Required(dispatcher, nameof(dispatcher));
        var state = request.StateAt(now);

        if (state != SessionRequestState.Requested)
        {
            return SessionRequestOutcome.Refused(request, $"The request is {state}, and only a Requested request can be claimed.");
        }

        return SessionRequestOutcome.Done(request with
        {
            State = SessionRequestState.Claimed,
            ClaimedBy = who,
            LeaseEndsAt = now + Lease
        });
    }

    /// <summary>Claimed → Requested, given up early by the dispatcher holding the
    /// claim — what a lapsed lease does, without the wait.</summary>
    public static SessionRequestOutcome Release(SessionRequest request, string dispatcher, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        var who = Required(dispatcher, nameof(dispatcher));

        if (HeldBy(request, who, now) is { } refusal) return SessionRequestOutcome.Refused(request, refusal);

        return SessionRequestOutcome.Done(Unclaimed(request));
    }

    /// <summary>Claimed → Started, reported by the dispatcher holding the claim with
    /// the session it started. Refused once the lease has run out: the request is
    /// Requested again and may already be another dispatcher's.</summary>
    public static SessionRequestOutcome Report(SessionRequest request, string dispatcher, string sessionId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        var who = Required(dispatcher, nameof(dispatcher));
        var session = Required(sessionId, nameof(sessionId));

        if (HeldBy(request, who, now) is { } refusal) return SessionRequestOutcome.Refused(request, refusal);

        return SessionRequestOutcome.Done(request with
        {
            State = SessionRequestState.Started,
            LeaseEndsAt = null,
            SessionId = session
        });
    }

    /// <summary>Started → Linked, when <paramref name="sessionId"/> — the session the
    /// dispatcher reported — calls <c>link_session</c> on this request's entry, or for a
    /// plan request on an entry carrying its plan's tag. Answers null when the call
    /// does not link this request.</summary>
    /// <param name="request">The request.</param>
    /// <param name="entryId">The entry the session linked itself to.</param>
    /// <param name="planTags">The plan tags that entry carries, with or without sigil.</param>
    /// <param name="sessionId">The session that linked itself.</param>
    /// <param name="now">The clock.</param>
    public static SessionRequest? Link(
        SessionRequest request,
        Guid entryId,
        IEnumerable<string> planTags,
        string sessionId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(planTags);

        if (request.StateAt(now) != SessionRequestState.Started) return null;
        if (!string.Equals(request.SessionId, (sessionId ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)) return null;

        var answers = request.Kind switch
        {
            SessionRequestKind.Entry => request.EntryId == entryId,
            _ => planTags.Any(tag => string.Equals(NormalizeTag(tag), request.PlanTag, StringComparison.OrdinalIgnoreCase))
        };

        return answers ? request with { State = SessionRequestState.Linked } : null;
    }

    /// <summary>Requested or Claimed → Cancelled, by the person. A Started request
    /// already has its session, and Linked and Cancelled are final.</summary>
    public static SessionRequestOutcome Cancel(SessionRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = request.StateAt(now);

        if (state is not (SessionRequestState.Requested or SessionRequestState.Claimed))
        {
            return SessionRequestOutcome.Refused(request, $"The request is {state}, and only a Requested or Claimed request can be cancelled.");
        }

        return SessionRequestOutcome.Done(Unclaimed(request) with { State = SessionRequestState.Cancelled });
    }

    /// <summary>The open request among <paramref name="held"/> that asks for the same
    /// entry or plan as <paramref name="candidate"/>, or null — a second ask returns it
    /// rather than queueing a duplicate.</summary>
    public static SessionRequest? OpenDuplicateOf(SessionRequest candidate, IEnumerable<SessionRequest> held, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(held);

        return held
            .Where(request => request.IsOpenAt(now) && request.HasSameTargetAs(candidate))
            .OrderBy(request => request.CreatedAt)
            .FirstOrDefault();
    }

    /// <summary>The Requested requests among <paramref name="held"/>, a lapsed claim
    /// included, oldest first: the order a dispatcher is handed them in.</summary>
    public static IReadOnlyList<SessionRequest> Waiting(IEnumerable<SessionRequest> held, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(held);

        return
        [
            .. held
                .Where(request => request.StateAt(now) == SessionRequestState.Requested)
                .OrderBy(request => request.CreatedAt)
                .ThenBy(request => request.Id)
        ];
    }

    private static SessionRequest New(SessionRequestKind kind, Guid? entryId, string? tag, string repository, string cloneFolder, DateTimeOffset now) =>
        new(
            Guid.NewGuid(),
            kind,
            entryId,
            tag,
            Required(repository, nameof(repository)),
            Required(cloneFolder, nameof(cloneFolder)),
            SessionRequestState.Requested,
            ClaimedBy: null,
            LeaseEndsAt: null,
            SessionId: null,
            now);

    private static string? HeldBy(SessionRequest request, string dispatcher, DateTimeOffset now)
    {
        var state = request.StateAt(now);

        if (state != SessionRequestState.Claimed)
        {
            return request.State == SessionRequestState.Claimed
                ? "The claim's lease has run out, so the request is Requested again."
                : $"The request is {state}, not Claimed.";
        }

        return string.Equals(request.ClaimedBy, dispatcher, StringComparison.Ordinal)
            ? null
            : "Another dispatcher holds the claim.";
    }

    private static SessionRequest Unclaimed(SessionRequest request) =>
        request with { State = SessionRequestState.Requested, ClaimedBy = null, LeaseEndsAt = null };

    private static string Required(string? value, string name)
    {
        var trimmed = (value ?? string.Empty).Trim();

        return trimmed.Length == 0 ? throw new ArgumentException($"{name} is required.", name) : trimmed;
    }
}

/// <summary>
/// PORT — this machine's session request queue (local ADR 0037). One row per request in
/// the app's local database; it does not replicate.
/// <para>
/// Every operation reads and writes in one transaction, so a claim checks the state
/// and moves it in the same write. Every read treats a lapsed claim as Requested.
/// Nothing here expires or deletes a request.
/// </para>
/// </summary>
public interface ISessionRequestStore
{
    /// <summary>Queues <paramref name="candidate"/>, or answers the request already open
    /// for the same entry or plan — Requested, Claimed or Started — and stores nothing.
    /// Called only by the app's own Start session action.</summary>
    Task<SessionRequest> RequestAsync(SessionRequest candidate, CancellationToken cancellationToken = default);

    /// <summary>The request with <paramref name="id"/>, or null.</summary>
    Task<SessionRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every request, or those reading as <paramref name="state"/> now, oldest first.</summary>
    Task<IReadOnlyList<SessionRequest>> ListAsync(SessionRequestState? state = null, CancellationToken cancellationToken = default);

    /// <summary>The oldest request reading as Requested now, or null.</summary>
    Task<SessionRequest?> NextAsync(CancellationToken cancellationToken = default);

    /// <summary><see cref="SessionRequests.Claim"/>, in one write.</summary>
    Task<SessionRequestOutcome> ClaimAsync(Guid id, string dispatcher, CancellationToken cancellationToken = default);

    /// <summary><see cref="SessionRequests.Release"/>, in one write.</summary>
    Task<SessionRequestOutcome> ReleaseAsync(Guid id, string dispatcher, CancellationToken cancellationToken = default);

    /// <summary><see cref="SessionRequests.Report"/>, in one write.</summary>
    Task<SessionRequestOutcome> ReportStartedAsync(Guid id, string dispatcher, string sessionId, CancellationToken cancellationToken = default);

    /// <summary><see cref="SessionRequests.Cancel"/>, in one write.</summary>
    Task<SessionRequestOutcome> CancelAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary><see cref="SessionRequests.Link"/> over every Started request, in one
    /// write: what <c>link_session</c> calls once it has linked
    /// <paramref name="sessionId"/> to <paramref name="entryId"/>. Answers the requests
    /// it linked.</summary>
    Task<IReadOnlyList<SessionRequest>> LinkAsync(
        Guid entryId,
        IReadOnlyCollection<string> planTags,
        string sessionId,
        CancellationToken cancellationToken = default);
}
