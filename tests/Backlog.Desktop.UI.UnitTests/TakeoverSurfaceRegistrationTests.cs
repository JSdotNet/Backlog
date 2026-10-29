using Backlog.Desktop.UI.Devbook.Extensions;
using Backlog.Desktop.UI.PullRequests;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.UI.Extensions;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.DevPc.UI;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.UI;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.UI;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The takeovers are their modules' surfaces (local ADR 0042 §1): each registers a
/// takeover-group <see cref="ShellSurface"/> behind the flag its header option has
/// always gated on, under the id its stored name now reads as (§2), with the query keys
/// a jump opens it on (§6). Only the Inbox keeps Escape for keys of its own.
/// </summary>
public sealed class TakeoverSurfaceRegistrationTests
{
    public static TheoryData<string, string, string, string[]> Takeovers => new()
    {
        { "inbox", "Inbox", InboxFeatures.Pane, [] },
        { "sessions", "Sessions", SessionFeatures.Sessions, ["session"] },
        { "pull-requests", "Pull requests", SessionFeatures.PullRequests, ["pullRequest"] },
        { "dashboard", "Dashboard", DashboardFeatures.Dashboard, [] },
        { "notifications", "Notifications", NotificationsFeatures.Notifications, ["notification"] },
        { "tools", "Tools", DevPcFeatures.SystemTools, [] },
        { "atlas", "Atlas", DevbookFeatures.LobesAtlas, ["layout"] },
    };

    [Theory]
    [MemberData(nameof(Takeovers))]
    public void Each_takeover_registers_in_the_takeover_group_behind_its_flag(string id, string title, string feature, string[] keys)
    {
        var surface = Assert.Single(Registered(), candidate => candidate.Id == id);

        Assert.Equal(title, surface.Title);
        Assert.Equal(ShellSurfaceGroup.Takeover, surface.Group);
        Assert.Equal(feature, surface.FeatureKey);
        Assert.Equal(keys, surface.QueryKeys ?? []);
        Assert.Equal(id != "inbox", surface.EscapeCloses);
    }

    [Fact]
    public void A_second_registration_adds_nothing()
    {
        var services = new ServiceCollection();
        for (var pass = 0; pass < 2; pass++)
        {
            services.AddInboxSurface();
            services.AddSessionsSurface();
            services.AddPullRequestsSurface();
            services.AddDashboardSurface();
            services.AddNotificationsSurface();
            services.AddToolsSurface();
            services.AddAtlasSurface();
        }

        Assert.Equal(7, services.Count(descriptor => descriptor.ImplementationInstance is ShellSurface));
    }

    [Fact]
    public void The_takeovers_are_ordered_as_the_header_places_them()
    {
        var order = Registered()
            .Where(surface => surface.Group == ShellSurfaceGroup.Takeover)
            .OrderBy(surface => surface.Order)
            .Select(surface => surface.Id);

        Assert.Equal(["inbox", "devbook", "sessions", "pull-requests", "dashboard", "notifications", "tools", "atlas"], order);
    }

    private static List<ShellSurface> Registered() =>
        [.. new ServiceCollection()
            .AddShellSurfaces()
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ShellSurface>()];
}
