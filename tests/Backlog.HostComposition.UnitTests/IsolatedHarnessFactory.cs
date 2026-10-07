using Backlog.Infrastructure.FileSystem;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// A browser harness started the way the AppHost starts it, over files of the
/// test's own.
/// <para>
/// Development on purpose, for the reason <see cref="WebHarnessHostTests"/> gives:
/// the generic host only turns <c>ValidateOnBuild</c> and <c>ValidateScopes</c> on
/// there, and that validation is what turns an unsatisfiable registration into a
/// startup failure.
/// </para>
/// <para>
/// The isolation is the other half, and it is not optional. The desktop harness
/// composes the per-user workspace — <c>Backlog.Debug</c> in a Debug build, the
/// real <c>Backlog</c> folder in a Release one — and seeds its checkout into the
/// repository registry there, which replicates to a person's other devices. What
/// is redirected, and what is not:
/// </para>
/// <list type="bullet">
/// <item>Everything resolved from <see cref="WorkspaceSettingsStore"/> —
/// <c>backlog.db</c>, the shared registry, the caches, the backup settings — goes
/// to a fresh temporary folder per factory, by replacing the store.</item>
/// <item>Every per-machine file the harness takes from a <c>BACKLOG_*</c>
/// variable goes to one temporary folder per process; see
/// <see cref="HarnessIsolation"/>.</item>
/// <item>Not redirected: <c>usage-reset.settings.json</c> and
/// <c>planning-velocity.settings.json</c>, which the harness names with no
/// variable and which still land in this worktree's
/// <c>obj/local-development</c>; the agent profile folders the Sessions sources
/// read; and the delivery-run telemetry they write, which only
/// <see cref="TelemetryEndpointGateTests"/> posts to, through a recording
/// double.</item>
/// </list>
/// <para>
/// Harmless for a harness that composes no workspace store, which is the mobile
/// one: nothing is replaced that was not registered, and the variables are ones
/// it does not read.
/// </para>
/// </summary>
internal abstract class IsolatedHarnessFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    /// <summary>This factory's workspace. Asking for it is also what runs
    /// <see cref="HarnessIsolation"/>'s static constructor, so the variables are
    /// set before this factory's host is built — which is when the harness reads
    /// them.</summary>
    private readonly string _workspace = HarnessIsolation.NewWorkspace();

    protected sealed override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        ConfigureHarness(builder);

        // Test services, so they run after the harness's own registrations
        // whatever a subclass does above: the last registration is the one resolved.
        builder.ConfigureTestServices(services =>
        {
            if (!services.Any(descriptor => descriptor.ServiceType == typeof(WorkspaceSettingsStore))) return;

            services.RemoveAll<WorkspaceSettingsStore>();

            // The probe answers "no synced folder", so whether this machine has
            // OneDrive signed in is not something a host test depends on. With no
            // settings file yet, the root is the folder itself.
            services.AddSingleton(_ => new WorkspaceSettingsStore(
                _workspace,
                Path.Combine(_workspace, "settings.json"),
                _ => null));
        });
    }

    /// <summary>What a test class adds — its feature switches, its doubles.</summary>
    protected virtual void ConfigureHarness(IWebHostBuilder builder)
    {
    }
}

/// <summary>
/// The one temporary folder this test process gives the harnesses, and the
/// desktop harness's <c>BACKLOG_*</c> file variables pointed into it.
/// <para>
/// Once per process, in the static constructor, and never per test: an
/// environment variable is process-wide and xUnit runs the classes in parallel, so
/// a variable set per test would move the files of a harness another class is
/// still running.
/// </para>
/// <para>
/// Every variable in the desktop harness's <c>Program</c> that names a file or a
/// folder is here. The two it reads that do not are left alone:
/// <c>BACKLOG_REPOSITORY_ROOT</c> says which checkout to seed, and
/// <c>BACKLOG_AZURE_FOUNDRY_LOCAL_ENDPOINT</c> is an address.
/// </para>
/// </summary>
internal static class HarnessIsolation
{
    /// <summary>Each variable, the name its file or folder gets, and whether the
    /// variable names a folder rather than a file.</summary>
    private static readonly (string Variable, string Name, bool IsFolder)[] Overrides =
    [
        ("BACKLOG_GITHUB_SETTINGS_PATH", "github.settings.json", false),
        ("BACKLOG_DEVICE_IDENTITY_PATH", "device.json", false),
        ("BACKLOG_DESKTOP_DEVICE_CREDENTIAL_PATH", "device-credential.json", false),
        ("BACKLOG_DESKTOP_TASK_SYNC_STATE_PATH", "task-sync-state.json", false),
        ("BACKLOG_DESKTOP_SESSION_SYNC_PATH", "session-sync", true),
        ("BACKLOG_DESKTOP_ANNOTATION_SYNC_PATH", "annotation-sync", true),
        ("BACKLOG_DESKTOP_BACKUP_STATE_PATH", "backup-state.json", false),
        ("BACKLOG_DESKTOP_SYNC_SERVICE_SETTINGS_PATH", "sync-service.json", false),
        ("BACKLOG_AZURE_FOUNDRY_SETTINGS_PATH", "azure-foundry.settings.json", false),
        ("BACKLOG_FEATURE_SETTINGS_PATH", "feature.settings.json", false),
        ("BACKLOG_WORKING_HOURS_SETTINGS_PATH", "working-hours.settings.json", false),
        ("BACKLOG_INBOX_ROUTING_RULES_PATH", "inbox-routing-rules.settings.json", false),
        ("BACKLOG_CAPTURE_SOURCES_SETTINGS_PATH", "capture-sources.settings.json", false),
        ("BACKLOG_CONNECTED_TARGETS_SETTINGS_PATH", "connected-targets.settings.json", false),
        ("BACKLOG_SPEC_MANAGER_CREDENTIALS_PATH", "spec-manager-credentials.json", false),
        ("BACKLOG_CAPTURE_RUN_LOG_PATH", "capture-runs.json", false),
        ("BACKLOG_CAPTURE_TARGETS_PATH", "capture-targets.json", false),
        ("BACKLOG_PULL_REQUEST_PINS_PATH", "pull-request-pins.json", false),
        ("BACKLOG_SHELL_NAVIGATION_SETTINGS_PATH", "shell-navigation.settings.json", false),
        ("BACKLOG_CLAUDE_SETTINGS_PATH", "claude.settings.json", false)
    ];

    static HarnessIsolation()
    {
        Root = Path.Combine(Path.GetTempPath(), "backlog-host-composition-" + Guid.NewGuid().ToString("N"));
        var machine = Path.Combine(Root, "machine");
        Directory.CreateDirectory(machine);

        foreach (var (variable, name, isFolder) in Overrides)
        {
            var path = Path.Combine(machine, name);
            if (isFolder) Directory.CreateDirectory(path);
            Environment.SetEnvironmentVariable(variable, path);
        }

        // Best effort: a pooled SQLite connection may still hold a database open,
        // and a temporary folder left behind is the operating system's to clear.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }

    /// <summary>
    /// The process's temporary folder. Under it: <c>machine/</c>, holding every
    /// file and folder the variables above name, and one <c>workspace-*</c> folder
    /// per factory, holding what the workspace store resolves. The two
    /// variable-less files the class summary names are not under it.
    /// </summary>
    public static string Root { get; }

    /// <summary>A workspace of one factory's own, so two classes running in
    /// parallel never share a <c>backlog.db</c>.</summary>
    public static string NewWorkspace() => Path.Combine(Root, "workspace-" + Guid.NewGuid().ToString("N"));
}
