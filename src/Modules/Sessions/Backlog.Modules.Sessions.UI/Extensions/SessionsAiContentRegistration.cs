using Backlog.SharedKernel.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Sessions.UI.Extensions;

/// <summary>
/// Wires the Sessions context's answer to <see cref="IAiContentSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// A host must compose the session readers before calling this —
/// <c>AddAgentSessionSource()</c>, from <c>Backlog.Infrastructure.Sessions</c> — and the
/// source answers from the merged catalog that call registers and from nothing else;
/// see <see cref="SessionsAiContentSource"/> for why the transcripts stay out. Scoped for
/// consistency with the other areas' sources, which read per-circuit state; this one
/// holds only the singleton port, so the lifetime costs one object per window.
/// </para>
/// </remarks>
public static class SessionsAiContentRegistration
{
    public static IServiceCollection AddSessionsAiContentSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAiContentSource, SessionsAiContentSource>();

        return services;
    }
}
