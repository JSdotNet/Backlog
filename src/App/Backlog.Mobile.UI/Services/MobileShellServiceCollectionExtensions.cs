using Backlog.Infrastructure.Sync;
using Backlog.Mobile.UI.AppUpdate;
using Backlog.Mobile.UI.Notes;
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

        // Whether the sync service is newer than this app, which the layout's
        // sync-paused callout reads. AddSyncClient registers the verdict a head's
        // clients write to; TryAdd so a host composed without one — the tests'
        // shell — still draws, with a verdict nothing ever changes.
        services.TryAddSingleton(_ => SyncAppVersion.OfEntryAssembly());
        services.TryAddSingleton<SyncCompatibility>();

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

        return services;
    }
}
