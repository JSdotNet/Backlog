using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.Features.CloseSprint;
using Backlog.Modules.Progress.Features.ConfigureSeries;
using Backlog.Modules.Progress.Features.CreateSeries;
using Backlog.Modules.Progress.Features.FillSprint;
using Backlog.Modules.Progress.Features.GetSeries;
using Backlog.Modules.Progress.Features.MarkSprintItem;
using Backlog.Modules.Progress.Features.ReadSprintProgress;
using Backlog.Modules.Progress.Features.StartSprint;
using Backlog.Modules.Progress.Services;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Modules.Progress.Extensions;

/// <summary>
/// The module's composition root. A host calls this once and gets every use case the
/// Progress context offers through <see cref="ISprintSeriesPlanning"/> and
/// <see cref="ISprintProgress"/>; it never registers a handler itself, and never sees a
/// series.
/// <para>
/// <see cref="ISprintSeriesRepository"/> and <see cref="ISprintTaskSource"/> are
/// deliberately not registered here: which adapters answer them is the host's
/// decision. <see cref="ISprintPullRequestReader"/> falls back to
/// <see cref="UnreadPullRequests"/> when the host registered none.
/// </para>
/// <para>
/// Composing the module draws nothing and offers nothing. The <c>progress</c> switch
/// decides at every call whether the ports answer, so turning it on or off needs no
/// restart, and while it is off no screen of Progress is drawn.
/// </para>
/// </summary>
public static class ProgressModuleRegistration
{
    public static IServiceCollection AddProgressModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ProgressGate>();
        services.TryAddSingleton<SeriesSaved>();
        services.TryAddSingleton<ISprintPullRequestReader, UnreadPullRequests>();

        services.AddScoped<SeriesChanges>();
        services.AddScoped<GetSeriesQueryHandler>();
        services.AddScoped<CreateSeriesCommandHandler>();
        services.AddScoped<ConfigureSeriesCommandHandler>();
        services.AddScoped<StartSprintCommandHandler>();
        services.AddScoped<FillSprintCommandHandler>();
        services.AddScoped<MarkSprintItemCommandHandler>();
        services.AddScoped<CloseSprintCommandHandler>();
        services.AddScoped<ReadSprintProgressQueryHandler>();

        // One instance answers both ports, each asking the progress switch at every call.
        services.AddScoped(sp => new SprintSeriesPlanning(
            sp.GetRequiredService<GetSeriesQueryHandler>(),
            sp.GetRequiredService<CreateSeriesCommandHandler>(),
            sp.GetRequiredService<ConfigureSeriesCommandHandler>(),
            sp.GetRequiredService<StartSprintCommandHandler>(),
            sp.GetRequiredService<FillSprintCommandHandler>(),
            sp.GetRequiredService<MarkSprintItemCommandHandler>(),
            sp.GetRequiredService<CloseSprintCommandHandler>(),
            sp.GetRequiredService<ReadSprintProgressQueryHandler>(),
            sp.GetService<IAppFeatureSettings>(),
            sp.GetRequiredService<SeriesSaved>()));
        services.AddScoped<ISprintSeriesPlanning>(sp => sp.GetRequiredService<SprintSeriesPlanning>());
        services.AddScoped<ISprintProgress>(sp => sp.GetRequiredService<SprintSeriesPlanning>());

        // Each series as a document that travels between a person's devices, the way
        // Roadmap Planning's plan does (local ADR 0018). A singleton, because it holds
        // the series' save notice for a sync loop to hear. The store is the host's: a
        // head that registers none keeps no series, so a copy reads as unreadable
        // rather than failing the provider. Not behind the progress switch: series
        // recorded while it is off are kept, so they travel too.
        services.TryAddSingleton<ISprintSeriesReplication>(sp => new SprintSeriesReplication(
            sp.GetService<ISprintSeriesReplicaStore>(),
            sp.GetRequiredService<SeriesSaved>(),
            sp.GetRequiredService<ProgressGate>(),
            sp.GetService<ISprintSeriesRepository>()));

        return services;
    }
}
