extern alias DesktopHarness;
extern alias MobileHarness;

using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sync;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// That a harness started by this suite writes nothing a person owns.
/// <para>
/// The desktop harness composes the per-user workspace — <c>Backlog.Debug</c> in a
/// Debug build and the real <c>Backlog</c> folder in a Release one — and seeds its
/// own checkout into the repository registry there, which replicates to every
/// paired device. A <c>dotnet test -c Release</c> run once wiped a real registry
/// that way. <see cref="IsolatedHarnessFactory{TEntryPoint}"/> is what keeps every
/// class here off it, and this is the test that says so.
/// </para>
/// <para>
/// Asked of the services the harness actually resolved rather than of the
/// variables, because a variable the harness stopped reading would still be set
/// and the file would quietly go back to where it was.
/// </para>
/// </summary>
public class HarnessIsolationTests
{
    private sealed class Harness : IsolatedHarnessFactory<DesktopHarness::Program>;

    [Fact]
    public void The_desktop_harness_keeps_its_workspace_registry_and_settings_in_the_test_folder()
    {
        using var harness = new Harness();
        var services = harness.Services;

        var workspace = services.GetRequiredService<WorkspaceSettingsStore>();
        var github = services.GetRequiredService<GitHubSettingsStore>();

        string[] paths =
        [
            workspace.RootDirectory,
            workspace.DatabasePath,
            github.RegistryPath,
            github.SettingsPath,
            // Registered by its port, so the store is reached through it.
            Assert.IsType<DeviceIdentityStore>(services.GetRequiredService<IDeviceIdentitySource>()).SettingsPath,
            // The rest of the stores the composition registers by their concrete
            // type; the ones behind a port alone are covered by the variables
            // HarnessIsolation sets beside these.
            services.GetRequiredService<WorkingHoursSettingsStore>().SettingsPath,
            services.GetRequiredService<MyDayPreferencesStore>().SettingsPath,
            services.GetRequiredService<ShellNavigationStore>().SettingsPath,
            services.GetRequiredService<PullRequestPinsStore>().SettingsPath,
            services.GetRequiredService<ClaudeSettingsStore>().SettingsPath,
            services.GetRequiredService<AzureFoundrySettingsStore>().SettingsPath,
            services.GetRequiredService<SyncServiceSettingsStore>().SettingsPath
        ];

        foreach (var path in paths)
        {
            Assert.StartsWith(HarnessIsolation.Root, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
            Assert.False(
                IsUnder(path, WorkspaceSettingsStore.DefaultAppDataDirectory),
                $"{path} lies in the per-user workspace folder.");
        }
    }

    /// <summary>
    /// The phone harness, which seeds its stand-up in Development, writes its device
    /// database — outbox and the cached task and Progress views — and keeps its
    /// credential in the test folder too. Before this, every host test that started
    /// it wrote the seeded goals into this worktree's own
    /// <c>obj/local-development/mobile-outbox.db</c>, where the next AppHost run's
    /// phone showed them beside synced data.
    /// </summary>
    [Fact]
    public void The_phone_harness_keeps_its_device_database_and_credential_in_the_test_folder()
    {
        using var harness = new IsolatedPhone();
        var services = harness.Services;

        Assert.StartsWith(HarnessIsolation.Root, Path.GetFullPath(services.GetRequiredService<IDeviceCredentialStore>().StorePath), StringComparison.OrdinalIgnoreCase);
        Assert.True(
            File.Exists(Path.Combine(HarnessIsolation.Root, "machine", "mobile-outbox.db")),
            "The phone harness's seeded device database is not in the test folder.");
    }

    private sealed class IsolatedPhone : IsolatedHarnessFactory<MobileHarness::Program>;

    private static bool IsUnder(string path, string folder) =>
        Path.GetFullPath(path).StartsWith(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
