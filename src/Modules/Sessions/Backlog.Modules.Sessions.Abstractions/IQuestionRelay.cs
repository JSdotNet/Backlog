namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// PORT — the Sessions context's question relay as the MCP <c>ask</c>, <c>get_answer</c> and
/// <c>close</c> tools reach it (<c>.devbook/domain/sessions/domain.md#question-relay</c>, local
/// ADR 0041). The desktop composes one over its waiting questions; a host that composes none
/// leaves the tools on their Notifications-only behaviour.
/// </summary>
public interface IQuestionRelay
{
    /// <summary>
    /// Registers a question <paramref name="sessionId"/> asked: a dialog of one to four questions
    /// when <paramref name="toolUseId"/> is given, else one free-text question. A second call for
    /// the same session and tool use answers the question already registered.
    /// </summary>
    Task<QuestionRelayResult> AskAsync(
        string sessionId,
        string? toolUseId,
        IReadOnlyList<RelayQuestion> questions,
        CancellationToken cancellationToken = default);

    /// <summary>The waiting question with <paramref name="id"/>, or null.</summary>
    Task<WaitingQuestion?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the dialog's hook asked for the answer now, then holds until the question is
    /// answered or closed, or <paramref name="wait"/> passes — awaiting a completion source an
    /// answer or a close completes, never polling. Answers the question as it then stands, or
    /// null for an unknown id. A zero <paramref name="wait"/> records the poll and answers at once.
    /// </summary>
    Task<WaitingQuestion?> WaitAsync(Guid id, TimeSpan wait, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes an open question with <paramref name="reason"/> — answered in the session with
    /// <paramref name="answers"/>, one per question; timed out; or session ended.
    /// </summary>
    Task<QuestionRelayResult> CloseAsync(
        Guid id,
        WaitingQuestionCloseReason reason,
        IReadOnlyList<RelayAnswer>? answers = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What one relay call did: the question as it now stands, or the refusal's code and message
/// (for example <c>relay.questions_invalid</c> or <c>notifications.not_available</c>).
/// </summary>
/// <param name="Question">The question, when the call succeeded.</param>
/// <param name="Code">The refusal's code, or null when it succeeded.</param>
/// <param name="Message">The refusal in words, or null when it succeeded.</param>
public sealed record QuestionRelayResult(WaitingQuestion? Question, string? Code = null, string? Message = null)
{
    /// <summary>Whether the call succeeded.</summary>
    public bool Succeeded => Code is null && Question is not null;

    /// <summary>A success.</summary>
    public static QuestionRelayResult Of(WaitingQuestion question) => new(question);

    /// <summary>A refusal.</summary>
    public static QuestionRelayResult Refused(string code, string message) => new(null, code, message);
}
