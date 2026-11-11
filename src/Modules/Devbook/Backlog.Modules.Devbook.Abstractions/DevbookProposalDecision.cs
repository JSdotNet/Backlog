namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// A person's approval or acceptance of a change proposal: the open-question check,
/// then the store's own rules (<see cref="DevbookSubjectRules.Approve"/>,
/// <see cref="DevbookSubjectRules.Accept"/>).
/// <para>
/// One use case for every caller, so the devbook view's Approve and Accept buttons
/// refuse exactly what the retired <c>devbook_approve</c> and <c>devbook_accept</c>
/// tools refused. Local ADR 0052 §7 takes both decisions off MCP: an agent holding
/// the token could otherwise approve its own proposal under the person's name. So
/// nothing on the MCP path calls this.
/// </para>
/// </summary>
public static class DevbookProposalDecision
{
    /// <summary>Approves <paramref name="subject"/> at
    /// <paramref name="fingerprint"/>, or answers <c>open-question</c> when the
    /// change has an open question note and writes nothing.</summary>
    public static DevbookSubjectOutcome Approve(
        IDevbookSubjectStateStore subjects,
        IDevbookAnnotationStore annotations,
        string? repositoryAlias,
        DevbookSubject subject,
        string fingerprint,
        string by)
    {
        ArgumentNullException.ThrowIfNull(subjects);

        return OpenQuestion(annotations, repositoryAlias, subject)
            ?? subjects.Approve(repositoryAlias, subject, fingerprint, by);
    }

    /// <summary>Accepts <paramref name="subject"/> at
    /// <paramref name="fingerprint"/>, or answers <c>open-question</c> when the
    /// change has an open question note and writes nothing.</summary>
    public static DevbookSubjectOutcome Accept(
        IDevbookSubjectStateStore subjects,
        IDevbookAnnotationStore annotations,
        string? repositoryAlias,
        DevbookSubject subject,
        string fingerprint,
        string by)
    {
        ArgumentNullException.ThrowIfNull(subjects);

        return OpenQuestion(annotations, repositoryAlias, subject)
            ?? subjects.Accept(repositoryAlias, subject, fingerprint, by);
    }

    private static DevbookSubjectOutcome? OpenQuestion(IDevbookAnnotationStore annotations, string? repositoryAlias, DevbookSubject subject)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        ArgumentNullException.ThrowIfNull(subject);

        return DevbookSubjectRules.OpenQuestion(repositoryAlias, subject, annotations.ListChangedSince(DateTimeOffset.MinValue));
    }
}
