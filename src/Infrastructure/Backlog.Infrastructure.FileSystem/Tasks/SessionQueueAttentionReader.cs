using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Backlog.Infrastructure.FileSystem.Tasks;

/// <summary>
/// The docked session queue's <see cref="ISessionQueueAttention"/> (local ADR 0062 §6),
/// answered out of the Sessions context's waiting questions (local ADR 0041) and its loop
/// register (local ADR 0051 §3). A cross-context join, here for the reason
/// <see cref="SessionRequestActions"/> is: the Tasks pane may not reference Sessions.
/// A mapping and nothing else.
/// </summary>
/// <param name="questions">This machine's waiting questions, or null when none is composed.</param>
/// <param name="loops">This machine's loop register, or null when none is composed.</param>
/// <param name="logger">Where an unreadable store is said; nothing when null.</param>
public sealed class SessionQueueAttentionReader(
    IWaitingQuestionStore? questions,
    ILoopRegister? loops,
    ILogger<SessionQueueAttentionReader>? logger = null) : ISessionQueueAttention
{
    public async Task<SessionQueueAttention> ReadAsync(CancellationToken cancellationToken = default)
    {
        var asked = await QuestionsAsync(cancellationToken).ConfigureAwait(false);
        var slots = await SlotsAsync(cancellationToken).ConfigureAwait(false);

        return new SessionQueueAttention(asked, slots);
    }

    /// <summary>Every open question, oldest first, named by its first question.</summary>
    private async Task<IReadOnlyList<SessionQueueQuestion>> QuestionsAsync(CancellationToken cancellationToken)
    {
        if (questions is null) return [];

        try
        {
            var open = await questions.ListAsync(openOnly: true, cancellationToken).ConfigureAwait(false);

            return
            [
                .. open
                    .Where(question => question.State is WaitingQuestionState.InSession or WaitingQuestionState.Notified)
                    .OrderBy(question => question.AskedAt)
                    .Select(question => new SessionQueueQuestion(
                        question.Id,
                        question.SessionId,
                        question.Questions.FirstOrDefault()?.Question?.Trim() ?? string.Empty,
                        question.AskedAt))
            ];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogWarning(exception, "The waiting questions could not be read for the session queue.");
            return [];
        }
    }

    /// <summary>The latest Manager check-in's working sessions against its session
    /// limit, or null when no Manager has checked in or it measured nothing.</summary>
    private async Task<SessionQueueSlots?> SlotsAsync(CancellationToken cancellationToken)
    {
        if (loops is null) return null;

        try
        {
            var all = await loops.ListAsync(cancellationToken).ConfigureAwait(false);

            if (all.FirstOrDefault(loop => loop.CheckIn.Kind == LoopKind.Dispatcher) is not { } manager) return null;

            var limits = manager.CheckIn.Reading?.Limits ?? manager.CheckIn.Limits;
            var working = manager.CheckIn.Reading?.WorkingSessions ?? manager.CheckIn.Held.Sessions;

            return new SessionQueueSlots(Math.Max(working, 0), limits.Sessions);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogWarning(exception, "The loop register could not be read for the session queue.");
            return null;
        }
    }
}
