using Backlog.Infrastructure.Sync;
using Backlog.Mobile.UI.AppUpdate;
using Backlog.Mobile.UI.Events;
using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.Notifications;
using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Progress;
using Backlog.Mobile.UI.TalkNotes;
using Backlog.Mobile.UI.Tasks;
using Backlog.UI.Components.AppUpdate;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Mobile.UI.Services;

public static class MobileShellServiceCollectionExtensions
{
    /// <summary>
    /// What the shell itself needs, the same in both hosts: the draft that outlives
    /// a tab switch, the status the app bar reads, and the lifecycle the hosts report
    /// resumes through. The MAUI head and the browser harness both call this, so the
    /// two cannot disagree about what the shell is.
    /// </summary>
    /// <remarks>The shell needs <see cref="AddDeviceOutbox"/> too; that one takes
    /// the file, which is the host's to place.</remarks>
    public static IServiceCollection AddMobileShell(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<CaptureDraft>();
        services.AddScoped<TalkNoteDraft>();
        services.AddScoped<SyncStatusTracker>();

        // One instance behind both names: the Inbox records against the tracker,
        // and the status line reads the same object through the abstraction.
        services.AddScoped<ISyncStatusSource>(provider => provider.GetRequiredService<SyncStatusTracker>());

        // Scoped, like the NavigationManager it lands the app on the Inbox with;
        // the layout starts it and the Inbox shows what it says.
        services.AddScoped<SharedContentCapture>();

        // Scoped unless the host already registered it: the MAUI head makes it a
        // singleton, because its window events arrive from outside any scope.
        services.TryAddScoped<AppLifecycle>();

        // The app bar's version control and its update window. A host that can
        // update itself registers its own service first - the Android head does;
        // anything else, the browser harness and the tests included, gets the
        // service that answers "not managed here", and the stamp that keeps the
        // quiet launch check eight hours apart only for as long as the process.
        services.TryAddSingleton<IAppUpdateService>(_ => new UnsupportedAppUpdateService());
        services.TryAddSingleton<IAppUpdateCheckStamp, InMemoryAppUpdateCheckStamp>();
        services.AddScoped<AppUpdateWatch>();

        // The Dictation setting the app bar's Settings sheet writes and the transcribers
        // read at the start of every turn. The Android head registers its preferences
        // store first, so the choice outlives a launch; anything else keeps it in memory —
        // in the browser harness, shared by every browser on it, as one pretend phone.
        services.TryAddSingleton<ISpeechSettingsStore, InMemorySpeechSettingsStore>();

        // The Notifications tab's From and Repository choices, kept the same way: the
        // Android head registers its preferences store first, anything else keeps them in
        // memory.
        services.TryAddSingleton<Backlog.Mobile.UI.Notifications.INotificationFilterStore, Backlog.Mobile.UI.Notifications.InMemoryNotificationFilterStore>();

        // Whether the sync service is newer than this app, which the layout's
        // sync-paused callout reads. AddSyncClient registers the verdict a head's
        // clients write to; TryAdd so a host composed without one — the tests'
        // shell — still draws, with a verdict nothing ever changes.
        services.TryAddSingleton(_ => SyncAppVersion.OfEntryAssembly());
        services.TryAddSingleton<SyncCompatibility>();

        // The Android push (local ADR 0039 §4): a tap in the tray handed to the layout, and the
        // registration of this install's push token. The Android head registers Firebase's
        // token source first; every other host has no OS push, so its source answers no token
        // and nothing is ever registered. Singletons, because the head raises both from outside
        // any scope.
        services.TryAddSingleton<NotificationOpenRequests>();

        // The Capture shortcut on the Android launcher icon, handed to the layout the same way.
        // Registered here rather than in the head, so every host's layout can inject it.
        services.TryAddSingleton<CaptureOpenRequests>();
        services.TryAddSingleton<IPushTokenSource, NoPushTokenSource>();
        services.TryAddSingleton<PushRegistration>();

        return services;
    }

    /// <summary>
    /// Dictation as the screens inject it (local ADR 0045 §6): <typeparamref name="TOnDevice"/>,
    /// the host's own recogniser, and the cloud engine, behind the
    /// <see cref="SelectingSpeechTranscriber"/> that picks one each turn.
    /// </summary>
    /// <remarks>
    /// The host registers the rest, because each is its own: the <see cref="IAudioRecorder"/>
    /// for its platform, and <see cref="CloudTranscriptionClient"/> as a typed client against
    /// its sync address with the sync pipeline and no retry. Scoped, like the recognisers:
    /// one turn per screen's circuit or web view. The per-run fallback is a singleton.
    /// </remarks>
    public static IServiceCollection AddDictation<TOnDevice>(this IServiceCollection services)
        where TOnDevice : class, ISpeechTranscriber
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<TOnDevice>();
        services.TryAddSingleton<CloudDictationAvailability>();
        services.AddScoped<CloudSpeechTranscriber>();
        services.AddScoped<ISpeechTranscriber>(provider => new SelectingSpeechTranscriber(
            provider.GetRequiredService<TOnDevice>(),
            provider.GetRequiredService<CloudSpeechTranscriber>(),
            provider.GetRequiredService<ISpeechSettingsStore>(),
            provider.GetRequiredService<CloudDictationAvailability>(),
            provider.GetRequiredService<ISyncStatusSource>()));

        return services;
    }

    /// <summary>
    /// The device store in <paramref name="databasePath"/>, the outbox over it, and
    /// the kinds it sends. Singletons: one file per device, and one flush over it —
    /// two would send the same entry twice.
    /// </summary>
    public static IServiceCollection AddDeviceOutbox(this IServiceCollection services, string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IDeviceStore>(_ => new SqliteDeviceStore(databasePath));
        services.AddSingleton<DeviceOutbox>();

        // One registration per kind. Each holds a typed sync client for the life
        // of the app, which on a phone is the life of the process anyway.
        services.AddSingleton<IOutboxKind, CaptureOutboxKind>();
        services.AddSingleton<IOutboxKind, TaskOutboxKind>();

        // A talk note's files wait beside the database until the capture naming
        // them has arrived; the outbox row holds only where they are.
        services.AddSingleton(_ => new TalkNoteFiles(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "talk-notes")));
        services.AddSingleton<IOutboxKind, TalkNoteOutboxKind>();
        services.AddSingleton<TalkNoteComposer>();

        // The Tasks tab's fold of the task feed, in the same file: a projection
        // plus the outbox's task kind, never a second task store.
        services.AddSingleton<ITaskViewStore>(_ => new SqliteTaskViewStore(databasePath));
        services.AddSingleton<TaskViewProjection>();

        // The three edits to a task already in My Day, the one service the
        // Tasks tab's screens call. Stateless over the projection and the outbox.
        services.AddSingleton<TaskEdits>();

        // The notes the phone keeps and edits (.devbook/arc42/06-runtime-view.md#mobile-note-sync):
        // their own view and cursor over the task feed, and their own outbox kind,
        // whose files wait in the talk notes' outbox folder.
        services.AddSingleton<INoteViewStore>(_ => new SqliteNoteViewStore(databasePath));
        services.AddSingleton<NoteViewProjection>();
        services.AddSingleton<IOutboxKind, NoteOutboxKind>();

        // Goals, impediments and the stand-up (local ADR 0035 §8): their own view and
        // cursor over the task feed, the stand-up read from it with the desktop's rules, and
        // the impediment edits the phone queues for a desktop to apply.
        services.AddSingleton<IProgressViewStore>(_ => new SqliteProgressViewStore(databasePath));
        services.AddSingleton<ProgressViewProjection>();
        services.AddSingleton<PhoneStandUp>();
        services.AddSingleton<IOutboxKind, ImpedimentEditOutboxKind>();

        // Notifications as a viewer (local ADR 0039): the owner's feed folded into its own
        // view and cursor, and the replies the phone makes queued as their own outbox kind,
        // so a reply made with no signal is kept.
        services.AddSingleton<INotificationViewStore>(_ => new SqliteNotificationViewStore(databasePath));
        services.AddSingleton<NotificationViewProjection>();
        services.AddSingleton<IOutboxKind, ReplyOutboxKind>();

        // Knowledge events as a reader (local ADR 0050 §2): the owner's knowledge event feed
        // folded into its own view and cursor, and the one thing the phone asks about an event —
        // attaching a Talk note — queued as its own outbox kind on the task feed.
        services.AddSingleton<IEventViewStore>(_ => new SqliteEventViewStore(databasePath));
        services.AddSingleton<EventViewProjection>();
        services.AddSingleton<EventNoteAttachments>();
        services.AddSingleton<IOutboxKind, EventAttachmentRequestOutboxKind>();

        return services;
    }
}
