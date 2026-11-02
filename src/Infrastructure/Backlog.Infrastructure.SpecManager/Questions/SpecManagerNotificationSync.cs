using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Infrastructure.SpecManager.OAuth;
using Backlog.Modules.Notifications.Abstractions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.SpecManager.Questions;

/// <summary>
/// One pull of the person's other spec-manager notifications — mentions, replies, change
/// proposals, input, review work, administration — and what it does to the alerts that say
/// them here. <see cref="SpecManagerQuestionSource"/> runs it after every question pull, on
/// the same stream events and loop.
/// <list type="bullet">
/// <item><b>Publish</b> the unread ones created since this desktop first signed in to the
/// installation (<see cref="SpecManagerNotificationMapper.Admits"/>). Each is named by an id
/// derived from it, so publishing it again — on the next pull, or from a second desktop —
/// is the same alert.</item>
/// <item><b>Mark read</b> in spec-manager the ones still unread there whose alert is
/// acknowledged here: the acknowledgement <see cref="SpecManagerNotificationReads"/> could
/// not send — given while signed out or out of reach, or before a restart.</item>
/// <item><b>Close</b> the alerts held open whose notification is no longer unread — read in
/// spec-manager — but only from a complete list: one cut at <see cref="Count"/> says
/// nothing of the ones it leaves out.</item>
/// </list>
/// <para>
/// The unread list has no <c>changedSince</c>, so every pull reads it whole. Never logs a
/// notification's subject or text: only its id and what went wrong.
/// </para>
/// </summary>
internal sealed class SpecManagerNotificationSync : IDisposable
{
    /// <summary>How many unread notifications a pull asks for: the most spec-manager
    /// answers.</summary>
    internal const int Count = 200;

    private readonly SpecManagerClient _client;
    private readonly SpecManagerSignIn _signIn;
    private readonly INotifications _notifications;
    private readonly INotificationReplication? _held;
    private readonly bool _defaultInstallation;
    private readonly ILogger _log;

    /// <summary>The product names by id, read once per sign-in; null until read.</summary>
    private volatile IReadOnlyDictionary<Guid, string>? _places;

    /// <summary>The product ids the last read of the products did not name — archived, or no
    /// longer the person's — so they are not asked for again on every pull; cleared with the
    /// names on the next sign-in.</summary>
    private volatile IReadOnlySet<Guid> _missing = new HashSet<Guid>();

    public SpecManagerNotificationSync(
        SpecManagerClient client,
        SpecManagerSignIn signIn,
        INotifications notifications,
        INotificationReplication? held,
        bool defaultInstallation,
        ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(notifications);

        _client = client;
        _signIn = signIn;
        _notifications = notifications;
        _held = held;
        _defaultInstallation = defaultInstallation;
        _log = log ?? NullLogger.Instance;

        _signIn.AccountChanged += OnAccountChanged;
    }

    public void Dispose() => _signIn.AccountChanged -= OnAccountChanged;

    private void OnAccountChanged()
    {
        _places = null;
        _missing = new HashSet<Guid>();
    }

    /// <summary>
    /// Pulls the unread notifications once and answers whether spec-manager answered. Nobody
    /// signed in pulls nothing; an installation out of reach is logged and left to the next
    /// pull.
    /// </summary>
    public async Task<bool> PullAsync(CancellationToken cancellationToken)
    {
        if (_signIn.Account is null || _signIn.FirstSignedInAt is not { } firstSignedInAt) return false;

        NotificationsResponse? unread;
        try
        {
            unread = await _client.GetUnreadNotificationsAsync(Count, cancellationToken).ConfigureAwait(false);
        }
        catch (SpecManagerSignInRequiredException)
        {
            return false;
        }
        catch (Exception ex) when (SpecManagerSignIn.IsUnreachable(ex, cancellationToken))
        {
            _log.LogInformation(ex, "The spec-manager notifications could not be read; the next pull asks again.");
            return false;
        }

        // An answer without a list is no answer: read as an empty list, it would close every
        // alert as read.
        if (unread?.Notifications is not { } listed)
        {
            _log.LogInformation("spec-manager answered the unread notifications without a list; the next pull asks again.");
            return false;
        }

        var admitted = listed.Where(notification => SpecManagerNotificationMapper.Admits(notification, firstSignedInAt)).ToList();
        var fresh = new List<SpecManagerNotificationDto>();
        var signedIn = true;

        foreach (var notification in admitted)
        {
            var id = SpecManagerQuestionIds.ForNotification(_client.Root, notification.Id);
            var held = _held is null ? null : await _held.GetAsync(id, cancellationToken).ConfigureAwait(false);

            if (held is null)
            {
                fresh.Add(notification);
            }
            else if (held.State == NotificationState.Acknowledged && signedIn)
            {
                signedIn = await MarkReadAsync(notification.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        if (fresh.Count > 0)
        {
            var places = await PlacesAsync(fresh, cancellationToken).ConfigureAwait(false);
            foreach (var notification in fresh)
            {
                await PublishAsync(notification, places, cancellationToken).ConfigureAwait(false);
            }
        }

        // A list cut short says nothing of the ones it leaves out, read or not.
        if (unread.Unread <= listed.Count)
        {
            await CloseReadAsync(listed, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private async Task PublishAsync(SpecManagerNotificationDto notification, IReadOnlyDictionary<Guid, string> places, CancellationToken cancellationToken)
    {
        var place = notification.ProductId is { } productId && places.TryGetValue(productId, out var name) ? name : null;
        var draft = SpecManagerNotificationMapper.ToDraft(notification, _client.Root, _defaultInstallation, place);
        var published = await _notifications.PublishAsync(draft, cancellationToken).ConfigureAwait(false);

        if (published.IsFailure)
        {
            // Never the notification's subject or text: only which one, and the rule it broke.
            _log.LogWarning(
                "spec-manager notification {NotificationId} was not published: {Code} {Message}",
                notification.Id,
                published.Error.Code,
                published.Error.Message);
        }
    }

    /// <summary>Marks one notification read in spec-manager; answers false when nobody is
    /// signed in any more, so the rest wait for the next pull too. A failure is logged and
    /// left to the next pull.</summary>
    private async Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        try
        {
            await _client.MarkNotificationReadAsync(notificationId, cancellationToken).ConfigureAwait(false);
            _log.LogInformation("Marked spec-manager notification {NotificationId} read, as it was acknowledged in Backlog.", notificationId);
            return true;
        }
        catch (SpecManagerSignInRequiredException)
        {
            return false;
        }
        catch (Exception ex) when (SpecManagerSignIn.IsUnreachable(ex, cancellationToken))
        {
            _log.LogWarning(ex, "spec-manager notification {NotificationId} could not be marked read; the next pull sends it again.", notificationId);
            return true;
        }
    }

    /// <summary>Closes every alert of this installation's notifications held open whose
    /// notification is not in <paramref name="listed"/> — the whole unread list — because it
    /// was read in spec-manager. An alert published by this pull is in the list, so it is
    /// never among them.</summary>
    private async Task CloseReadAsync(IReadOnlyList<SpecManagerNotificationDto> listed, CancellationToken cancellationToken)
    {
        if (_held is null) return;

        var unread = listed.Select(notification => notification.Id).ToHashSet();

        foreach (var open in await _held.ListOpenAsync(cancellationToken).ConfigureAwait(false))
        {
            if (SpecManagerNotificationMapper.NotificationOf(open.Id, open.Kind, open.Source, _client.Root) is not { } notificationId) continue;
            if (unread.Contains(notificationId)) continue;

            var closed = await _notifications.CloseAsync(open.Id, SpecManagerNotificationMapper.ReadReason, cancellationToken).ConfigureAwait(false);
            if (closed.IsFailure)
            {
                _log.LogInformation(
                    "The alert of spec-manager notification {NotificationId} was not closed: {Code}",
                    notificationId,
                    closed.Error.Code);
            }
        }
    }

    /// <summary>The product names by id: read once per sign-in, and again when one of
    /// <paramref name="admitted"/> names a product they do not and no earlier read missed. Products that cannot be read
    /// leave the alerts without a place.</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> PlacesAsync(IReadOnlyList<SpecManagerNotificationDto> admitted, CancellationToken cancellationToken)
    {
        var known = _places;
        var missing = _missing;
        var asked = admitted
            .Select(notification => notification.ProductId)
            .OfType<Guid>()
            .Where(id => known?.ContainsKey(id) != true && !missing.Contains(id))
            .ToHashSet();

        if (asked.Count == 0) return known ?? new Dictionary<Guid, string>();

        try
        {
            var products = await _client.GetProductsAsync(cancellationToken).ConfigureAwait(false);
            var places = new Dictionary<Guid, string>();

            foreach (var product in products)
            {
                if (product.Id is not { } id) continue;

                var name = !string.IsNullOrWhiteSpace(product.Name) ? product.Name.Trim() : product.Slug?.Trim();
                if (!string.IsNullOrWhiteSpace(name)) places[id] = name;
            }

            _places = places;
            _missing = new HashSet<Guid>(missing.Concat(asked.Where(id => !places.ContainsKey(id))));
            return places;
        }
        catch (Exception ex) when (ex is SpecManagerSignInRequiredException || SpecManagerSignIn.IsUnreachable(ex, cancellationToken))
        {
            _log.LogInformation(ex, "The spec-manager products could not be read; the alerts name no product this time.");
            return known ?? new Dictionary<Guid, string>();
        }
    }
}
