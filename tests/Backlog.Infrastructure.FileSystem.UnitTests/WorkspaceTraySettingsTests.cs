namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The tray's half of the workspace settings file (local ADR 0043 §5): whether
/// closing the main window keeps the app running in the notification area, and
/// whether the one-time toast saying so has been shown. Every store is built on the
/// test-seam constructor, so nothing here touches <c>%LOCALAPPDATA%</c>.
/// </summary>
public sealed class WorkspaceTraySettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "workspace-tray-settings-tests-" + Guid.NewGuid().ToString("N"));

    public WorkspaceTraySettingsTests() => Directory.CreateDirectory(_root);

    private string SettingsFile => Path.Combine(_root, "settings.json");

    private WorkspaceSettingsStore Store() => new(_root, SettingsFile, _ => null);

    [Fact]
    public void An_untouched_workspace_keeps_running_in_the_tray_and_has_shown_no_hint()
    {
        var store = Store();

        Assert.True(store.KeepRunningInTray);
        Assert.False(store.TrayHintShown);
    }

    [Fact]
    public void Turning_keep_running_off_survives_a_restart_and_raises_the_change()
    {
        var store = Store();
        var raised = 0;
        store.TrayChanged += () => raised++;

        Assert.Null(store.SetKeepRunningInTray(false));

        Assert.Equal(1, raised);
        Assert.False(store.KeepRunningInTray);
        Assert.False(Store().KeepRunningInTray);
    }

    [Fact]
    public void Turning_it_back_on_survives_a_restart()
    {
        Assert.Null(Store().SetKeepRunningInTray(false));

        Assert.Null(Store().SetKeepRunningInTray(true));

        Assert.True(Store().KeepRunningInTray);
    }

    [Fact]
    public void Setting_the_value_it_already_holds_raises_nothing()
    {
        var store = Store();
        var raised = 0;
        store.TrayChanged += () => raised++;

        Assert.Null(store.SetKeepRunningInTray(true));

        Assert.Equal(0, raised);
        Assert.False(File.Exists(SettingsFile));
    }

    [Fact]
    public void The_hint_once_shown_stays_shown_after_a_restart()
    {
        var store = Store();
        var raised = 0;
        store.TrayChanged += () => raised++;

        Assert.Null(store.MarkTrayHintShown());
        Assert.Null(store.MarkTrayHintShown());

        Assert.Equal(1, raised);
        Assert.True(Store().TrayHintShown);
        Assert.True(Store().KeepRunningInTray);
    }

    [Fact]
    public void The_tray_row_leaves_the_other_settings_as_they_were()
    {
        var store = Store();
        Assert.Null(store.SetMcpServerPort(6060));

        Assert.Null(store.SetKeepRunningInTray(false));
        Assert.Null(store.MarkTrayHintShown());

        var reopened = Store();
        Assert.Equal(6060, reopened.McpServerPort);
        Assert.False(reopened.KeepRunningInTray);
        Assert.True(reopened.TrayHintShown);
    }

    [Fact]
    public void A_workspace_at_the_defaults_writes_no_tray_row()
    {
        Assert.Null(Store().SetMcpServerPort(6060));

        using var written = System.Text.Json.JsonDocument.Parse(File.ReadAllText(SettingsFile));
        Assert.False(written.RootElement.TryGetProperty("tray", out _));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
