using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.UI;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Devices page is the Sync module's section on the settings screen, on the
/// precedent of the Inbox's and the Dashboard's: the shell knows it only as the
/// <see cref="SettingsSection"/> <c>AddSyncSettings</c> adds.
/// </summary>
public sealed class SyncSettingsRegistrationTests
{
    /// <summary>The shell's own pages, which win a collision.</summary>
    private static readonly string[] ShellPageIds = ["features", "ai", "storage", "accounts", "repositories"];

    [Fact]
    public void The_devices_page_is_registered_as_one_settings_section_behind_the_sync_switch()
    {
        var services = new ServiceCollection().AddSyncSettings();

        using var provider = services.BuildServiceProvider();
        var section = Assert.Single(provider.GetServices<SettingsSection>());
        Assert.Equal("devices", section.Id);
        Assert.Equal("Devices", section.Title);
        Assert.Equal(typeof(DevicesSettings), section.Component);
        Assert.Equal(SyncFeatures.Sync, section.FeatureKey);
        Assert.DoesNotContain(section.Id, ShellPageIds);
        Assert.Single(services);

        // The tab sat straight after Repositories while the shell drew it, so it
        // stays there: ahead of every other module's section.
        Assert.True(section.Order < TaskConnectorSettingsRegistration.Order);
    }
}
