using Backlog.SharedKernel.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.DevPc.UI;

/// <summary>
/// Wires Dev PC Management's answer to <see cref="IAiContentSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// A host must register <c>IDevToolService</c> before calling this — the
/// desktop's shells out to the CLIs, the harness's reads the JSON only — and the
/// source only holds whichever one it was given. Scoped for consistency with
/// the other areas' sources, which read per-circuit state; this one holds a
/// singleton port, so the lifetime costs one object per window.
/// </para>
/// </remarks>
public static class ToolsAiContentRegistration
{
    public static IServiceCollection AddToolsAiContentSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAiContentSource, ToolsAiContentSource>();

        return services;
    }
}
