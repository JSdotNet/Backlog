using Backlog.Desktop.UI.Shell;
using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Dashboard.UI.Extensions;
using Backlog.Modules.Sessions.UI.Extensions;
using Backlog.Modules.Sync.UI;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Repositories page is the Tasks module's section on the settings screen, on
/// the precedent of the Devices and Connectors pages: the shell knows it only as the
/// <see cref="SettingsSection"/> <c>AddRepositorySettings</c> adds. The Devbook
/// rows inside each repository card stay the shell's, contributed to the card as a
/// <see cref="RepositorySettingsPart"/>.
/// </summary>
public sealed class RepositorySettingsRegistrationTests
{
    /// <summary>The shell's own pages, which win a collision.</summary>
    private static readonly string[] ShellPageIds = ["features", "ai", "storage", "accounts"];

    [Fact]
    public void The_repositories_page_is_registered_as_one_settings_section_offered_with_no_feature_key()
    {
        var services = new ServiceCollection().AddRepositorySettings();

        using var provider = services.BuildServiceProvider();
        var section = Assert.Single(provider.GetServices<SettingsSection>());
        Assert.Equal("repositories", section.Id);
        Assert.Equal("Repositories", section.Title);
        Assert.Equal(typeof(RepositoriesSettings), section.Component);
        Assert.Null(section.FeatureKey);
        Assert.DoesNotContain(section.Id, ShellPageIds);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(SettingsSection));
    }

    /// <summary>The page shares how this machine reaches GitHub with the shell's
    /// Accounts and AI pages, so the holder comes with it - scoped, one per window,
    /// and added once however often the page's registration runs.</summary>
    [Fact]
    public void The_repositories_page_brings_the_github_connection_state_it_shares_with_the_shell()
    {
        var services = new ServiceCollection().AddRepositorySettings().AddRepositorySettings();

        var holder = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GitHubConnectionState));
        Assert.Equal(ServiceLifetime.Scoped, holder.Lifetime);
    }

    /// <summary>The tab sat straight after Accounts while the shell drew it, so it
    /// stays there: ahead of every other module's section, Devices first among
    /// them.</summary>
    [Fact]
    public void The_repositories_page_sorts_ahead_of_every_other_module_section()
    {
        Assert.True(RepositorySettingsRegistration.Order < SyncSettingsRegistration.Order);
        Assert.True(RepositorySettingsRegistration.Order < TaskConnectorSettingsRegistration.Order);
        Assert.True(RepositorySettingsRegistration.Order < InboxSettingsRegistration.Order);
        Assert.True(RepositorySettingsRegistration.Order < SessionsSettingsRegistration.Order);
        Assert.True(RepositorySettingsRegistration.Order < DashboardSettingsRegistration.Order);
    }

    [Fact]
    public void The_shell_contributes_the_devbook_rows_to_every_repository_card()
    {
        var services = new ServiceCollection().AddRepositoryDevbookSettings();

        using var provider = services.BuildServiceProvider();
        var part = Assert.Single(provider.GetServices<RepositorySettingsPart>());
        Assert.Equal(typeof(RepositoryDevbookSettings), part.Component);
        Assert.Empty(provider.GetServices<SettingsSection>());
    }
}
