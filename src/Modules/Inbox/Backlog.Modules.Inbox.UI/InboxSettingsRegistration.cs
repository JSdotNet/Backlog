using Backlog.Modules.Inbox.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// Puts the Inbox's page on the settings screen.
/// </summary>
/// <remarks>
/// <para>
/// An extension method on the precedent of <see cref="InboxAiContentRegistration"/>:
/// whether a host's settings screen carries the Inbox is the host's decision, and
/// the shell knows the page only as the <see cref="SettingsSection"/> this adds.
/// </para>
/// <para>
/// Offered behind <see cref="InboxFeatures.Pane"/>, the switch the rules on it are
/// read under, so the page is not on the strip while nothing would read them.
/// </para>
/// </remarks>
public static class InboxSettingsRegistration
{
    /// <summary>The Inbox page's place among the sections modules register.</summary>
    public const int Order = 100;

    public static IServiceCollection AddInboxSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new SettingsSection("inbox", "Inbox", Order, typeof(InboxSettings), InboxFeatures.Pane));

        return services;
    }
}
