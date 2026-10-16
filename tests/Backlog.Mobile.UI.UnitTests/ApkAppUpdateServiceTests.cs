using Backlog.Mobile.UI.AppUpdate;
using Backlog.UI.Components.AppUpdate;

using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The phone's update service: a build with no update channel compiled in answers
/// every request with "a development build cannot update itself", and a release build
/// checks GitHub against the installed versionCode and hands the newest APK to the
/// platform installer only when the person asks for it.
/// </summary>
public sealed class ApkAppUpdateServiceTests
{
    private const string Feed_0_1_60 =
        """[{"tag_name":"mobile-v0.1.60","draft":false,"assets":[{"name":"Backlog.Mobile_0.1.60.apk","state":"uploaded","size":31457280,"browser_download_url":"https://github.com/JSdotNet/Backlog/releases/download/mobile-v0.1.60/Backlog.Mobile_0.1.60.apk"}]}]""";

    private static ApkAppUpdateService Service(
        ApkUpdateChannel channel,
        RecordingInstaller installer,
        AppUpdateCheckTests.ScriptedGitHub? github = null) =>
        new(
            channel,
            installer,
            new GitHubApkUpdateCheck(
                new HttpClient(github ?? AppUpdateCheckTests.ScriptedGitHub.Answering(Feed_0_1_60)),
                channel,
                TimeProvider.System,
                NullLogger<GitHubApkUpdateCheck>.Instance),
            NullLogger<ApkAppUpdateService>.Instance);

    [Fact]
    public async Task A_development_build_says_so_and_points_at_the_install_script()
    {
        var installer = new RecordingInstaller(installedVersionCode: 1, currentVersion: "1.0");
        var github = AppUpdateCheckTests.ScriptedGitHub.Answering(Feed_0_1_60);
        var updates = Service(ApkUpdateChannel.None, installer, github);

        var check = await updates.CheckForUpdateAsync(TestContext.Current.CancellationToken);
        var install = await updates.StartUpdateAsync(TestContext.Current.CancellationToken);

        Assert.False(updates.IsSupported);
        Assert.Equal("1.0", updates.CurrentVersion);
        Assert.Equal(AppUpdateAvailability.Unsupported, check.Availability);
        Assert.Contains("Install-AndroidApp.ps1", check.Message);
        Assert.False(install.Started);
        Assert.Contains("Install-AndroidApp.ps1", install.Message);
        Assert.Empty(installer.Installed);
        Assert.Empty(github.Requests);
    }

    [Fact]
    public async Task A_release_build_checks_against_the_installed_version_code()
    {
        var installer = new RecordingInstaller(installedVersionCode: 100059L, currentVersion: "0.1.59");
        var updates = Service(new ApkUpdateChannel("JSdotNet/Backlog"), installer);

        var check = await updates.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        Assert.True(updates.IsSupported);
        Assert.Equal(AppUpdateAvailability.Available, check.Availability);
        Assert.Equal("0.1.60", check.AvailableVersion);
        Assert.Empty(installer.Installed);
    }

    [Fact]
    public async Task Installing_hands_the_newest_apk_to_the_platform_installer()
    {
        var installer = new RecordingInstaller(installedVersionCode: 100059L, currentVersion: "0.1.59")
        {
            Answer = AppUpdateInstallResult.InProgress("Confirm the update in the Android installer.")
        };
        var updates = Service(new ApkUpdateChannel("JSdotNet/Backlog"), installer);

        var install = await updates.StartUpdateAsync(TestContext.Current.CancellationToken);

        var release = Assert.Single(installer.Installed);
        Assert.Equal("0.1.60", release.Version.Display);
        Assert.Equal(
            new Uri("https://github.com/JSdotNet/Backlog/releases/download/mobile-v0.1.60/Backlog.Mobile_0.1.60.apk"),
            release.DownloadUrl);
        Assert.True(install.Started);
        Assert.Equal("Confirm the update in the Android installer.", install.Message);
    }

    [Fact]
    public async Task Nothing_newer_installs_nothing()
    {
        var installer = new RecordingInstaller(installedVersionCode: 100060L, currentVersion: "0.1.60");
        var updates = Service(new ApkUpdateChannel("JSdotNet/Backlog"), installer);

        var install = await updates.StartUpdateAsync(TestContext.Current.CancellationToken);

        Assert.False(install.Started);
        Assert.Empty(installer.Installed);
    }

    [Fact]
    public async Task An_installer_that_throws_is_a_failed_install_not_a_crash()
    {
        var installer = new RecordingInstaller(installedVersionCode: 100059L, currentVersion: "0.1.59")
        {
            Throws = new IOException("No space left on device.")
        };
        var updates = Service(new ApkUpdateChannel("JSdotNet/Backlog"), installer);

        var install = await updates.StartUpdateAsync(TestContext.Current.CancellationToken);

        Assert.False(install.Started);
        Assert.False(string.IsNullOrWhiteSpace(install.Message));
    }

    private sealed class RecordingInstaller(long installedVersionCode, string currentVersion) : IApkInstaller
    {
        public List<ApkRelease> Installed { get; } = [];

        public AppUpdateInstallResult Answer { get; init; } = AppUpdateInstallResult.InProgress();

        public Exception? Throws { get; init; }

        public string CurrentVersion => currentVersion;

        public long InstalledVersionCode => installedVersionCode;

        public Task<AppUpdateInstallResult> InstallAsync(ApkRelease release, CancellationToken cancellationToken = default)
        {
            Installed.Add(release);
            return Throws is null ? Task.FromResult(Answer) : Task.FromException<AppUpdateInstallResult>(Throws);
        }
    }
}
