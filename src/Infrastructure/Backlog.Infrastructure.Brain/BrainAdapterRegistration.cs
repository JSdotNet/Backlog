using Backlog.Infrastructure.Devbook;
using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Brain.Abstractions;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Roadmap.Abstractions.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Backlog.Infrastructure.Brain;

/// <summary>
/// The adapters behind the Brain's ports (local ADR 0040 §1), registered in one
/// place so every host that composes the Brain composes the same thing with the
/// same lifetimes.
/// </summary>
public static class BrainAdapterRegistration
{
    /// <summary>
    /// Registers the Brain's adapters: its settings file, its folder source under
    /// the Brain's port and Devbook's personal-source port, the note writer and the
    /// link writer, the backlink and task-link readers, the folder watch, Show hidden, and the
    /// Inbox's To Brain target. Call after the
    /// <see cref="DevbookDatabaseRefresher"/> is registered, which the watch
    /// resolves; each head starts the watch after <c>Build()</c>.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="settings">Where this host keeps the Brain setting.</param>
    public static IServiceCollection AddBrainAdapters(this IServiceCollection services, Func<IServiceProvider, BrainSettingsStore> settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);

        // The Brain folder (local ADR 0026): where it is, kept per user and never in
        // the folder, and the file-system work of setting one up that the Settings
        // card does. The folder picker behind its Browse is the desktop head's
        // alone; a host that registers none types the folder instead.
        services.AddSingleton(settings);
        services.AddSingleton<IBrainSettings>(sp => sp.GetRequiredService<BrainSettingsStore>());
        services.AddSingleton(_ => new BrainFolderSetup());

        // Show hidden (local ADR 0026 §2): whether the Brain's ignored folders and
        // dot-folders show as tabs, kept per device in the shell's navigation file —
        // never in the Brain folder, never on the Brain setting whose change reloads
        // the Brain.
        services.AddSingleton<IBrainShowHidden>(sp =>
            new BrainShowHidden(sp.GetService<ShellNavigationStore>()));

        // The Brain's folder, asked of the person's setting on every resolve, under
        // a port of its own — registering it as a second IDevbookFolderSource would
        // replace the repository source every search, reference and annotation
        // reader resolves. Devbook reads the same folder as its personal source
        // (local ADR 0040 §2): beside the repository source, never as one.
        services.AddSingleton(sp =>
        {
            var brainSettings = sp.GetRequiredService<IBrainSettings>();
            return new BrainFolderSource(() => brainSettings.BrainFolder);
        });
        services.AddSingleton<IBrainFolderSource>(sp => sp.GetRequiredService<BrainFolderSource>());
        services.AddSingleton<IDevbookPersonalSource>(sp => sp.GetRequiredService<BrainFolderSource>());

        // The one writer of a new note into the Brain's inbox area: the pane's
        // Capture, triage's To Brain and the capture tool all go through it.
        services.AddSingleton<IBrainNoteWriter>(sp => new BrainNoteWriter(sp.GetRequiredService<BrainFolderSource>()));

        // The second writer (local ADR 0052): a link appended to an existing note's
        // tasks or roadmap list, and a finished project note moved to the archive.
        services.AddSingleton<IBrainNoteLinkWriter>(sp => new BrainNoteLinkWriter(sp.GetRequiredService<BrainFolderSource>()));

        // The Brain notes that link to a repository chapter, read in reverse from
        // the Brain's own database (local ADR 0027 §6) — so the repository's
        // devbook shows its owner "From your Brain" and nothing is written there.
        services.AddSingleton<IBrainBacklinks>(sp => new BrainBacklinkReader(sp.GetRequiredService<IBrainFolderSource>()));
        services.AddSingleton<IBrainTaskLinks>(sp => new BrainTaskLinkReader(sp.GetRequiredService<IBrainFolderSource>()));

        // The notes a roadmap item gathers (local ADR 0053), from the same database.
        // Replace rather than TryAdd, as for To Brain below: the roadmap's
        // cross-context registration leaves an empty answer for a host without a
        // Brain, and whichever of the two runs first, this one wins. A singleton, as
        // that one is: it holds no scoped service.
        services.Replace(ServiceDescriptor.Singleton<IRoadmapBrainNotes>(sp =>
            new BrainRoadmapNoteReader(sp.GetRequiredService<IBrainFolderSource>())));

        // The watch ties the setting to the refresher; each head starts it after Build().
        services.AddSingleton(sp => new BrainFolderWatch(
            sp.GetRequiredService<IBrainSettings>(),
            sp.GetRequiredService<BrainFolderSource>(),
            sp.GetRequiredService<DevbookDatabaseRefresher>(),
            sp.GetService<ILogger<BrainFolderWatch>>()));

        // To Brain, over the note writer above. Replace rather than TryAdd: the
        // Inbox's cross-context registration leaves a refusing target for a host
        // without a Brain, and whichever of the two runs first, this one wins.
        services.Replace(ServiceDescriptor.Scoped<IInboxBrainTarget>(sp =>
            new InboxBrainTarget(sp.GetRequiredService<IBrainNoteWriter>(), sp.GetService<IDevbookPersonalSource>(), sp.GetService<IBrainFolderSource>())));

        return services;
    }
}
