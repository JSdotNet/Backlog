using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel;
using Backlog.UI.Components.Diagrams;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.Devbook.Extensions;

/// <summary>
/// Wires the Devbook context's own services: the area providers and stores the
/// panels read, the menu, scope and source selection the pane is built on, the
/// chapter writer, the instruction-source discovery, and the two consumers of the
/// Copilot launcher.
/// </summary>
/// <remarks>
/// <para>
/// Both desktop heads used to register these line by line, and the two copies had
/// drifted (issue #738). What genuinely differs between the heads is a parameter
/// here, never a branch: which Copilot launcher the host may start, and how long
/// one reader's domain store lives.
/// </para>
/// <para>
/// The adapters behind the module's ports are the host's to compose, not this
/// project's: a host registers <see cref="IDevbookFolderSource"/>,
/// <see cref="GitHubSettingsStore"/>, <see cref="IAppFeatureSettings"/> and an
/// <see cref="IFolderEditorLauncher"/> before resolving anything registered here.
/// <c>AddDesktopComposition</c> does that for both desktop heads and calls this.
/// </para>
/// </remarks>
public static class DevbookModuleRegistration
{
    /// <param name="services">The container to register into.</param>
    /// <param name="copilotCliLauncher">The launcher the Devbook's "open in Copilot
    /// CLI" offers and the Archify artifact generator start through. A head on the
    /// person's machine starts the CLI; a host that may not start a process passes
    /// one that says so when pressed.</param>
    /// <param name="domainStoreLifetime">How long one <see cref="DomainDevbookStore"/>
    /// lives: the window's lifetime. A singleton in a head with one window; scoped in
    /// a host with a circuit per visitor, so two readers never share one store's
    /// state.</param>
    public static IServiceCollection AddDevbookModule(
        this IServiceCollection services,
        Func<IServiceProvider, ICopilotCliLauncher> copilotCliLauncher,
        ServiceLifetime domainStoreLifetime)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(copilotCliLauncher);

        services.AddSingleton<DesignDevbookProvider>();
        services.AddSingleton<AiDevbookProvider>();
        services.AddSingleton<TechnologyDevbookService>();
        services.AddSingleton<DevbookAtlasService>();
        services.AddSingleton<InstructionSourceDiscovery>();
        services.AddSingleton<DevbookMenu>();
        services.AddSingleton(sp => new DevbookCopilotCli(copilotCliLauncher(sp)));
        // The shared diagram component asks for this optionally, so registering it
        // is what switches Archify artifacts on for the app at all. Everything it
        // answers — the flag, which clone the chapters came from, whether a CLI can
        // be started — is the host's to know, which is why the library only asks.
        services.AddSingleton<IDiagramArtifactSource>(sp => new ArchifyDiagramArtifacts(
            sp.GetRequiredService<IAppFeatureSettings>(),
            sp.GetRequiredService<IDevbookFolderSource>(),
            sp.GetRequiredService<GitHubSettingsStore>(),
            copilotCliLauncher(sp)));
        services.AddSingleton<DevbookScope>();
        services.AddSingleton<DevbookUpdateService>();

        // Shared by the Devbook pane and the settings screen, and a singleton so
        // the branch list somebody fetched in one is already there in the other.
        services.AddSingleton<DevbookSourceSelection>();
        services.AddSingleton<DevbookFolderOpenService>();
        services.AddSingleton<Arc42DevbookStore>();
        // The C4 model beside the architecture chapters. Registered next to the
        // arc42 store because it answers the same scope question against the same
        // clone; it reads its own feature key and hands back nothing when that key
        // is off, so registering it does not turn it on.
        services.AddSingleton<C4DevbookStore>();
        services.AddSingleton<DevbookChapterWriter>();
        // The scenario pages and their runs: what the menu marks, what a scenario
        // page shows above its body, and what a `scenario:` image resolves to. One
        // per app, since it only caches committed files and drops them when the
        // folder source says they moved.
        services.AddSingleton<DevbookScenarioStore>();
        services.Add(new ServiceDescriptor(
            typeof(DomainDevbookStore),
            sp => new DomainDevbookStore(sp.GetRequiredService<IDevbookFolderSource>()),
            domainStoreLifetime));

        return services;
    }
}
