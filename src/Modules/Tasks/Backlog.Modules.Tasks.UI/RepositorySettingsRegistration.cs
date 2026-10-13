using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// Puts the Repositories page on the settings screen, on the precedent of
/// <c>AddTaskConnectorSettings</c>: whether a host's settings screen carries it is
/// the host's decision, and the shell knows the page only as the
/// <see cref="SettingsSection"/> this adds.
/// <para>
/// Offered with no feature key, as the shell offered it: the registry it edits is
/// what every repository control in the app reads, so it is never switched off. The
/// Devbook rows on each repository's card are not this page's to draw; the shell
/// adds them as a <see cref="RepositorySettingsPart"/>.
/// </para>
/// <para>
/// It also registers the <see cref="GitHubConnectionState"/> the page shares with
/// the shell's Accounts and AI pages, so the connection is asked for once per
/// screen and a token stored on a card is what those pages read too.
/// </para>
/// </summary>
public static class RepositorySettingsRegistration
{
    /// <summary>The Repositories page's place among the sections modules register:
    /// first of all, straight after the shell's Accounts page, where the tab sat
    /// while the shell drew it — and so ahead of Devices.</summary>
    public const int Order = 5;

    public static IServiceCollection AddRepositorySettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new SettingsSection("repositories", "Repositories", Order, typeof(RepositoriesSettings)));

        // Scoped, so it lives as long as the window's renderer rather than
        // outliving every settings screen as a singleton would.
        services.TryAddScoped<GitHubConnectionState>();

        return services;
    }
}
