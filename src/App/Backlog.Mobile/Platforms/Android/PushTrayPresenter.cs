using Android.App;
using Android.Content;

using AndroidX.Core.App;

using Backlog.Mobile.UI.Notifications;

using AndroidUri = Android.Net.Uri;
using RemoteInput = AndroidX.Core.App.RemoteInput;

namespace Backlog.Mobile;

/// <summary>
/// Puts a pushed notification in the Android tray and takes it out again (local ADR 0039 §4).
/// <para>
/// Each entry is tagged with the notification's id, so the next push about the same
/// notification replaces it, and a settled one is removed by the same tag. Its tap opens the
/// notification's detail through <see cref="MainActivity"/>; its actions — the set answers, the
/// inline reply — go to <see cref="NotificationActionReceiver"/>, which queues the reply. What
/// the entry says and which actions it has is <see cref="PushTray"/>'s to decide; this class
/// only draws it.
/// </para>
/// </summary>
public sealed class PushTrayPresenter(Context context)
{
    /// <summary>The intent action of a tap on an entry: <see cref="MainActivity"/> opens the
    /// notification named by <see cref="NotificationIdExtra"/>.</summary>
    public const string OpenAction = "com.jsdotnet.backlog.mobile.OPEN_NOTIFICATION";

    /// <summary>The intent action of a set answer or the inline reply.</summary>
    public const string AnswerAction = "com.jsdotnet.backlog.mobile.ANSWER_NOTIFICATION";

    public const string NotificationIdExtra = "notification-id";
    public const string ActionKeyExtra = "action-key";
    public const string TitleExtra = "title";

    /// <summary>The inline reply's result key in <see cref="RemoteInput"/>.</summary>
    public const string ReplyTextKey = "reply-text";

    /// <summary>The one channel Backlog's notifications go on. A stored value: Android keeps the
    /// person's settings for it by this id.</summary>
    private const string ChannelId = "backlog-notifications";

    /// <summary>Every entry shares this id; the tag tells them apart.</summary>
    private const int EntryId = 1;

    /// <summary>Shows <paramref name="entry"/>, replacing whatever the tray held for the same
    /// notification. Does nothing while the person has notifications off for Backlog.</summary>
    public void Show(PushTrayEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var manager = Manager();
        if (!manager.AreNotificationsEnabled()) return;

        EnsureChannel();

        var builder = Builder(entry.Id, entry.Title, entry.Text, entry.Urgent);

        foreach (var action in entry.Actions)
        {
            builder.AddAction(action.Kind == PushActionKind.InlineReply
                ? InlineReply(entry, action)
                : new NotificationCompat.Action.Builder(0, action.Label, AnswerIntent(entry.Id, entry.Title, action.Key, mutable: false)).Build());
        }

        manager.Notify(Tag(entry.Id), EntryId, builder.Build());
    }

    /// <summary>Puts back an entry whose answer from the tray could not be queued, without
    /// actions, so the tray stops waiting on it and the tap opens the question in the app.</summary>
    public void ShowUnanswered(Guid notificationId, string? title)
    {
        var manager = Manager();
        if (!manager.AreNotificationsEnabled()) return;

        EnsureChannel();

        var builder = Builder(notificationId, title ?? PushTray.FallbackTitle, "Open Backlog to answer.", urgent: false);
        manager.Notify(Tag(notificationId), EntryId, builder.Build());
    }

    /// <summary>Takes the entry for <paramref name="notificationId"/> out of the tray, if there is one.</summary>
    public void Cancel(Guid notificationId) =>
        Manager().Cancel(Tag(notificationId), EntryId);

    private NotificationCompat.Builder Builder(Guid notificationId, string title, string? text, bool urgent)
    {
        // Each setter returns the builder, nullable in the binding; called as statements instead.
        var builder = new NotificationCompat.Builder(context, ChannelId);
        builder.SetSmallIcon(Resource.Drawable.notification_icon);
        builder.SetContentTitle(title);
        builder.SetAutoCancel(true);
        builder.SetCategory(NotificationCompat.CategoryMessage);
        builder.SetPriority(urgent ? NotificationCompat.PriorityHigh : NotificationCompat.PriorityDefault);
        builder.SetContentIntent(OpenIntent(notificationId));

        if (text is not null) builder.SetContentText(text);

        return builder;
    }

    private NotificationCompat.Action InlineReply(PushTrayEntry entry, PushAction action)
    {
        var input = new RemoteInput.Builder(ReplyTextKey);
        input.SetLabel(action.Placeholder ?? action.Label);

        // Mutable: Android writes the typed text into this intent.
        var reply = new NotificationCompat.Action.Builder(0, action.Label, AnswerIntent(entry.Id, entry.Title, action.Key, mutable: true));
        reply.AddRemoteInput(input.Build());
        reply.SetAllowGeneratedReplies(false);

        return reply.Build() ?? throw new InvalidOperationException("Android built no inline reply action.");
    }

    private PendingIntent OpenIntent(Guid notificationId)
    {
        var intent = new Intent(context, typeof(MainActivity))
            .SetAction(OpenAction)
            .SetData(EntryUri(notificationId, "open"))
            .PutExtra(NotificationIdExtra, notificationId.ToString("D"))
            .AddFlags(ActivityFlags.SingleTop);

        return PendingIntent.GetActivity(context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    private PendingIntent AnswerIntent(Guid notificationId, string title, string key, bool mutable)
    {
        // A distinct data address per notification and action: Android matches pending intents
        // by their address, not their extras, and would otherwise hand every button the last one.
        var intent = new Intent(context, typeof(NotificationActionReceiver))
            .SetAction(AnswerAction)
            .SetData(EntryUri(notificationId, key))
            .PutExtra(NotificationIdExtra, notificationId.ToString("D"))
            .PutExtra(ActionKeyExtra, key)
            .PutExtra(TitleExtra, title);

        // The inline reply's intent must stay mutable, or Android drops the typed text RemoteInput
        // fills in: FLAG_MUTABLE from Android 12, where an intent has to say which it is, and no
        // mutability flag before it, where FLAG_IMMUTABLE would already block the fill-in.
        var flags = PendingIntentFlags.UpdateCurrent;
        if (!mutable) flags |= PendingIntentFlags.Immutable;
        else if (OperatingSystem.IsAndroidVersionAtLeast(31)) flags |= PendingIntentFlags.Mutable;

        return PendingIntent.GetBroadcast(context, 0, intent, flags)!;
    }

    private void EnsureChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;

        // Creating a channel that exists changes nothing the person set, so this is safe on every show.
        var channel = new NotificationChannel(ChannelId, "Questions and alerts", NotificationImportance.High)
        {
            Description = "Questions and alerts from your Backlog desktops."
        };

        (context.GetSystemService(Context.NotificationService) as NotificationManager)?.CreateNotificationChannel(channel);
    }

    private NotificationManagerCompat Manager() =>
        NotificationManagerCompat.From(context) ?? throw new InvalidOperationException("Android has no notification manager.");

    private static string Tag(Guid notificationId) => notificationId.ToString("D");

    private static AndroidUri EntryUri(Guid notificationId, string key) =>
        AndroidUri.Parse($"backlog-notification://{notificationId:D}/{AndroidUri.Encode(key)}")!;
}
