using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.Toasts;
using Backlog.Modules.Notifications.UI;

using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Backlog.Desktop.Services;

/// <summary>
/// The owner desktop's Windows app notifications, through the Windows App SDK
/// (<c>Microsoft.Windows.AppNotifications</c>): every alert or question this desktop publishes is
/// raised as one, and taken down again once it is answered, acknowledged or closed from any
/// channel.
/// <para>
/// What a notification shows and what a press means are <see cref="NotificationToast"/>'s rules,
/// tested without Windows: set answers become action buttons, at most three; an open answer gets
/// an inline text box and Send; an alert gets Acknowledge. A press is read back by the same class
/// and answered through <see cref="INotificationCentre"/> — the path the shell's centre answers
/// through — so the owner checks a toast's answer exactly as it checks one given in the app. A
/// refused answer, and a press on the body or on "Open in Backlog", opens the centre on that
/// notification through <see cref="NotificationCentreRequests"/>.
/// </para>
/// <para>
/// Unpackaged (Debug), <see cref="AppNotificationManager.Register()"/> registers the COM server
/// itself; the packaged build declares it in <c>Package.appxmanifest</c>. A failure to register —
/// an elevated process, a missing runtime — leaves the app without Windows notifications and logs
/// why; the centre in the shell works regardless.
/// </para>
/// </summary>
internal sealed class WindowsAppNotifications(
    INotificationCentre centre,
    NotificationCentreRequests requests,
    ILogger<WindowsAppNotifications> logger) : IDisposable
{
    private bool _registered;

    /// <summary>Registers for presses and starts raising notifications. Called once, after the
    /// container is built and before the first window.</summary>
    public void Start()
    {
        try
        {
            var manager = AppNotificationManager.Default;

            // The handler before Register: a launch caused by a press delivers that press as
            // soon as Register returns.
            manager.NotificationInvoked += OnInvoked;
            manager.Register();
            _registered = true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Windows app notifications are unavailable; the notification centre still works.");
            return;
        }

        centre.Changed += OnChanged;
    }

    public void Dispose()
    {
        centre.Changed -= OnChanged;
        if (!_registered) return;

        try
        {
            AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Unregistering Windows app notifications failed on the way out.");
        }
    }

    private void OnChanged(object? sender, NotificationChanged change)
    {
        var notification = change.Notification;

        if (change.IsNew && NotificationToast.For(notification) is { } content)
        {
            Show(content);
        }
        else if (!notification.IsOpen)
        {
            _ = RemoveAsync(notification.Id);
        }
    }

    private void Show(NotificationToastContent content)
    {
        try
        {
            AppNotificationManager.Default.Show(Build(content));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Raising the Windows notification for {NotificationId} failed.", content.NotificationId);
        }
    }

    private async Task RemoveAsync(Guid notificationId)
    {
        try
        {
            await AppNotificationManager.Default.RemoveByTagAsync(NotificationToast.TagFor(notificationId));
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Taking down the Windows notification for {NotificationId} failed.", notificationId);
        }
    }

    private static AppNotification Build(NotificationToastContent content)
    {
        var builder = new AppNotificationBuilder()
            .SetTag(content.Tag)
            .AddText(content.Title)
            .AddText(content.Summary);

        // Where it came from, "System · Place", on the attribution line under the text.
        if (!string.IsNullOrWhiteSpace(content.Attribution)) builder.SetAttributionText(content.Attribution);

        foreach (var (key, value) in content.Arguments) builder.AddArgument(key, value);

        if (content.Severity == NotificationSeverity.Critical && AppNotificationBuilder.IsUrgentScenarioSupported())
        {
            builder.SetScenario(AppNotificationScenario.Urgent);
        }

        if (content.TextBox is { } box) builder.AddTextBox(box.Id, box.Placeholder, string.Empty);

        foreach (var button in content.Buttons)
        {
            var built = new AppNotificationButton(button.Label);
            foreach (var (key, value) in button.Arguments) built.AddArgument(key, value);
            if (button.UsesTextBox && content.TextBox is { } input) built.SetInputId(input.Id);

            switch (button.Style)
            {
                case AnswerChoiceStyle.Primary:
                    built.SetButtonStyle(AppNotificationButtonStyle.Success);
                    break;
                case AnswerChoiceStyle.Destructive:
                    built.SetButtonStyle(AppNotificationButtonStyle.Critical);
                    break;
            }

            builder.AddButton(built);
        }

        return builder.BuildNotification();
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) =>
        _ = HandleAsync(
            new Dictionary<string, string>(args.Arguments, StringComparer.Ordinal),
            new Dictionary<string, string>(args.UserInput, StringComparer.Ordinal));

    private async Task HandleAsync(IReadOnlyDictionary<string, string> arguments, IReadOnlyDictionary<string, string> userInput)
    {
        if (NotificationToast.Read(arguments, userInput) is not { } action) return;

        try
        {
            switch (action.Kind)
            {
                case NotificationToastActionKind.Answer:
                    var answered = await centre.AnswerAsync(action.NotificationId, action.ChoiceIds, action.Text).ConfigureAwait(false);
                    if (answered.IsFailure) OpenCentre(action.NotificationId);
                    break;

                case NotificationToastActionKind.Acknowledge:
                    var acknowledged = await centre.AcknowledgeAsync(action.NotificationId).ConfigureAwait(false);
                    if (acknowledged.IsFailure) OpenCentre(action.NotificationId);
                    break;

                default:
                    OpenCentre(action.NotificationId);
                    break;
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Answering notification {NotificationId} from Windows failed.", action.NotificationId);
            OpenCentre(action.NotificationId);
        }
    }

    /// <summary>Brings the window forward on the centre, open on the notification — where a
    /// refused answer's reason is shown and the person can answer again.</summary>
    private void OpenCentre(Guid notificationId)
    {
        requests.Open(notificationId);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is Microsoft.UI.Xaml.Window window)
                {
                    window.Activate();
                }
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Bringing the window forward failed.");
            }
        });
    }
}
