using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The notification settings' half of the workspace settings file: away after, notify
/// after and dialog wait, which the question relay reads on this desktop (local ADR 0041).
/// Every store is built on the test-seam constructor; nothing here touches
/// <c>%LOCALAPPDATA%</c>.
/// </summary>
public sealed class WorkspaceNotificationSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "workspace-notification-settings-tests-" + Guid.NewGuid().ToString("N"));

    public WorkspaceNotificationSettingsTests() => Directory.CreateDirectory(_root);

    private string SettingsFile => Path.Combine(_root, "settings.json");

    private WorkspaceSettingsStore Store() => new(_root, SettingsFile, _ => null);

    private static readonly PresenceSettings Chosen =
        new(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(4));

    [Fact]
    public void An_untouched_workspace_reads_the_defaults_and_writes_no_row()
    {
        var store = Store();

        Assert.Equal(PresenceSettings.Default, store.Presence);

        // Saving for another reason leaves the file without a notifications row.
        Assert.Null(store.SetMcpServerPort(6060));
        Assert.DoesNotContain("notifications", File.ReadAllText(SettingsFile), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Chosen_settings_survive_a_restart()
    {
        Assert.Null(Store().SetPresence(Chosen));

        Assert.Equal(Chosen, Store().Presence);
    }

    [Fact]
    public void Choosing_settings_announces_the_change_once()
    {
        var store = Store();
        var raised = 0;
        store.PresenceChanged += () => raised++;

        Assert.Null(store.SetPresence(Chosen));
        Assert.Null(store.SetPresence(Chosen));

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Settings_out_of_range_are_refused_and_the_stored_ones_kept()
    {
        var store = Store();

        var error = store.SetPresence(Chosen with { DialogWait = TimeSpan.FromHours(48) });

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(PresenceSettings.Default, store.Presence);
    }

    [Fact]
    public void Going_back_to_the_defaults_removes_the_row()
    {
        Assert.Null(Store().SetPresence(Chosen));
        Assert.Null(Store().SetPresence(PresenceSettings.Default));

        Assert.DoesNotContain("notifications", File.ReadAllText(SettingsFile), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PresenceSettings.Default, Store().Presence);
    }

    [Fact]
    public void A_hand_edited_row_out_of_range_reads_as_the_defaults()
    {
        File.WriteAllText(SettingsFile, """{ "notifications": { "awayAfterMinutes": 0 } }""");

        Assert.Equal(PresenceSettings.Default, Store().Presence);
    }

    [Fact]
    public void A_row_naming_one_setting_keeps_the_defaults_for_the_others()
    {
        File.WriteAllText(SettingsFile, """{ "notifications": { "notifyAfterMinutes": 45 } }""");

        Assert.Equal(PresenceSettings.Default with { NotifyAfter = TimeSpan.FromMinutes(45) }, Store().Presence);
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
