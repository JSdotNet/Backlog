using Backlog.SharedKernel.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// Wires the Inbox's answer to <see cref="IAiContentSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// An extension method rather than a public class, on the precedent of every
/// other module registration: composition is the host's decision, and keeping
/// the source internal means nothing outside this project can ask the Inbox for
/// its content except through the port the shell collects.
/// </para>
/// <para>
/// Scoped, because <see cref="InboxDesktopState"/> is scoped in the web harness
/// and a singleton over it would be a captive dependency; on the desktop the
/// state is a singleton and a scoped source over it is one cheap object per
/// window. A host must register the state before calling this.
/// </para>
/// </remarks>
public static class InboxAiContentRegistration
{
    public static IServiceCollection AddInboxAiContentSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAiContentSource, InboxAiContentSource>();

        return services;
    }
}
