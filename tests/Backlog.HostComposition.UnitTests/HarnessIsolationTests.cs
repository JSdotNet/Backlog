extern alias DesktopHarness;

using System.Text.RegularExpressions;

using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.AzureMaps;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.Devbook;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sqlite.Mcp;
using Backlog.Infrastructure.Sync;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Mcp;

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
            services.GetRequiredService<IterationSettingsStore>().SettingsPath,
            services.GetRequiredService<ShellNavigationStore>().SettingsPath,
            services.GetRequiredService<PullRequestPinsStore>().SettingsPath,
            services.GetRequiredService<ClaudeSettingsStore>().SettingsPath,
            services.GetRequiredService<AzureFoundrySettingsStore>().SettingsPath,
            services.GetRequiredService<AzureMapsSettingsStore>().SettingsPath,
            services.GetRequiredService<SyncServiceSettingsStore>().SettingsPath,
            // Registered by its port alone, and a database file rather than settings.
            Assert.IsType<SqliteMcpAuditLog>(services.GetRequiredService<IMcpAuditLog>()).DatabasePath
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
    /// Read from the harnesses' source rather than their services, like the
    /// architecture tests: a variable a harness starts reading without one here
    /// would put a file back in this worktree's <c>obj/local-development</c>,
    /// shared with every factory and with any harness the AppHost runs.
    /// </summary>
    [Theory]
    [InlineData("Backlog.Desktop.WebHarness")]
    [InlineData("Backlog.Mobile.WebHarness")]
    public void Every_file_variable_a_harness_reads_is_redirected(string harness)
    {
        var source = File.ReadAllText(RepositoryRoot.File("src", "Harness", harness, "Program.cs"));
        var read = Regex.Matches(source, "\"(BACKLOG_[A-Z0-9_]+)\"").Select(match => match.Groups[1].Value).Distinct();

        var missing = read.Except(HarnessIsolation.Redirected).Except(HarnessIsolation.NotFiles).Order(StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0, $"{harness} reads {string.Join(", ", missing)}, which HarnessIsolation does not redirect.");
    }

    /// <summary>
    /// The devbook database location is process-wide and each desktop host's app
    /// refresher configures it, so a host that stops must hand it back: left in
    /// place, its delegate read a disposed service provider and the next host's
    /// import threw <see cref="ObjectDisposedException"/> (local ADR 0015's resolver;
    /// the flaky <see cref="ImportPlanChangeSetTests"/>).
    /// </summary>
    [Fact]
    public void A_stopped_harness_leaves_the_devbook_database_location_to_the_ones_still_running()
    {
        using var running = new Harness();
        _ = running.Services;

        var stopped = new Harness();
        _ = stopped.Services;
        stopped.Dispose();

        var exception = Record.Exception(() => DevbookDatabaseLocation.DatabasesDirectory);

        Assert.Null(exception);
    }

    private static bool IsUnder(string path, string folder) =>
        Path.GetFullPath(path).StartsWith(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
