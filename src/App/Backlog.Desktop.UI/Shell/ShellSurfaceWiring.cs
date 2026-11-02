using Backlog.Desktop.UI.PullRequests;
using Backlog.Desktop.UI.Tasks;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Dashboard.UI;
using Backlog.Modules.Inbox.UI;
using Backlog.Modules.Sessions.UI;
using Backlog.SharedKernel;
using Backlog.UI.Components.Feedback;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// What the takeover surfaces read from the window they are shown in, answered by the
/// host from that window's own state (local ADR 0043 §1, §3, §5): the repository scope,
/// the hues, the entries a session or a pull request names, a pull request's chip, and
/// the Inbox's Capture.
/// <para>
/// One set per window, with the window's view state, built from the window's
/// <see cref="TasksDesktopState"/> and navigator — so a surface draws the same in the
/// main window and in a window of its own, and its jumps land in the window they were
/// pressed in. What only the main window's shell holds — the Dashboard's Ask AI — the
/// shell adds to the window's wiring itself.
/// </para>
/// </summary>
public static class ShellSurfaceWiring
{
    public static IServiceCollection AddShellSurfaceWiring(this IServiceCollection services, ServiceLifetime windowStateLifetime)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Add(new ServiceDescriptor(
            typeof(ShellTaskReferences),
            sp => new ShellTaskReferences(
                sp.GetRequiredService<TasksDesktopState>(),
                sp.GetRequiredService<ISurfaceNavigator>(),
                sp.GetService<IToastChannel>()),
            windowStateLifetime));

        services.Add(new ServiceDescriptor(
            typeof(SessionsSurfaceWiring),
            sp =>
            {
                var state = sp.GetRequiredService<TasksDesktopState>();
                var references = sp.GetRequiredService<ShellTaskReferences>();
                return new SessionsSurfaceWiring
                {
                    RepositoryScope = () => state.SelectedRepositoryAliases,
                    RepositoryAlias = state.RepositoryAliasFor,
                    RepositoryColour = state.RepositoryColourFor,
                    OpenTask = references.OpenTaskAsync,
                    SessionTask = references.SessionTaskFor,
                    // How a pull request a session leads to stands, as its readiness
                    // verdict: read from GitHub by number and judged the way the pull
                    // requests list judges it, so one pull request wears one chip on
                    // both screens. A pull request it finds merged or closed closes the
                    // notifications about it.
                    PullRequestStatus = pull => SessionPullRequestStatuses.ReadAsync(
                        sp.GetService<GitHubIntegration>(),
                        pull,
                        sp.GetService<PullRequestSubjects>()),
                };
            },
            windowStateLifetime));

        services.Add(new ServiceDescriptor(
            typeof(DashboardSurfaceWiring),
            sp =>
            {
                var state = sp.GetRequiredService<TasksDesktopState>();
                return new DashboardSurfaceWiring
                {
                    RepositoryAliases = () => state.SelectedRepositoryAliases,
                    RepositoryColour = state.RepositoryColourFor,
                };
            },
            windowStateLifetime));

        services.Add(new ServiceDescriptor(
            typeof(ShellInboxCapture),
            sp => new ShellInboxCapture(
                sp.GetRequiredService<ICaptureRunner>(),
                sp.GetRequiredService<InboxDesktopState>(),
                sp.GetService<IToastChannel>()),
            windowStateLifetime));
        services.Add(new ServiceDescriptor(
            typeof(InboxSurfaceWiring),
            sp => sp.GetRequiredService<ShellInboxCapture>().Wiring(),
            windowStateLifetime));

        return services;
    }
}
