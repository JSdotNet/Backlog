using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Infrastructure.SpecManager.OAuth;
using Backlog.Modules.Notifications.Abstractions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.SpecManager.Questions;

/// <summary>
/// Marks a spec-manager notification read once its alert is acknowledged in Backlog, from any
/// channel: it listens to <see cref="INotificationCentre.Changed"/>, which the owner raises
/// after every stored change — the phone's acknowledgement arriving through the sync exchange
/// included — and sends <c>POST /api/notifications/{id}/read</c> for each alert of this
/// installation's notifications that reached <see cref="NotificationState.Acknowledged"/>.
/// <para>
/// The centre is the owner's alone, so a host without one listens to nothing. The send never
/// runs on the thread that raised the change, and the sends run one after another.
/// Signed out, or out of reach, nothing is sent and nothing is kept: the next pull
/// (<see cref="SpecManagerNotificationSync"/>) finds the alert acknowledged and the
/// notification still unread, and sends it then.
/// </para>
/// <para>
/// Started and stopped by <see cref="SpecManagerQuestionSource"/>, which owns it.
/// </para>
/// </summary>
internal sealed class SpecManagerNotificationReads : IDisposable
{
    private readonly SpecManagerClient _client;
    private readonly SpecManagerSignIn _signIn;
    private readonly INotificationCentre? _centre;
    private readonly ILogger _log;
    private readonly Lock _gate = new();
    private Task _sends = Task.CompletedTask;
    private bool _listening;

    public SpecManagerNotificationReads(
        SpecManagerClient client,
        SpecManagerSignIn signIn,
        INotificationCentre? centre,
        ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(signIn);

        _client = client;
        _signIn = signIn;
        _centre = centre;
        _log = log ?? NullLogger.Instance;
    }

    /// <summary>The reads started so far, so a test can wait for them; the listener never
    /// awaits one on the raising thread.</summary>
    internal Task Sends
    {
        get
        {
            lock (_gate) return _sends;
        }
    }

    /// <summary>Starts listening to the centre, when this host has one.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_listening || _centre is null) return;

            _centre.Changed += OnChanged;
            _listening = true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_listening || _centre is null) return;

            _centre.Changed -= OnChanged;
            _listening = false;
        }
    }

    private void OnChanged(object? sender, NotificationChanged change)
    {
        var view = change.Notification;
        if (view.State != NotificationState.Acknowledged) return;
        if (SpecManagerNotificationMapper.NotificationOf(view.Id, view.Kind, view.Source, _client.Root) is not { } notificationId) return;
        if (_signIn.Account is null) return;

        lock (_gate)
        {
            var previous = _sends;
            _sends = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);
                await SendAsync(notificationId).ConfigureAwait(false);
            });
        }
    }

    private async Task SendAsync(Guid notificationId)
    {
        try
        {
            await _client.MarkNotificationReadAsync(notificationId, CancellationToken.None).ConfigureAwait(false);
            _log.LogInformation("Marked spec-manager notification {NotificationId} read, as it was acknowledged in Backlog.", notificationId);
        }
        catch (Exception ex)
        {
            // Whatever went wrong, the next pull sends it again while it is unread there.
            _log.LogInformation(ex, "spec-manager notification {NotificationId} could not be marked read now; the next pull sends it.", notificationId);
        }
    }
}
