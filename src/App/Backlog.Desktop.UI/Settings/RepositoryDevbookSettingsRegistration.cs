using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// Puts the Devbook block on every repository's card on the Repositories settings
/// page. The page is the Tasks module's (<c>AddRepositorySettings</c>) and may not
/// reach Devbook, so the shell — which may — contributes the block as the
/// <see cref="RepositorySettingsPart"/> this adds, and the card knows it only as
/// that.
/// </summary>
/// <remarks>
/// The namespace is the shell's rather than a Settings folder's, for the reason
/// the note at the top of <c>Settings.razor</c> gives.
/// </remarks>
public static class RepositoryDevbookSettingsRegistration
{
    public static IServiceCollection AddRepositoryDevbookSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new RepositorySettingsPart("devbook", 0, typeof(RepositoryDevbookSettings)));

        return services;
    }
}
