using Backlog.UI.Components.AppUpdate;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The phone's version control and its update window, through the whole shell: the
/// app bar names the running version and opens the window, a check offers Install
/// only when it finds something newer, and the app never installs on its own — a
/// quiet check on launch only marks the control.
/// </summary>
public sealed class AppUpdateSheetTests
{
    [Fact]
    public void The_app_bar_names_the_running_version_and_opens_the_update_window()
    {
        using var host = ShellHost.Paired(updates: new ScriptedAppUpdateService { CurrentVersion = "0.1.59" });
        var app = host.Open();

        var control = app.Find("[data-testid='app-version']");
        Assert.Equal("0.1.59", app.Find("[data-testid='app-version-number']").TextContent);
        Assert.Contains("Version 0.1.59. Open update window.", control.GetAttribute("aria-label"));
        Assert.Equal("false", app.Find("[data-testid='app-update-sheet']").GetAttribute("data-open"));

        control.Click();

        app.WaitForAssertion(() => Assert.Equal("true", app.Find("[data-testid='app-update-sheet']").GetAttribute("data-open")));
        Assert.Equal("0.1.59", app.Find("[data-testid='app-update-current']").TextContent);
        Assert.Empty(app.FindAll("[data-testid='install-update']"));
    }

    [Fact]
    public void A_check_that_finds_a_newer_version_offers_to_install_it()
    {
        var updates = new ScriptedAppUpdateService
        {
            CheckAnswer = AppUpdateCheckResult.UpToDate(),
            InstallAnswer = AppUpdateInstallResult.InProgress("Confirm the update in the Android installer.")
        };
        using var host = ShellHost.Paired(updates: updates);
        var app = host.Open();

        app.Find("[data-testid='app-version']").Click();
        updates.CheckAnswer = AppUpdateCheckResult.Available(availableVersion: "0.1.60");
        app.Find("[data-testid='check-for-updates']").Click();

        app.WaitForAssertion(() => Assert.Equal("Version 0.1.60 is available.", app.Find("[data-testid='app-update-status']").TextContent.Trim()));
        Assert.Equal(0, updates.Installs);

        app.Find("[data-testid='install-update']").Click();

        app.WaitForAssertion(() => Assert.Equal(
            "Confirm the update in the Android installer.",
            app.Find("[data-testid='app-update-status']").TextContent.Trim()));
        Assert.Equal(1, updates.Installs);
    }

    [Fact]
    public void Up_to_date_offers_nothing_to_install()
    {
        var updates = new ScriptedAppUpdateService { CheckAnswer = AppUpdateCheckResult.UpToDate() };
        using var host = ShellHost.Paired(updates: updates);
        var app = host.Open();

        app.Find("[data-testid='app-version']").Click();
        app.Find("[data-testid='check-for-updates']").Click();

        app.WaitForAssertion(() => Assert.Equal("You are on the latest version.", app.Find("[data-testid='app-update-status']").TextContent.Trim()));
        Assert.Empty(app.FindAll("[data-testid='install-update']"));
    }

    [Fact]
    public void A_host_that_cannot_update_itself_says_so_and_never_checks_on_its_own()
    {
        // No service handed in: the shell's own default, which the browser harness gets too.
        using var host = ShellHost.Paired();
        var app = host.Open();

        Assert.False(host.Service<IAppUpdateService>().IsSupported);

        app.Find("[data-testid='app-version']").Click();
        app.Find("[data-testid='check-for-updates']").Click();

        app.WaitForAssertion(() => Assert.Equal(
            "Updates are managed by however you started this build.",
            app.Find("[data-testid='app-update-status']").TextContent.Trim()));
        Assert.Empty(app.FindAll("[data-testid='app-update-dot']"));
        Assert.Empty(app.FindAll("[data-testid='install-update']"));
    }

    [Fact]
    public void A_quiet_check_on_launch_marks_the_control_and_installs_nothing()
    {
        var updates = new ScriptedAppUpdateService { CheckAnswer = AppUpdateCheckResult.Available(availableVersion: "0.1.60") };
        using var host = ShellHost.Paired(updates: updates);
        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='app-update-dot']")));
        Assert.Equal(1, updates.Checks);
        Assert.Equal(0, updates.Installs);
        Assert.Contains("An update is available.", app.Find("[data-testid='app-version']").GetAttribute("aria-label"));

        // The window opens on what the quiet check found, one tap from installing.
        app.Find("[data-testid='app-version']").Click();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='install-update']")));
        Assert.Equal("Version 0.1.60 is available.", app.Find("[data-testid='app-update-status']").TextContent.Trim());
        Assert.Equal(0, updates.Installs);
    }

    [Fact]
    public void The_window_cannot_be_put_away_while_an_install_is_running()
    {
        var updates = new ScriptedAppUpdateService
        {
            CheckAnswer = AppUpdateCheckResult.Available(availableVersion: "0.1.60"),
            InstallGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            InstallAnswer = AppUpdateInstallResult.Failed("The update was cancelled.")
        };
        using var host = ShellHost.Paired(updates: updates);
        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='app-update-dot']")));
        app.Find("[data-testid='app-version']").Click();
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='install-update']")));
        app.Find("[data-testid='install-update']").Click();

        app.WaitForAssertion(() => Assert.True(app.Find("[data-testid='app-update-sheet-close']").HasAttribute("disabled")));
        Assert.True(app.Find("[data-testid='check-for-updates']").HasAttribute("disabled"));
        Assert.Equal("Installing...", app.Find("[data-testid='install-update']").TextContent.Trim());

        app.Find("[data-testid='app-update-sheet']").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Equal("true", app.Find("[data-testid='app-update-sheet']").GetAttribute("data-open"));

        updates.InstallGate.SetResult();

        app.WaitForAssertion(() => Assert.Equal("The update was cancelled.", app.Find("[data-testid='app-update-status']").TextContent.Trim()));
        Assert.False(app.Find("[data-testid='app-update-sheet-close']").HasAttribute("disabled"));
    }
}
