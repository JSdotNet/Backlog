using System.Text.RegularExpressions;

namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// The rules of <c>devbook.decision@1</c>'s four state operations, as one pure
/// copy every store applies — the file store, the session fallback, and anything
/// that answers the contract later.
/// <para>
/// Pure so that a store is only a place to keep the answer: each operation takes
/// the subject's current state (null when the store keeps none), who is acting and
/// when, and hands back the state to write or the refusal. A refusal carries no
/// state, so a store that writes only what comes back writes nothing on a refusal.
/// <paramref name="by"/> and the time are the host's — its author and its clock —
/// never a caller's, which is the contract's rule and the reason neither is an
/// argument of the MCP tools.
/// </para>
/// </summary>
public static partial class DevbookSubjectRules
{
    private const string Draft = "draft";
    private const string Approved = "approved";
    private const string Accepted = "accepted";

    /// <summary>
    /// Sets a subject's state to one in its kind's row.
    /// <para>
    /// A proposal's only way to <c>approved</c> and <c>accepted</c> is
    /// <see cref="Approve"/> and <see cref="Accept"/>, so neither can be set
    /// here; that leaves <c>draft</c>, which withdraws both decisions — the person
    /// has taken the change back. A decision or debt record has no approval to
    /// keep, so its state simply moves.
    /// </para>
    /// </summary>
    public static DevbookSubjectOutcome SetState(
        DevbookSubjectState? current,
        string? repositoryAlias,
        DevbookSubject subject,
        string state,
        string by,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(by);

        if (subject.Kind == DevbookSubjectKind.Proposal && state is Approved or Accepted)
        {
            return DevbookSubjectOutcome.Refuse(
                DevbookSubjectRefusal.BadState,
                $"A proposal reaches '{state}' through devbook_{(state == Approved ? "approve" : "accept")}, not set_state.");
        }

        if (!subject.States.Contains(state ?? string.Empty, StringComparer.Ordinal))
        {
            return DevbookSubjectOutcome.Refuse(
                DevbookSubjectRefusal.BadState,
                $"'{state}' is no state of a {Name(subject.Kind)}; it is one of {string.Join(", ", subject.States)}.");
        }

        var next = Start(current, repositoryAlias, subject, at) with { State = state!, By = by, At = at, UpdatedAt = at };

        return DevbookSubjectOutcome.Done(subject.Kind == DevbookSubjectKind.Proposal
            ? next with { Approval = null, Acceptance = null }
            : next);
    }

    /// <summary>Approves a proposal at a fingerprint of the change. Approving
    /// again replaces the approval and withdraws any acceptance, which stood on
    /// the approval it replaces.</summary>
    public static DevbookSubjectOutcome Approve(
        DevbookSubjectState? current,
        string? repositoryAlias,
        DevbookSubject subject,
        string fingerprint,
        string by,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(by);

        if (Refusal(subject, fingerprint, "approve") is { } refused) return refused;

        var decision = new DevbookSubjectDecision(by, at, fingerprint);

        return DevbookSubjectOutcome.Done(Start(current, repositoryAlias, subject, at) with
        {
            State = Approved,
            By = by,
            At = at,
            Approval = decision,
            Acceptance = null,
            UpdatedAt = at
        });
    }

    /// <summary>Accepts a proposal, standing on an approval at the same
    /// fingerprint: with none it refuses <c>not-approved</c>, with one at another
    /// fingerprint <c>fingerprint-mismatch</c> — the change moved since it was
    /// approved, and the person has to approve what it now says first.</summary>
    public static DevbookSubjectOutcome Accept(
        DevbookSubjectState? current,
        string? repositoryAlias,
        DevbookSubject subject,
        string fingerprint,
        string by,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(by);

        if (Refusal(subject, fingerprint, "accept") is { } refused) return refused;

        if (current?.Approval is not { } approval)
        {
            return DevbookSubjectOutcome.Refuse(DevbookSubjectRefusal.NotApproved, $"'{subject.Key}' has no approval to accept.");
        }

        if (!string.Equals(approval.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return DevbookSubjectOutcome.Refuse(
                DevbookSubjectRefusal.FingerprintMismatch,
                $"'{subject.Key}' was approved at {approval.Fingerprint}, not {fingerprint}.");
        }

        return DevbookSubjectOutcome.Done(current with
        {
            State = Accepted,
            By = by,
            At = at,
            Acceptance = new DevbookSubjectDecision(by, at, fingerprint),
            UpdatedAt = at
        });
    }

    /// <summary>
    /// Whether a copy that arrived from another device replaces the one this
    /// device holds: when it holds none, or the arriving one is stamped strictly
    /// later. A tie is the same version, or a race nobody can order, and keeps
    /// what is held — the rule the replica applies to a push.
    /// <para>
    /// Here, beside the operations, so every store applies it inside its own
    /// write: the check and the write have to be one step, or a state set on this
    /// device between them is overwritten by the older copy.
    /// </para>
    /// </summary>
    public static bool Supersedes(DevbookSubjectState arriving, DevbookSubjectState? held)
    {
        ArgumentNullException.ThrowIfNull(arriving);

        return held is null || arriving.UpdatedAt > held.UpdatedAt;
    }

    /// <summary>Whether a value is a <c>devbook.chapter.fingerprint@1</c>:
    /// <c>sha256:</c> and eight lowercase hex characters.</summary>
    public static bool IsFingerprint(string? value) => value is not null && FingerprintPattern().IsMatch(value);

    /// <summary>What approve and accept refuse before they look at any state.</summary>
    private static DevbookSubjectOutcome? Refusal(DevbookSubject subject, string fingerprint, string operation)
    {
        if (subject.Kind != DevbookSubjectKind.Proposal)
        {
            return DevbookSubjectOutcome.Refuse(
                DevbookSubjectRefusal.BadState,
                $"Only a proposal can be {operation}d; this is a {Name(subject.Kind)}.");
        }

        return IsFingerprint(fingerprint)
            ? null
            : DevbookSubjectOutcome.Refuse(
                DevbookSubjectRefusal.BadState,
                $"'{fingerprint}' is no fingerprint: one is 'sha256:' and eight lowercase hex characters.");
    }

    /// <summary>The state an operation starts from: what the store keeps, or a
    /// fresh document under the subject's derived id.</summary>
    private static DevbookSubjectState Start(DevbookSubjectState? current, string? repositoryAlias, DevbookSubject subject, DateTimeOffset at)
    {
        var alias = DevbookSubject.AliasKey(repositoryAlias);

        return current ?? new DevbookSubjectState(
            DevbookSubject.IdFor(alias, subject),
            alias,
            subject,
            string.Empty,
            string.Empty,
            at,
            Approval: null,
            Acceptance: null,
            UpdatedAt: at);
    }

    private static string Name(DevbookSubjectKind kind) => kind switch
    {
        DevbookSubjectKind.Proposal => "proposal",
        DevbookSubjectKind.Decision => "decision record",
        DevbookSubjectKind.Debt => "debt record",
    };

    // \z rather than $: a value with a trailing newline is not a fingerprint.
    [GeneratedRegex(@"^sha256:[0-9a-f]{8}\z", RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintPattern();
}
