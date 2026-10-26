using System.Collections.Concurrent;

using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Infrastructure.SpecManager.OAuth;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.SpecManager.Questions;

/// <summary>
/// One pull of the questions of every spec-manager product among the connected targets,
/// and what it does to the notifications that ask them here: publish the ones that reach
/// the person, close the ones answered or withdrawn in spec-manager.
/// <para>
/// <b>What a pull reads.</b> Per product, <c>state=open</c> and <c>state=answered</c>,
/// each with <c>changedSince</c> fifteen minutes before the last pull of that product —
/// the allowance <see cref="SpecManagerConnector.ClockSkewAllowance"/> gives the backlog,
/// for the same reason: the last pull's moment is this machine's clock and the change
/// stamps are the server's. A <i>whole</i> pull — the first of a run, and each one the
/// stream asks for when it (re)connects — leaves <c>changedSince</c> out, because a deleted
/// question is in no answer at all and only a whole list shows it is gone.
/// </para>
/// <para>
/// <b>Which questions reach the person</b>: those assigned to them — the member the
/// product's members call <c>isYou</c> — and, unless the target says "Only questions
/// assigned to me", the unassigned ones spec-manager says they can answer.
/// </para>
/// <para>
/// <b>What closes</b>, of the notifications this desktop holds open: a question answered
/// in spec-manager ("Answered in spec-manager by Ada: Round half even"); one deleted, or
/// that no longer reaches the person — reassigned away ("Withdrawn in spec-manager"). An
/// item finished or archived closes its questions through the linked task's own subject
/// end, which the Tasks context reports.
/// </para>
/// </summary>
internal sealed class SpecManagerQuestionSync
{
    private readonly SpecManagerClient _client;
    private readonly SpecManagerSignIn _signIn;
    private readonly IConnectedTargets? _targets;
    private readonly INotifications _notifications;
    private readonly INotificationReplication? _held;
    private readonly ISpecManagerLinkedWorkLookup _linked;
    private readonly TimeProvider _time;
    private readonly bool _defaultInstallation;
    private readonly ILogger _log;

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastPull = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _me = new(StringComparer.OrdinalIgnoreCase);

    public SpecManagerQuestionSync(
        SpecManagerClient client,
        SpecManagerSignIn signIn,
        IConnectedTargets? targets,
        INotifications notifications,
        INotificationReplication? held,
        ISpecManagerLinkedWorkLookup linked,
        TimeProvider time,
        bool defaultInstallation,
        ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(linked);
        ArgumentNullException.ThrowIfNull(time);

        _client = client;
        _signIn = signIn;
        _targets = targets;
        _notifications = notifications;
        _held = held;
        _linked = linked;
        _time = time;
        _defaultInstallation = defaultInstallation;
        _log = log ?? NullLogger.Instance;

        _signIn.AccountChanged += () => _me.Clear();
    }

    /// <summary>The spec-manager products among the connected targets that are switched on.</summary>
    public IReadOnlyList<ConnectedTarget> Products() =>
        [.. (_targets?.List() ?? [])
            .Where(target => target.Enabled && string.Equals(target.ConnectorId, SpecManagerConnector.ConnectorId, StringComparison.Ordinal))];

    /// <summary>
    /// Pulls every product once — whole when <paramref name="whole"/> or when a product has
    /// not been pulled yet this run — and answers whether every product answered. Nobody
    /// signed in pulls nothing. One product that refuses or cannot be reached is logged and
    /// leaves the others to be pulled.
    /// </summary>
    public async Task<bool> PullAsync(bool whole, CancellationToken cancellationToken)
    {
        if (_signIn.Account is null) return false;

        var answered = true;

        foreach (var target in Products())
        {
            try
            {
                await PullProductAsync(target, whole, cancellationToken).ConfigureAwait(false);
            }
            catch (SpecManagerSignInRequiredException)
            {
                return false;
            }
            catch (Exception ex) when (SpecManagerSignIn.IsUnreachable(ex, cancellationToken))
            {
                answered = false;
                _log.LogInformation(ex, "The questions of {Product} could not be read from spec-manager; the next pull asks again.", target.Target);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One product's surprise is not the others': they are still pulled.
                answered = false;
                _log.LogWarning(ex, "Pulling the questions of {Product} failed; the next pull asks again.", target.Target);
            }
        }

        return answered;
    }

    private async Task PullProductAsync(ConnectedTarget target, bool whole, CancellationToken cancellationToken)
    {
        var product = target.Target.Trim();
        var startedAt = _time.GetUtcNow();

        DateTimeOffset? since = !whole && _lastPull.TryGetValue(product, out var last)
            ? last - SpecManagerConnector.ClockSkewAllowance
            : null;

        var me = await MeAsync(product, cancellationToken).ConfigureAwait(false);
        var open = await _client.GetQuestionsAsync(product, BacklogQuestionStates.Open, since, cancellationToken).ConfigureAwait(false);
        var answered = await _client.GetQuestionsAsync(product, BacklogQuestionStates.Answered, since, cancellationToken).ConfigureAwait(false);
        var held = await HeldAsync(product, cancellationToken).ConfigureAwait(false);

        foreach (var question in open)
        {
            var id = SpecManagerQuestionIds.For(_client.Root, question.Id);

            if (!SpecManagerQuestionMapper.Concerns(question, me, target.OnlyQuestionsAssignedToMe))
            {
                if (held is null || held.ContainsKey(id)) await CloseAsync(id, SpecManagerQuestionMapper.WithdrawnReason, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (held?.ContainsKey(id) == true) continue;

            await PublishAsync(question, product, me, cancellationToken).ConfigureAwait(false);
        }

        foreach (var question in answered)
        {
            if (question.Answer is not { } answer) continue;

            var id = SpecManagerQuestionIds.For(_client.Root, question.Id);
            if (held is null || held.ContainsKey(id))
            {
                await CloseAsync(id, SpecManagerQuestionMapper.AnsweredReason(answer), cancellationToken).ConfigureAwait(false);
            }
        }

        // Only a whole list says what is gone: a question deleted in spec-manager is in no
        // answer, the changed-since ones included.
        if (since is null && held is not null)
        {
            var listed = open.Concat(answered)
                .Select(question => SpecManagerQuestionIds.For(_client.Root, question.Id))
                .ToHashSet();

            foreach (var id in held.Keys.Where(id => !listed.Contains(id)))
            {
                await CloseAsync(id, SpecManagerQuestionMapper.WithdrawnReason, cancellationToken).ConfigureAwait(false);
            }
        }

        _lastPull[product] = startedAt;
    }

    private async Task PublishAsync(BacklogQuestionDto question, string product, string? me, CancellationToken cancellationToken)
    {
        var linked = question.ItemId is { } itemId
            ? await _linked.FindAsync(itemId, cancellationToken).ConfigureAwait(false)
            : null;

        var draft = SpecManagerQuestionMapper.ToDraft(question, product, _client.Root, _defaultInstallation, me, linked);
        var published = await _notifications.PublishAsync(draft, cancellationToken).ConfigureAwait(false);

        if (published.IsFailure)
        {
            // Never the question's text: only which one, and the rule it broke.
            _log.LogWarning(
                "spec-manager question {QuestionId} of {Product} was not published: {Code} {Message}",
                question.Id,
                product,
                published.Error.Code,
                published.Error.Message);
        }
    }

    /// <summary>Closes one notification; one already settled, or never published here,
    /// is left as it is.</summary>
    private async Task CloseAsync(Guid id, string reason, CancellationToken cancellationToken) =>
        _ = await _notifications.CloseAsync(id, reason, cancellationToken).ConfigureAwait(false);

    /// <summary>The questions of <paramref name="product"/> this desktop holds open, by
    /// notification id, or null when this host cannot say — and then every close is asked
    /// for and answered "not found" where there is nothing to close.</summary>
    private async Task<Dictionary<Guid, string>?> HeldAsync(string product, CancellationToken cancellationToken)
    {
        if (_held is null) return null;

        var prefix = product + "/";
        var held = new Dictionary<Guid, string>();

        foreach (var notification in await _held.ListOpenAsync(cancellationToken).ConfigureAwait(false))
        {
            if (notification.Kind != NotificationKind.Question) continue;
            if (!string.Equals(notification.Source.Context, SpecManagerQuestionMapper.SourceContext, StringComparison.Ordinal)) continue;
            if (!notification.Source.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            held[notification.Id] = notification.Source.Key;
        }

        return held;
    }

    /// <summary>The person's member id in <paramref name="product"/>, read once per sign-in
    /// from the members' <c>isYou</c>; null while the product will not say.</summary>
    private async Task<string?> MeAsync(string product, CancellationToken cancellationToken)
    {
        if (_me.TryGetValue(product, out var known)) return known;

        var members = await _client.GetMembersAsync(product, cancellationToken).ConfigureAwait(false);
        var me = members?.FirstOrDefault(member => member.IsYou)?.UserId;

        if (!string.IsNullOrWhiteSpace(me)) _me[product] = me;

        return me;
    }
}
