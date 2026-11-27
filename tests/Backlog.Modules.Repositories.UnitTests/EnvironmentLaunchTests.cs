using Backlog.Modules.Repositories.Abstractions.Environments;

namespace Backlog.Modules.Repositories.UnitTests;

/// <summary>
/// The Environment Launch service of <c>domain/repositories</c>: a launch link turned into
/// the action a screen offers, composed with the repository's clone directory on this
/// device.
/// </summary>
public sealed class EnvironmentLaunchTests
{
    private static readonly string Clone = Path.Combine(Path.GetTempPath(), "clones", "app");

    [Fact]
    public void A_url_opens_in_the_browser()
    {
        var action = EnvironmentLaunch.Resolve(new LaunchLink(LaunchLinkKind.Url, "https://staging.example.com/"), Clone);

        Assert.Equal(LaunchActionKind.OpenUri, action.Kind);
        Assert.Equal("https://staging.example.com/", action.Target);
    }

    [Fact]
    public void A_command_runs_in_the_clone_directory_when_the_repository_has_one()
    {
        var withClone = EnvironmentLaunch.Resolve(new LaunchLink(LaunchLinkKind.Command, "aspire start"), Clone);
        var without = EnvironmentLaunch.Resolve(new LaunchLink(LaunchLinkKind.Command, "aspire start"), null);

        Assert.Equal(new LaunchAction(LaunchActionKind.RunCommand, "aspire start", Clone), withClone);
        Assert.Equal(new LaunchAction(LaunchActionKind.RunCommand, "aspire start"), without);
    }

    [Fact]
    public void A_workspace_path_is_resolved_against_the_clone_directory()
    {
        var action = EnvironmentLaunch.Resolve(new LaunchLink(LaunchLinkKind.WorkspacePath, Path.Combine("src", "web")), Clone);

        Assert.Equal(LaunchActionKind.OpenWorkspace, action.Kind);
        Assert.Equal(Path.Combine(Clone, "src", "web"), action.Target);
    }

    [Fact]
    public void A_relative_workspace_path_offers_no_launch_without_a_clone_on_this_device()
    {
        var action = EnvironmentLaunch.Resolve(new LaunchLink(LaunchLinkKind.WorkspacePath, "src"), null);

        Assert.Equal(LaunchActionKind.Unavailable, action.Kind);
        Assert.Equal(EnvironmentLaunch.NoCloneForWorkspace, action.Reason);
    }

    [Fact]
    public void An_absolute_workspace_path_opens_without_a_clone()
    {
        var folder = Path.Combine(Path.GetTempPath(), "workspace");

        var action = EnvironmentLaunch.Resolve(new LaunchLink(LaunchLinkKind.WorkspacePath, folder), null);

        Assert.Equal(new LaunchAction(LaunchActionKind.OpenWorkspace, folder), action);
    }

    [Fact]
    public void An_azure_resource_id_opens_its_portal_blade()
    {
        const string id = "/subscriptions/0000/resourceGroups/rg-app";

        var action = EnvironmentLaunch.Resolve(new LaunchLink(LaunchLinkKind.ResourceId, id), null);

        Assert.Equal(new LaunchAction(LaunchActionKind.OpenUri, EnvironmentLaunch.AzurePortalResourceBase + id), action);
    }

    [Fact]
    public void A_resource_id_Backlog_cannot_place_offers_no_launch()
    {
        var action = EnvironmentLaunch.Resolve(new LaunchLink(LaunchLinkKind.ResourceId, "arn:aws:s3:::bucket"), null);

        Assert.Equal(LaunchActionKind.Unavailable, action.Kind);
        Assert.Equal(EnvironmentLaunch.UnknownResourceId, action.Reason);
    }
}
