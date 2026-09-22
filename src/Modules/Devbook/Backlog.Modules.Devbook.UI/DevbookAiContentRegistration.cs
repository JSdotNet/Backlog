using Backlog.SharedKernel.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Wires Devbook's answer to <see cref="IAiContentSource"/>, and the open-chapter
/// mirror it pins from.
/// </summary>
/// <remarks>
/// <para>
/// Both scoped. <see cref="DevbookOpenChapter"/> is what one reader has on
/// screen, and in the web harness two circuits are two readers — a singleton
/// there would pin one visitor's chapter for another. On the desktop there is
/// one window and the scope is the window, so scoped and singleton are the same
/// object. <see cref="DevbookScope"/> beside it stays a singleton because it
/// holds nothing; this holds something.
/// </para>
/// <para>
/// A host must register <see cref="Backlog.Modules.Devbook.Abstractions.IDevbookSearch"/>
/// and <see cref="Backlog.Modules.Devbook.Abstractions.IDevbookFolderSource"/> before
/// calling this; the source only holds them.
/// </para>
/// </remarks>
public static class DevbookAiContentRegistration
{
    public static IServiceCollection AddDevbookAiContentSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<DevbookOpenChapter>();
        services.AddScoped<IAiContentSource, DevbookAiContentSource>();

        return services;
    }
}
