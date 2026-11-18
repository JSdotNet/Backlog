using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Sessions.UI;

/// <summary>
/// Puts the Sessions context's session list in the shell as a takeover-group
/// <see cref="ShellSurface"/> (local ADR 0043 §1), offered behind
/// <see cref="SessionFeatures.Sessions"/>, the flag its header option has always gated
/// on. It accepts <see cref="SessionKey"/>, the session to open on (§6). On the
/// precedent of <c>AddDevbookSurface</c>; safe to call twice.
/// <para>
/// The open pull requests beside it are Repository Management's screen (local ADR
/// 0042), drawn by the host, beside the sessions that opened them; its id stays named
/// here, beside the session list's, where the header has always found it.
/// </para>
/// </summary>
public static class SessionsSurfaceRegistration
{
    /// <summary>The session list's id: what a route, a tool and a link name.</summary>
    public const string SurfaceId = "sessions";

    /// <summary>The pull requests list's id: Repository Management's screen (local ADR
    /// 0042), offered beside the session list.</summary>
    public const string PullRequestsSurfaceId = "pull-requests";

    /// <summary>The query key naming the session the list opens on.</summary>
    public const string SessionKey = "session";

    /// <summary>The query key naming the page inside the takeover the surface opens on:
    /// <see cref="SchedulesPage"/>, or the list when it is absent.</summary>
    public const string PageKey = "page";

    /// <summary>The <see cref="PageKey"/> value that opens the Schedules page (local ADR 0051
    /// §6) — what a schedule alert's Open Schedules links to.</summary>
    public const string SchedulesPage = "schedules";

    /// <summary>The query key naming the schedule the Schedules page opens on, picked in its
    /// detail.</summary>
    public const string ScheduleKey = "schedule";

    /// <summary>The session list's place among the takeovers: after the Inbox and the
    /// Devbook, after Goals in the header's Goals group.</summary>
    public const int Order = 30;

    public static IServiceCollection AddSessionsSurface(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShellSurface)
                && descriptor.ImplementationInstance is ShellSurface { Id: SurfaceId }))
        {
            return services;
        }

        services.AddSingleton(new ShellSurface(
            SurfaceId,
            "Sessions",
            ShellSurfaceGroup.Takeover,
            Order,
            typeof(SessionsSurface),
            SessionFeatures.Sessions,
            [SessionKey, PageKey, ScheduleKey]));

        return services;
    }
}
