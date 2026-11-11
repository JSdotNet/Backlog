using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UI;

/// <summary>
/// What a Waiting row's "Asks" strip shows: the first question of the session's open
/// <see cref="WaitingQuestion"/> that still has no answer, how many more of that dialog are
/// still open, and the notification that question was published as — null while the
/// question is still InSession, when there is nothing in the notification centre to open.
/// </summary>
/// <param name="Question">The question as the session wrote it.</param>
/// <param name="More">How many other questions of the same dialog are still unanswered.</param>
/// <param name="NotificationId">The question's own notification, once it is Notified.</param>
public sealed record SessionAsk(string Question, int More, Guid? NotificationId)
{
    /// <summary>
    /// The ask for each session holding an open question, by session id. A session with
    /// several open questions shows the one it asked first: <paramref name="open"/> comes
    /// oldest first, as the store lists it.
    /// </summary>
    public static IReadOnlyDictionary<string, SessionAsk> BySession(IEnumerable<WaitingQuestion> open)
    {
        ArgumentNullException.ThrowIfNull(open);

        var asks = new Dictionary<string, SessionAsk>(StringComparer.Ordinal);

        foreach (var question in open.OrderBy(question => question.AskedAt))
        {
            if (asks.ContainsKey(question.SessionId)) continue;
            if (Of(question) is { } ask) asks[question.SessionId] = ask;
        }

        return asks;
    }

    /// <summary>The ask <paramref name="question"/> makes, or null when it is no longer
    /// open or every one of its questions has an answer.</summary>
    public static SessionAsk? Of(WaitingQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);

        if (!question.IsOpen) return null;

        var unanswered = new List<int>(question.Questions.Count);
        for (var index = 0; index < question.Questions.Count; index++)
        {
            if (index >= question.Answers.Count || question.Answers[index] is null) unanswered.Add(index);
        }

        if (unanswered.Count == 0) return null;

        var first = unanswered[0];
        Guid? notification = question.State == WaitingQuestionState.Notified && first < question.NotificationIds.Count
            ? question.NotificationIds[first]
            : null;

        return new SessionAsk(question.Questions[first].Question, unanswered.Count - 1, notification);
    }
}
