using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Tests;

/// <summary>Waiting questions held in memory, applying each transition in one step the way
/// the SQLite store applies it in one write. <see cref="Add"/> puts a question in as it is,
/// so a test can hold one in any state — an answer waiting as a delivery, say.</summary>
internal sealed class HeldWaitingQuestions : IWaitingQuestionStore
{
    private readonly Dictionary<Guid, WaitingQuestion> _held = [];
    private readonly Lock _gate = new();

    public WaitingQuestion Add(WaitingQuestion question)
    {
        lock (_gate) _held[question.Id] = question;

        return question;
    }

    /// <summary>A prose question <paramref name="sessionId"/> asked, answered in Backlog at
    /// <paramref name="answeredAt"/> with <paramref name="answer"/> after its hook stopped
    /// asking: a Pending delivery.</summary>
    public WaitingQuestion AddPendingDelivery(string sessionId, string question, string answer, DateTimeOffset answeredAt)
    {
        var asked = WaitingQuestions.Ask(sessionId, null, [RelayQuestion.Prose(question)], isAway: true, TimeSpan.Zero, answeredAt.AddMinutes(-30));
        var notified = WaitingQuestions.Notify(asked, [Guid.NewGuid()]).Question!;
        var answered = WaitingQuestions.Answer(notified, notified.NotificationIds[0], RelayAnswer.Open(answer), answeredAt).Question!;

        return Add(answered);
    }

    public Task<WaitingQuestion> RegisterAsync(WaitingQuestion candidate, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (WaitingQuestions.DuplicateOf(candidate, _held.Values) is { } registered) return Task.FromResult(registered);

            _held[candidate.Id] = candidate;
            return Task.FromResult(candidate);
        }
    }

    public Task<WaitingQuestion?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(_held.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<WaitingQuestion>> ListAsync(bool openOnly = false, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<WaitingQuestion>>(
                [.. _held.Values.Where(question => !openOnly || question.IsOpen).OrderBy(question => question.AskedAt)]);
        }
    }

    public Task<WaitingQuestionOutcome> ChangeAsync(Guid id, Func<WaitingQuestion, WaitingQuestionOutcome> transition, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_held.TryGetValue(id, out var question))
                return Task.FromResult(WaitingQuestionOutcome.Refused(null, $"No waiting question has the id '{id:D}'."));

            var outcome = transition(question);
            if (outcome is { Succeeded: true, Question: { } changed }) _held[id] = changed;

            return Task.FromResult(outcome);
        }
    }
}

/// <summary>A session list that answers the sessions it was given, every reading.</summary>
internal sealed class FixedSessionSource(params AgentSession[] sessions) : IAgentSessionSource
{
    /// <summary>How many times the list was read.</summary>
    public int Reads { get; private set; }

    public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        Reads++;
        return Task.FromResult(new AgentSessionCatalog(sessions, [], sessions.Length));
    }

    public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
        GetSessionsAsync(cancellationToken);

    /// <summary>A local Claude session; with <paramref name="desktopId"/> it has a desktop
    /// record, archived when <paramref name="archived"/>.</summary>
    public static AgentSession Claude(string id, string? desktopId, bool archived = false, AgentSessionState state = AgentSessionState.Finished) =>
        new AgentSession(
            id,
            AgentSessionKind.Claude,
            "env-1",
            "PC",
            "A session",
            @"D:\Repos\Backlog",
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            state,
            null,
            AgentSessionOrigin.Local)
        {
            DesktopSessionId = desktopId,
            Archived = desktopId is null ? null : archived
        };
}
