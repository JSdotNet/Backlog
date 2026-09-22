using Backlog.SharedKernel.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// Wires the Roadmap's answer to <see cref="IAiContentSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// Scoped, because <c>IRoadmapPlanning</c> is one of the module's scoped
/// handlers and a singleton holding it would be a captive dependency. A host
/// must call <c>AddRoadmapModule()</c> before this; the source only holds the
/// port.
/// </para>
/// </remarks>
public static class RoadmapAiContentRegistration
{
    public static IServiceCollection AddRoadmapAiContentSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAiContentSource, RoadmapAiContentSource>();

        return services;
    }
}
