
namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The update result model is the contract the Settings page renders verbatim,
/// and the version formatter is what a person sees as "which build am I on".
/// Both are pure, so they are worth testing directly rather than through a head.
/// </summary>
public class AppUpdateTests
{
    [Fact]
    public void An_available_update_is_ready_to_install()
    {
        var result = AppUpdateCheckResult.Available();

        Assert.Equal(AppUpdateAvailability.Available, result.Availability);
        Assert.True(result.UpdateReady);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void A_required_update_is_also_ready_to_install()
    {
        var result = AppUpdateCheckResult.Required();

        Assert.Equal(AppUpdateAvailability.Required, result.Availability);
        Assert.True(result.UpdateReady);
    }

    [Theory]
    [InlineData(AppUpdateAvailability.UpToDate)]
    [InlineData(AppUpdateAvailability.Unsupported)]
    [InlineData(AppUpdateAvailability.Failed)]
    [InlineData(AppUpdateAvailability.Unknown)]
    public void Only_available_or_required_offers_an_install(AppUpdateAvailability availability)
    {
        var result = new AppUpdateCheckResult(availability, "message");

        Assert.False(result.UpdateReady);
    }

    [Fact]
    public void Every_factory_carries_a_human_readable_message()
    {
        AppUpdateCheckResult[] results =
        [
            AppUpdateCheckResult.UpToDate(),
            AppUpdateCheckResult.Available(),
            AppUpdateCheckResult.Required(),
            AppUpdateCheckResult.Unsupported("nope"),
            AppUpdateCheckResult.Failed("boom")
        ];

        Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.Message)));
    }

    [Fact]
    public void A_custom_message_is_kept()
    {
        var result = AppUpdateCheckResult.Unsupported("Updates are managed elsewhere.");

        Assert.Equal("Updates are managed elsewhere.", result.Message);
    }

    [Fact]
    public void An_available_update_names_the_version_it_would_install()
    {
        var result = AppUpdateCheckResult.Available(availableVersion: "0.1.65.0");

        Assert.Equal("0.1.65.0", result.AvailableVersion);
        Assert.Equal("Version 0.1.65.0 is available.", result.Message);
        Assert.True(result.UpdateReady);
    }

    [Fact]
    public void A_required_update_names_the_version_it_would_install()
    {
        var result = AppUpdateCheckResult.Required(availableVersion: "0.1.65.0");

        Assert.Equal("0.1.65.0", result.AvailableVersion);
        Assert.Equal("Version 0.1.65.0 is a required update.", result.Message);
        Assert.True(result.UpdateReady);
    }

    [Fact]
    public void An_update_whose_version_is_unknown_is_still_reported()
    {
        var result = AppUpdateCheckResult.Available();

        Assert.Null(result.AvailableVersion);
        Assert.Equal("An update is available.", result.Message);
        Assert.True(result.UpdateReady);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_version_counts_as_unknown(string version)
    {
        var result = AppUpdateCheckResult.Available(availableVersion: version);

        Assert.Null(result.AvailableVersion);
        Assert.Equal("An update is available.", result.Message);
    }

    [Fact]
    public void A_padded_version_is_trimmed()
    {
        var result = AppUpdateCheckResult.Required(availableVersion: " 0.1.65.0 ");

        Assert.Equal("0.1.65.0", result.AvailableVersion);
        Assert.Contains("Version 0.1.65.0", result.Message);
    }

    [Fact]
    public void A_custom_message_still_carries_the_version()
    {
        var result = AppUpdateCheckResult.Available("Grab it while it is hot.", "0.1.65.0");

        Assert.Equal("Grab it while it is hot.", result.Message);
        Assert.Equal("0.1.65.0", result.AvailableVersion);
    }

    [Fact]
    public void An_install_in_progress_reports_started()
    {
        var result = AppUpdateInstallResult.InProgress();

        Assert.True(result.Started);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("failed")]
    public void A_non_started_install_reports_not_started(string kind)
    {
        var result = kind == "unsupported"
            ? AppUpdateInstallResult.Unsupported("no source")
            : AppUpdateInstallResult.Failed("boom");

        Assert.False(result.Started);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }
}

/// <summary>
/// The version the update window names comes from the published .appinstaller,
/// so the parser must read what the release workflow writes and shrug at anything
/// else: a bad manifest means "version unknown", never a failed check.
/// </summary>
public class AppInstallerManifestTests
{
    private const string ReleaseManifest = """
        <?xml version="1.0" encoding="utf-8"?>
        <AppInstaller
            xmlns="http://schemas.microsoft.com/appx/appinstaller/2018"
            Uri="https://github.com/JSdotNet/Backlog/releases/latest/download/Backlog.Desktop.appinstaller"
            Version="0.1.65.0">

          <MainPackage
              Name="JSdotNet.Backlog"
              Publisher="CN=JSdotNet"
              Version="0.1.65.0"
              ProcessorArchitecture="x64"
              Uri="https://github.com/JSdotNet/Backlog/releases/download/v0.1.65/Backlog.Desktop_0.1.65.0_x64.msix" />

          <UpdateSettings>
            <OnLaunch HoursBetweenUpdateChecks="8" ShowPrompt="true" />
            <AutomaticBackgroundTask />
          </UpdateSettings>

        </AppInstaller>
        """;

    [Fact]
    public void The_release_manifest_names_its_package_version()
    {
        Assert.Equal("0.1.65.0", AppInstallerManifest.ReadPackageVersion(ReleaseManifest));
    }

    [Fact]
    public void The_package_version_wins_over_the_manifest_version()
    {
        const string xml = """
            <AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2018" Version="9.9.9.9">
              <MainPackage Name="n" Publisher="p" Version="0.1.65.0" Uri="https://example.test/a.msix" />
            </AppInstaller>
            """;

        Assert.Equal("0.1.65.0", AppInstallerManifest.ReadPackageVersion(xml));
    }

    [Fact]
    public void A_bundle_manifest_is_read_the_same_way()
    {
        const string xml = """
            <AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2017" Version="1.0.0.0">
              <MainBundle Name="n" Publisher="p" Version="1.2.3.0" Uri="https://example.test/a.msixbundle" />
            </AppInstaller>
            """;

        Assert.Equal("1.2.3.0", AppInstallerManifest.ReadPackageVersion(xml));
    }

    [Fact]
    public void A_newer_schema_namespace_is_not_a_problem()
    {
        const string xml = """
            <AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2021" Version="1.0.0.0">
              <MainPackage Name="n" Publisher="p" Version="1.2.3.0" Uri="https://example.test/a.msix" />
            </AppInstaller>
            """;

        Assert.Equal("1.2.3.0", AppInstallerManifest.ReadPackageVersion(xml));
    }

    [Fact]
    public void A_padded_version_attribute_is_trimmed()
    {
        const string xml = """
            <AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2018" Version="1.0.0.0">
              <MainPackage Name="n" Publisher="p" Version=" 1.2.3.0 " Uri="https://example.test/a.msix" />
            </AppInstaller>
            """;

        Assert.Equal("1.2.3.0", AppInstallerManifest.ReadPackageVersion(xml));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not xml at all")]
    [InlineData("<AppInstaller><MainPackage Version=\"1.0\"")]
    public void Nothing_readable_means_version_unknown_not_an_exception(string? xml)
    {
        Assert.Null(AppInstallerManifest.ReadPackageVersion(xml));
    }

    [Fact]
    public void A_document_that_is_not_a_manifest_yields_no_version()
    {
        const string xml = """<html><body>404 Not Found</body></html>""";

        Assert.Null(AppInstallerManifest.ReadPackageVersion(xml));
    }

    [Fact]
    public void A_manifest_without_a_package_version_yields_no_version()
    {
        const string xml = """
            <AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2018" Version="1.0.0.0">
              <MainPackage Name="n" Publisher="p" Uri="https://example.test/a.msix" />
            </AppInstaller>
            """;

        Assert.Null(AppInstallerManifest.ReadPackageVersion(xml));
    }

    [Fact]
    public void A_manifest_with_a_dtd_is_refused_rather_than_expanded()
    {
        const string xml = """
            <!DOCTYPE AppInstaller [<!ENTITY v "1.2.3.0">]>
            <AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2018" Version="1.0.0.0">
              <MainPackage Name="n" Publisher="p" Version="&v;" Uri="https://example.test/a.msix" />
            </AppInstaller>
            """;

        Assert.Null(AppInstallerManifest.ReadPackageVersion(xml));
    }
}

/// <summary>
/// The unsupported service is what the web host and any unpackaged run rely on:
/// it must report a clear reason and never throw.
/// </summary>
public class UnsupportedAppUpdateServiceTests
{
    [Fact]
    public void It_is_never_supported()
    {
        var service = new UnsupportedAppUpdateService();

        Assert.False(service.IsSupported);
    }

    [Fact]
    public void It_reports_a_current_version()
    {
        var service = new UnsupportedAppUpdateService();

        Assert.False(string.IsNullOrWhiteSpace(service.CurrentVersion));
    }

    [Fact]
    public async Task Checking_is_answered_unsupported_with_the_configured_message()
    {
        var service = new UnsupportedAppUpdateService("Handled by your package manager.");

        var result = await service.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AppUpdateAvailability.Unsupported, result.Availability);
        Assert.Equal("Handled by your package manager.", result.Message);
        Assert.False(result.UpdateReady);
    }

    [Fact]
    public async Task Installing_is_answered_unsupported_not_thrown()
    {
        var service = new UnsupportedAppUpdateService("Handled by your package manager.");

        var result = await service.StartUpdateAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Started);
        Assert.Equal("Handled by your package manager.", result.Message);
    }

    [Fact]
    public async Task The_default_message_explains_who_owns_updates()
    {
        var service = new UnsupportedAppUpdateService();

        var result = await service.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        Assert.Contains("managed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_supplied_version_is_reported_verbatim()
    {
        var service = new UnsupportedAppUpdateService(currentVersion: "9.9.9");

        Assert.Equal("9.9.9", service.CurrentVersion);
    }
}

/// <summary>
/// The version in the header opens the update window, while the update window owns
/// checking, installing, and status presentation.
/// </summary>
public class AppUpdatePresentationTests
{
    [Fact]
    public void The_idle_label_says_what_clicking_the_version_does()
    {
        Assert.Equal("Check for updates", AppUpdatePresentation.CheckLabel(isChecking: false));
    }

    [Fact]
    public void The_busy_label_says_a_check_is_running()
    {
        Assert.Equal("Checking...", AppUpdatePresentation.CheckLabel(isChecking: true));
    }

    [Fact]
    public void The_header_version_accessible_name_opens_the_update_window()
    {
        var label = AppUpdatePresentation.VersionWindowLabel("1.2.3");

        Assert.Contains("1.2.3", label);
        Assert.Contains("Open update window", label);
    }

    [Fact]
    public void The_header_names_the_worktree_when_a_host_supplies_one()
    {
        var label = AppUpdatePresentation.WorkspaceWindowLabel("dev-mode-app-title (claude/titles)");

        Assert.Contains("dev-mode-app-title (claude/titles)", label);
        Assert.Contains("Open update window", label);
        Assert.DoesNotContain("Version", label);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_worktree_still_produces_an_accessible_name(string? workspace)
    {
        var label = AppUpdatePresentation.WorkspaceWindowLabel(workspace);

        Assert.Contains("unknown", label);
        Assert.Contains("Open update window", label);
    }

    [Fact]
    public void The_header_version_accessible_name_does_not_start_a_check()
    {
        var label = AppUpdatePresentation.VersionWindowLabel("1.2.3");

        Assert.DoesNotContain("Check for updates", label);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_version_still_produces_an_accessible_name(string? version)
    {
        var label = AppUpdatePresentation.VersionWindowLabel(version);

        Assert.Contains("unknown", label);
        Assert.Contains("Open update window", label);
    }

    [Theory]
    [InlineData(AppUpdateAvailability.UpToDate, "app-version__status--ok")]
    [InlineData(AppUpdateAvailability.Available, "app-version__status--available")]
    [InlineData(AppUpdateAvailability.Required, "app-version__status--available")]
    [InlineData(AppUpdateAvailability.Failed, "app-version__status--error")]
    public void An_outcome_gets_its_own_status_colour(AppUpdateAvailability availability, string expected)
    {
        var css = AppUpdatePresentation.StatusClass(availability);

        Assert.Contains(expected, css);
    }

    [Theory]
    [InlineData(AppUpdateAvailability.Unknown)]
    [InlineData(AppUpdateAvailability.Unsupported)]
    public void A_neutral_outcome_is_rendered_without_a_colour_modifier(AppUpdateAvailability availability)
    {
        Assert.Equal("app-version__status", AppUpdatePresentation.StatusClass(availability));
    }

    [Theory]
    [InlineData(AppUpdateAvailability.Unknown)]
    [InlineData(AppUpdateAvailability.UpToDate)]
    [InlineData(AppUpdateAvailability.Available)]
    [InlineData(AppUpdateAvailability.Required)]
    [InlineData(AppUpdateAvailability.Unsupported)]
    [InlineData(AppUpdateAvailability.Failed)]
    public void Every_outcome_keeps_the_base_status_class(AppUpdateAvailability availability)
    {
        Assert.StartsWith("app-version__status", AppUpdatePresentation.StatusClass(availability));
    }
}
