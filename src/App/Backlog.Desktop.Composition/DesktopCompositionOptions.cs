using Backlog.Desktop.UI.AppUpdate;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.SpecManager;
using Backlog.Infrastructure.Sync;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.Composition;

/// <summary>
/// What the two desktop heads do differently, and nothing they do alike.
/// <para>
/// <see cref="DesktopCompositionRegistration.AddDesktopComposition"/> registers the
/// same modules and adapters for both. What it cannot decide for them is here: where
/// each head keeps a machine's files — the installed app under the per-user app-data
/// folder, the harness under its own content root so a session there never rewrites
/// the real ones — which adapter answers a port the harness may not exercise for
/// real, and how long one window's state lives. Every member is required, so a head
/// that forgot one does not compile rather than silently taking the other head's
/// answer.
/// </para>
/// <para>
/// Ports rather than configuration, so this is not an <c>IOptions&lt;T&gt;</c>
/// section: nothing here is read from a settings source, and most of it is a factory
/// over the provider because the store it builds reads other registrations.
/// </para>
/// </summary>
public sealed class DesktopCompositionOptions
{
    /// <summary>Which machine this installation is. An instance rather than a
    /// factory: the identity belongs to the installation, not to whichever surface
    /// first asks, so the head constructs it at startup and <c>device.json</c>
    /// exists from the first start.</summary>
    public required IDeviceIdentitySource DeviceIdentity { get; init; }

    /// <summary>The device half of cloud sync: where this device's registration
    /// credential is kept. DPAPI in the installed app (ADR 0005); a file under the
    /// content root in the harness, so it pairs as a device of its own.</summary>
    public required Func<IServiceProvider, IDeviceCredentialStore> DeviceCredentialStore { get; init; }

    /// <summary>This device's task replication progress, beside the credential and
    /// never under the workspace root: the watermark describes how far this machine
    /// has got, and a workspace root can be a folder some other product syncs.</summary>
    public required Func<IServiceProvider, ITaskSyncStateStore> TaskSyncStateStore { get; init; }

    /// <summary>The folder session replication keeps its two files in.</summary>
    public required string SessionSyncFolder { get; init; }

    /// <summary>The folder annotation replication keeps its progress file in.</summary>
    public required string AnnotationSyncFolder { get; init; }

    /// <summary>The file what the last backup did is kept in.</summary>
    public required string BackupStatePath { get; init; }

    /// <summary>The file the sync service's address from the Settings page is kept
    /// in.</summary>
    public required string SyncServiceSettingsPath { get; init; }

    /// <summary>The feature switches, over <c>AppFeatures.All</c>.</summary>
    public required Func<IServiceProvider, IAppFeatureSettings> FeatureSettings { get; init; }

    /// <summary>Which hours the reader means to be working. The store rather than
    /// the port: the pace store carries the same week (local ADR 0019) and is built
    /// over it.</summary>
    public required Func<IServiceProvider, WorkingHoursSettingsStore> WorkingHoursSettings { get; init; }

    /// <summary>When the assistant's weekly allowance resets.</summary>
    public required Func<IServiceProvider, IUsageResetSettings> UsageResetSettings { get; init; }

    /// <summary>The reader's pace, which the roadmap places by. It carries the
    /// working week too, so build it over the registered
    /// <see cref="WorkingHoursSettingsStore"/>.</summary>
    public required Func<IServiceProvider, PlanningVelocitySettingsStore> PlanningVelocitySettings { get; init; }

    /// <summary>Which surface the shell was last showing.</summary>
    public required Func<IServiceProvider, ShellNavigationStore> ShellNavigation { get; init; }

    /// <summary>Where Capture's monitored sources are kept.</summary>
    public required Func<IServiceProvider, ICaptureSourceSettings> CaptureSourceSettings { get; init; }

    /// <summary>Where what past capture runs said is kept.</summary>
    public required Func<IServiceProvider, ICaptureRunLog> CaptureRunLog { get; init; }

    /// <summary>The repositories and products connected for linked tasks, with their
    /// settings and sync progress (local ADR 0020, §9).</summary>
    public required Func<IServiceProvider, IConnectedTargets> ConnectedTargets { get; init; }

    /// <summary>Where the spec-manager sign-in is kept: the client registration and
    /// the token pair. DPAPI at its default path beside <c>github.json</c> in the
    /// installed app; a file under the content root in the harness, so a sign-in
    /// there is not the installed app's. Never a settings file.</summary>
    public required Func<IServiceProvider, ISpecManagerTokenStore> SpecManagerTokenStore { get; init; }

    /// <summary>The reader's inbox routing rules.</summary>
    public required Func<IServiceProvider, IInboxRoutingRules> InboxRoutingRules { get; init; }

    /// <summary>The GitHub settings, given the workspace root the shared half of
    /// them follows. The composition reloads the store when that root moves.</summary>
    public required Func<Func<string>, GitHubSettingsStore> GitHubSettings { get; init; }

    /// <summary>The pull requests pinned on this device. The per-user file in the
    /// installed app; one under the content root in the harness, so a pin made there
    /// is not the installed app's.</summary>
    public required Func<IServiceProvider, PullRequestPinsStore> PullRequestPins { get; init; }

    /// <summary>The Claude Admin API settings.</summary>
    public required Func<IServiceProvider, ClaudeSettingsStore> ClaudeSettings { get; init; }

    /// <summary>The Azure Foundry connection and cost scope.</summary>
    public required Func<IServiceProvider, AzureFoundrySettingsStore> AzureFoundrySettings { get; init; }

    /// <summary>Dev PC Management's tools adapter: the full one that runs the CLIs
    /// in the installed app, the catalog-only one in the harness.</summary>
    public required Func<IServiceProvider, IDevToolService> DevToolService { get; init; }

    /// <summary>What opens a devbook folder in an editor.</summary>
    public required Func<IServiceProvider, IFolderEditorLauncher> FolderEditorLauncher { get; init; }

    /// <summary>Whether and how this head updates itself.</summary>
    public required Func<IServiceProvider, IAppUpdateService> AppUpdateService { get; init; }

    /// <summary>What an "open in Copilot CLI" offer starts.</summary>
    public required Func<IServiceProvider, ICopilotCliLauncher> CopilotCliLauncher { get; init; }

    /// <summary>Which registered clone a session's working folder lies inside.</summary>
    public required Func<IServiceProvider, ISessionRepositoryResolver> SessionRepositoryResolver { get; init; }

    /// <summary>
    /// The lifetime of the state one window holds: the Tasks and Inbox panes'
    /// state, the save-state band that reads the first, the toast channel, the
    /// Report issue channel and the Inbox's plan drafter.
    /// <para>
    /// <see cref="ServiceLifetime.Singleton"/> in the MAUI head, which has one window
    /// and one user, so one object outlives every page drawn on it.
    /// <see cref="ServiceLifetime.Scoped"/> in the harness, which has one Blazor
    /// Server circuit per visitor: a singleton there would show one visitor's toasts
    /// to every other, and a singleton over the modules' scoped services is a captive
    /// dependency validation refuses.
    /// </para>
    /// </summary>
    public required ServiceLifetime WindowStateLifetime { get; init; }
}
