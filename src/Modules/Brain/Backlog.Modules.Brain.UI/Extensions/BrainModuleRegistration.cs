using Backlog.Desktop.UI.Devbook;
using Backlog.Infrastructure.Brain;
using Backlog.Modules.Devbook.Abstractions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Modules.Brain.UI.Extensions;

/// <summary>
/// The Brain module's one registration (guideline ADR 0005): its adapters and the
/// mode it adds to the Devbook pane (local ADR 0040 §2). Called once, from the
/// shared desktop composition; a head adds only what is its own — the folder
/// picker and starting the folder watch.
/// </summary>
public static class BrainModuleRegistration
{
    /// <summary>The Brain's adapters and its mode on the Devbook pane.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="settings">The Brain folder setting's store, which each head
    /// places where it keeps its own settings.</param>
    public static IServiceCollection AddBrainModule(this IServiceCollection services, Func<IServiceProvider, BrainSettingsStore> settings)
    {
        services.AddBrainAdapters(settings);
        return services.AddBrainPane();
    }

    /// <summary>The UI half alone, for a host or test that registers the Brain's
    /// ports itself. The mode resolves the source when it is first asked for, so
    /// it answers through whichever source is registered last.</summary>
    public static IServiceCollection AddBrainPane(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(sp => new BrainAtlas(sp.GetRequiredService<IDevbookFolderSource>()));
        services.TryAddSingleton<IDevbookPaneMode>(sp => new BrainPaneMode(sp, sp.GetRequiredService<IDevbookPersonalSource>()));
        // The Lobes atlas over the Brain and every registered devbook; without it
        // the atlas says there is nothing to map.
        services.TryAddSingleton<IDevbookLobesSource>(sp => new BrainLobesSource(
            sp.GetRequiredService<DevbookAtlasService>(),
            sp.GetRequiredService<BrainAtlas>(),
            sp.GetRequiredService<DevbookScope>(),
            sp));
        return services;
    }
}
