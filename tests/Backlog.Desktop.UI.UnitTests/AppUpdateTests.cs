using Backlog.Desktop.UI.Services;

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

        var result = await service.CheckForUpdateAsync();

        Assert.Equal(AppUpdateAvailability.Unsupported, result.Availability);
        Assert.Equal("Handled by your package manager.", result.Message);
        Assert.False(result.UpdateReady);
    }

    [Fact]
    public async Task Installing_is_answered_unsupported_not_thrown()
    {
        var service = new UnsupportedAppUpdateService("Handled by your package manager.");

        var result = await service.StartUpdateAsync();

        Assert.False(result.Started);
        Assert.Equal("Handled by your package manager.", result.Message);
    }

    [Fact]
    public async Task The_default_message_explains_who_owns_updates()
    {
        var service = new UnsupportedAppUpdateService();

        var result = await service.CheckForUpdateAsync();

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
/// Version formatting is what the "About" section shows; the informational
/// version carries a source-revision suffix the SDK appends that a person should
/// never see.
/// </summary>
public class AppVersionTests
{
    [Theory]
    [InlineData("1.2.3+abc1234", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.0.0-preview.2+deadbeef", "1.0.0-preview.2")]
    [InlineData("  1.4.0+meta  ", "1.4.0")]
    public void Normalize_drops_the_source_revision_suffix(string input, string expected)
    {
        Assert.Equal(expected, AppVersion.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("+onlymetadata")]
    public void Normalize_treats_blank_or_metadata_only_as_no_version(string? input)
    {
        Assert.Null(AppVersion.Normalize(input));
    }

    [Fact]
    public void Of_an_assembly_never_returns_blank()
    {
        var version = AppVersion.Of(typeof(AppVersion).Assembly);

        Assert.False(string.IsNullOrWhiteSpace(version));
    }
}

/// <summary>
/// The version in the header is itself the "check for updates" control, so its
/// label, accessible name, and status colour are the visible contract of that
/// interaction. They are pure, so they are asserted here rather than through a head.
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
    public void The_accessible_name_carries_both_the_version_and_the_action()
    {
        var label = AppUpdatePresentation.VersionActionLabel("1.2.3", isChecking: false);

        Assert.Contains("1.2.3", label);
        Assert.Contains("Check for updates", label);
    }

    [Fact]
    public void The_accessible_name_reports_a_check_in_progress()
    {
        var label = AppUpdatePresentation.VersionActionLabel("1.2.3", isChecking: true);

        Assert.Contains("Checking for updates", label);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_version_still_produces_an_accessible_name(string? version)
    {
        var label = AppUpdatePresentation.VersionActionLabel(version, isChecking: false);

        Assert.Contains("unknown", label);
        Assert.Contains("Check for updates", label);
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
