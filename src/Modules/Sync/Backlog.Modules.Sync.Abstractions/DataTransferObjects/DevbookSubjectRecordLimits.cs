namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>
/// What a <see cref="DevbookSubjectChange"/> must be for the service to store
/// it: how long each field may be, which states each kind has, and
/// <see cref="WhyRefused"/>, the one rule both ends run.
/// <para>
/// Shared on <see cref="AnnotationRecordLimits"/>'s precedent: the service
/// refuses a push whole when one change in it breaks a rule, so the desktop
/// leaves such a change out before it batches, and two copies that drifted apart
/// would have a change the desktop thinks fits refuse every batch it is in, on
/// every cycle.
/// </para>
/// <para>
/// The kind names and the state rows are <c>devbook.decision@1</c>'s, written
/// down here a second time because this project may see nothing of the Devbook
/// module. The desktop's suite holds the two copies against each other, so the
/// module growing a state the wire does not know fails a test instead of every
/// push that carries it.
/// </para>
/// </summary>
public static class DevbookSubjectRecordLimits
{
    /// <summary>The longest repository alias, as for a remark.</summary>
    public const int RepositoryAliasLength = AnnotationRecordLimits.RepositoryAliasLength;

    /// <summary>The longest key — a change's folder name, or a record's
    /// repository path, bounded as a remark's chapter path is.</summary>
    public const int KeyLength = AnnotationRecordLimits.ChapterPathLength;

    /// <summary>The longest <c>by</c> label, on the state and on each decision —
    /// a machine name or a person's name, bounded as a remark's author is.</summary>
    public const int ByLength = AnnotationRecordLimits.AuthorLength;

    private const string FingerprintPrefix = "sha256:";

    private static readonly IReadOnlyList<string> ProposalStates = ["draft", "approved", "accepted"];
    private static readonly IReadOnlyList<string> DecisionStates = ["proposed", "decided", "declined"];
    private static readonly IReadOnlyList<string> DebtStates = ["identified", "planned", "in-progress", "resolved"];

    /// <summary>The states the contract closes for a kind, by its wire name, or
    /// none for a name the contract does not write.</summary>
    public static IReadOnlyList<string> StatesOf(string? kind) => kind switch
    {
        "proposal" => ProposalStates,
        "decision" => DecisionStates,
        "debt" => DebtStates,
        _ => [],
    };

    /// <summary>
    /// Why the service would refuse <paramref name="change"/>, or null when it
    /// would store it. The service answers the reason as the detail of its
    /// <see cref="SyncErrorCodes.DevbookSubjectInvalid"/> refusal; the desktop
    /// names it in the warning that says it left the change out.
    /// <para>
    /// A change no device could file — no alias, a kind or a state the contract
    /// does not have, a key the contract could not name — is refused, and so is
    /// a decision on anything but a proposal or a fingerprint that is not one: a
    /// device applying either would hold a state its own rules could never have
    /// produced. The bounds stop a caller posting a megabyte into durable,
    /// per-request-billed storage.
    /// </para>
    /// <para>
    /// A reason names the field and never repeats the caller's value, as
    /// <see cref="AnnotationRecordLimits.WhyRefused"/> never does: the kind, the
    /// state and a fingerprint are checked before any length is, so an echoed
    /// value could be as long as the request body, and the reason goes back in the
    /// <c>400</c> and into the desktop's warning. The kind is echoed only once it
    /// is known to be one of the three names.
    /// </para>
    /// </summary>
    public static string? WhyRefused(DevbookSubjectChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        // Null only when a caller's JSON left it out; the type says otherwise.
        if (change.Subject is null)
        {
            return "A devbook subject change needs the subject it changes.";
        }

        if (change.Id == Guid.Empty)
        {
            return "A devbook subject change needs the id of the state it changes.";
        }

        var subject = change.Subject;

        if (string.IsNullOrWhiteSpace(subject.RepositoryAlias))
        {
            return "A devbook subject needs the alias of the repository it is in.";
        }

        var states = StatesOf(subject.Kind);
        if (states.Count == 0)
        {
            return "A devbook subject's kind is proposal, decision or debt, spelled in lower case.";
        }

        if (string.IsNullOrWhiteSpace(subject.Key))
        {
            return "A devbook subject needs its change's name or its record's path.";
        }

        var isDecision = subject.Kind == "decision";
        if (isDecision && subject.PullRequest is not > 0)
        {
            return "A decision record's state is keyed by the pull request it is decided in.";
        }

        if (!isDecision && subject.PullRequest is not null)
        {
            return "Only a decision record's state is keyed by a pull request.";
        }

        if (!states.Contains(subject.State, StringComparer.Ordinal))
        {
            return $"A {subject.Kind}'s state is one of {string.Join(", ", states)}.";
        }

        if (string.IsNullOrWhiteSpace(subject.By))
        {
            return "A devbook subject's state needs who set it.";
        }

        if (subject.Kind != "proposal" && (subject.Approval is not null || subject.Acceptance is not null))
        {
            return "Only a proposal is approved or accepted.";
        }

        return WhyDecisionRefused("approval", subject.Approval)
            ?? WhyDecisionRefused("acceptance", subject.Acceptance)
            ?? TooLong("repository alias", subject.RepositoryAlias, RepositoryAliasLength)
            ?? TooLong("key", subject.Key, KeyLength)
            ?? TooLong("by", subject.By, ByLength);
    }

    private static string? WhyDecisionRefused(string name, DevbookSubjectDecisionPayload? decision)
    {
        if (decision is null) return null;

        if (string.IsNullOrWhiteSpace(decision.By))
        {
            return $"A proposal's {name} needs who gave it.";
        }

        if (!IsFingerprint(decision.Fingerprint))
        {
            return $"A proposal's {name} fingerprint is 'sha256:' and eight lowercase hex characters.";
        }

        return TooLong($"{name} by", decision.By, ByLength);
    }

    /// <summary><c>^sha256:[0-9a-f]{8}$</c>, the shape <c>DevbookSubjectRules</c>
    /// holds an approval to, checked by hand so this project needs no
    /// regex.</summary>
    private static bool IsFingerprint(string? value) =>
        value is { Length: 15 }
        && value.StartsWith(FingerprintPrefix, StringComparison.Ordinal)
        && value.AsSpan(FingerprintPrefix.Length).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static string? TooLong(string field, string? value, int limit) =>
        value is not null && value.Length > limit
            ? $"A devbook subject's {field} may be at most {limit} characters; this one was {value.Length}."
            : null;
}
