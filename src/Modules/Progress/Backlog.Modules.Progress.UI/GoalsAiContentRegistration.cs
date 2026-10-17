using Backlog.SharedKernel.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Progress.UI;

/// <summary>
/// Wires the Goals view's answer to <see cref="IAiContentSource"/>, so Ask AI is offered
/// while the Goals view is on screen.
/// </summary>
/// <remarks>
/// Scoped, because <c>IStandUp</c> is one of the module's scoped services and a singleton
/// holding it would be a captive dependency. A host must call <c>AddProgressModule()</c>
/// before this; the source only holds the port.
/// </remarks>
public static class GoalsAiContentRegistration
{
    public static IServiceCollection AddGoalsAiContentSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAiContentSource, GoalsAiContentSource>();

        return services;
    }
}
