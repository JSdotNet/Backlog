using System.Globalization;
using System.Net;

using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Infrastructure.SpecManager.OAuth;
using Backlog.Modules.Notifications.Abstractions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.SpecManager.Questions;

/// <summary>
/// The <see cref="INotificationAnswerHandler"/> for the <c>spec-manager</c> source: the
/// answer a person gave in Backlog — on the desktop or the phone — sent to spec-manager
/// with <c>PUT …/backlog-questions/{id}/answer</c>: the chosen option by its position, or
/// the "Other" text, and the note.
/// <para>
/// <b>Idempotent on the notification id</b>, as the contract asks: the product and the
/// question are read from the answer's source key, and the same answer sent again by the
/// same person is one spec-manager takes as the same answer. So a replay after a restart
/// changes nothing there.
/// </para>
/// <para>
/// <b>When spec-manager refuses</b>, the answer is done with — sending it again would be
/// refused again — and an alert says why: the product does not allow answering from a
/// connected app (with the link to the product's settings), someone else answered first
/// (with who, and what), or the question is gone. Which refusal is which is read off
/// spec-manager's own sentence, since it answers both "switched off" and "keeps agents out"
/// with a 403 and gives no code beside it. <b>When nobody is signed in</b> the answer is
/// kept: the handler throws, the owner keeps it waiting, the sign-in line says
/// "Sign in to spec-manager to send answers", and it is handed over again once the person
/// signs in (<see cref="SpecManagerQuestionSource"/>). An installation that cannot be
/// reached keeps it the same way, until the next start.
/// </para>
/// </summary>
internal sealed class SpecManagerAnswers : INotificationAnswerHandler
{
    /// <summary>The words spec-manager's refusal uses when a product does not allow answers
    /// from a connected app — read so that refusal is told apart from the product keeping
    /// agents out, which is a 403 too.</summary>
    internal const string ConnectedAppRefusalMarker = "connected app";

    /// <summary>The words spec-manager's refusal uses when someone else answered first.</summary>
    internal const string AnsweredByOtherMarker = "answered by someone else";

    private readonly SpecManagerClient _client;
    private readonly SpecManagerSignIn _signIn;
    private readonly Func<INotifications?> _notifications;
    private readonly Func<INotificationReplication?> _held;
    private readonly bool _defaultInstallation;
    private readonly ILogger _log;
    private readonly Lock _alertsGate = new();
    private Task _alerts = Task.CompletedTask;

    public SpecManagerAnswers(
        SpecManagerClient client,
        SpecManagerSignIn signIn,
        Func<INotifications?> notifications,
        Func<INotificationReplication?> held,
        bool defaultInstallation,
        ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(held);

        _client = client;
        _signIn = signIn;
        _notifications = notifications;
        _held = held;
        _defaultInstallation = defaultInstallation;
        _log = log ?? NullLogger.Instance;
    }

    public string Source => SpecManagerQuestionMapper.SourceContext;

    /// <summary>The alerts started so far, so a test can wait for them; the handler never
    /// awaits one itself (see the contract's remarks).</summary>
    internal Task Alerts
    {
        get
        {
            lock (_alertsGate) return _alerts;
        }
    }

    public async Task HandleAnswerAsync(NotificationAnswer answer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(answer);

        if (SpecManagerQuestionMapper.ParseSourceKey(answer.Source.Key) is not { } named)
        {
            _log.LogDebug("An answer with source key {Key} names no spec-manager question; nothing to send.", answer.Source.Key);
            return;
        }

        var (product, questionId) = named;

        if (_signIn.Account is null)
        {
            _signIn.SetAnswersWaiting(true);
            throw new SpecManagerSignInRequiredException();
        }

        try
        {
            await _client.AnswerQuestionAsync(product, questionId, RequestOf(answer), cancellationToken).ConfigureAwait(false);
            _signIn.SetAnswersWaiting(false);
            _log.LogInformation("Sent the answer to spec-manager question {QuestionId} of {Product}.", questionId, product);
        }
        catch (SpecManagerSignInRequiredException)
        {
            _signIn.SetAnswersWaiting(true);
            throw;
        }
        catch (Exception ex) when (ex is not SpecManagerRefusedException && SpecManagerSignIn.IsUnreachable(ex, cancellationToken))
        {
            // Signed in, and spec-manager did not answer: no sign-in will help, so the line
            // asks for none. The answer waits for the next start.
            _signIn.SetAnswersWaiting(false);
            throw;
        }
        catch (SpecManagerRefusedException refused)
        {
            // Refused is an answer about the question: done with, whatever else waits.
            _signIn.SetAnswersWaiting(false);
            _log.LogInformation(
                "spec-manager refused the answer to question {QuestionId} of {Product} with {Status}.",
                questionId,
                product,
                (int)refused.Status);

            var question = await QuestionTitleAsync(answer.NotificationId, cancellationToken).ConfigureAwait(false);
            var alert = await RefusalAlertAsync(refused, product, questionId, question, cancellationToken).ConfigureAwait(false);
            Raise(alert);
        }
    }

    /// <summary>What the reply becomes: its first choice as the option's position, else its
    /// open text as "Other"; and its note.</summary>
    internal static BacklogAnswerRequest RequestOf(NotificationAnswer answer)
    {
        int? option = answer.ChoiceIds.Count > 0
            && int.TryParse(answer.ChoiceIds[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var position)
                ? position
                : null;

        return new BacklogAnswerRequest(
            option,
            option is null && !string.IsNullOrWhiteSpace(answer.Text) ? answer.Text.Trim() : null,
            string.IsNullOrWhiteSpace(answer.Note) ? null : answer.Note.Trim());
    }

    private async Task<NotificationDraft> RefusalAlertAsync(
        SpecManagerRefusedException refused,
        string product,
        string questionId,
        string question,
        CancellationToken cancellationToken)
    {
        var root = _client.Root;

        if (refused.Status == HttpStatusCode.Forbidden && Mentions(refused.Reason, ConnectedAppRefusalMarker))
        {
            return Alert(
                product,
                questionId,
                $"Turn on 'Answer questions from a connected app' for {product} in spec-manager",
                $"Your answer to '{question}' was not sent: {product} does not take answers from a connected app. "
                + "Turn the setting on in the product's features, or answer in spec-manager itself.",
                SpecManagerQuestionMapper.ProductSettingsLink(product, root));
        }

        if (refused.Status == HttpStatusCode.BadRequest && Mentions(refused.Reason, AnsweredByOtherMarker)
            && await FirstAnswerAsync(product, questionId, cancellationToken).ConfigureAwait(false) is { } first)
        {
            var by = string.IsNullOrWhiteSpace(first.AnsweredBy?.Name) ? "Someone else" : first.AnsweredBy.Name.Trim();
            return Alert(
                product,
                questionId,
                $"Your answer to '{question}' was not recorded: {by} answered '{first.Said}' first",
                $"spec-manager keeps the first answer to a question, and only whoever gave it can change it.",
                QuestionLink(product, questionId));
        }

        if (refused.Status == HttpStatusCode.NotFound)
        {
            return Alert(
                product,
                questionId,
                $"Your answer to '{question}' was not recorded: the question is gone from spec-manager",
                "It was deleted in spec-manager before the answer reached it.",
                QuestionLink(product, questionId));
        }

        return Alert(
            product,
            questionId,
            $"Your answer to '{question}' was not recorded in spec-manager",
            refused.Reason is null
                ? $"spec-manager refused it ({(int)refused.Status})."
                : $"spec-manager refused it ({(int)refused.Status}): {refused.Reason}",
            QuestionLink(product, questionId));
    }

    /// <summary>The answer the question now holds, read back from the answered questions; null
    /// when spec-manager will not say.</summary>
    private async Task<BacklogAnswerDto?> FirstAnswerAsync(string product, string questionId, CancellationToken cancellationToken)
    {
        try
        {
            var answered = await _client.GetQuestionsAsync(product, BacklogQuestionStates.Answered, changedSince: null, cancellationToken).ConfigureAwait(false);
            return answered.FirstOrDefault(question => string.Equals(question.Id, questionId, StringComparison.OrdinalIgnoreCase))?.Answer;
        }
        catch (Exception ex) when (ex is SpecManagerSignInRequiredException || SpecManagerSignIn.IsUnreachable(ex, cancellationToken))
        {
            return null;
        }
    }

    /// <summary>
    /// An alert about the answer to one question. Its words go in the summary, which holds
    /// the whole sentence; the title says only that an answer did not arrive. Its id is
    /// derived from the question, so a refusal replayed after a restart raises one alert.
    /// </summary>
    private NotificationDraft Alert(string product, string questionId, string sentence, string detail, string? link) => new(
        new NotificationSource(SpecManagerQuestionMapper.SourceContext, SpecManagerQuestionMapper.SourceKey(product, questionId)),
        Subject: null,
        NotificationKind.Alert,
        NotificationSeverity.Warning,
        "Your answer did not reach spec-manager",
        SpecManagerQuestionMapper.Cut(SpecManagerQuestionMapper.OneLine(sentence), 180),
        sentence + "\n\n" + detail,
        DeepLink: link,
        Origin: NotificationOrigin.Fit(
            SpecManagerQuestionMapper.SystemOf(_client.Root, _defaultInstallation),
            product,
            openLink: link),
        Id: SpecManagerQuestionIds.RefusalFor(_client.Root, questionId));

    private string QuestionLink(string product, string questionId) =>
        $"{_client.Root}/producten/{Uri.EscapeDataString(product)}/questions?question={Uri.EscapeDataString(questionId)}";

    /// <summary>The question as its notification titles it, for the alert's sentence.</summary>
    private async Task<string> QuestionTitleAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        try
        {
            if (_held() is { } held && await held.GetAsync(notificationId, cancellationToken).ConfigureAwait(false) is { } snapshot)
            {
                return snapshot.Title;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogDebug(ex, "The question of notification {NotificationId} could not be read back for its alert.", notificationId);
        }

        return "the question";
    }

    /// <summary>Starts publishing the alert without waiting for it: the contract asks a
    /// handler not to await the work it causes.</summary>
    private void Raise(NotificationDraft alert)
    {
        if (_notifications() is not { } notifications) return;

        lock (_alertsGate)
        {
            var previous = _alerts;
            _alerts = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);

                try
                {
                    var published = await notifications.PublishAsync(alert, CancellationToken.None).ConfigureAwait(false);
                    if (published.IsFailure)
                    {
                        _log.LogWarning("The alert about a refused spec-manager answer was not published: {Code}", published.Error.Code);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "The alert about a refused spec-manager answer was not published.");
                }
            });
        }
    }

    private static bool Mentions(string? reason, string marker) =>
        reason is not null && reason.Contains(marker, StringComparison.OrdinalIgnoreCase);
}
