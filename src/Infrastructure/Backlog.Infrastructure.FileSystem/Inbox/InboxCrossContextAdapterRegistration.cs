using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Infrastructure.FileSystem.Inbox;

/// <summary>
/// The cross-context joins the Inbox takes part in, answered by adapters that
/// may see both contexts: an inbox item becomes backlog entries
/// (<see cref="IInboxBacklogTarget"/> over <see cref="ITaskItems"/>), and the
/// backlog's tags are offered to the inbox's picker
/// (<see cref="IBacklogTagSource"/> over the same port), and its open tasks to
/// a batch's dependency proposal (<see cref="IInboxTaskReferences"/>, the same).
/// <para>
/// Registered here — in one place both hosts call — so the lifetime cannot
/// drift between the desktop app and the web harness. All three capture a
/// service the Tasks module registers as <c>Scoped</c>, so they must be
/// <c>Scoped</c> too: a singleton over a scoped dependency is a captive
/// dependency that a validating root provider refuses to build.
/// </para>
/// <para>
/// The Inbox's other outward port, <see cref="IInboxPlanDrafter"/>, is not
/// here. It is answered by the Azure Foundry adapter, which a host registers
/// beside that adapter's chat client — and may leave out, in which case the
/// module answers <c>inbox.plan.not_configured</c>.
/// </para>
/// </summary>
public static class InboxCrossContextAdapterRegistration
{
    /// <summary>
    /// Registers the inbox cross-context adapters. Call after
    /// <c>AddTasksModule</c>, which supplies the scoped port they capture.
    /// </summary>
    public static IServiceCollection AddInboxCrossContextAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IInboxBacklogTarget, InboxBacklogTarget>();
        services.AddScoped<IBacklogTagSource, InboxBacklogTagSource>();
        services.AddScoped<IInboxTaskReferences, InboxTaskReferences>();

        // Where an item's files are kept: under the workspace's inbox folder,
        // read per call so a moved workspace takes them along. A singleton,
        // because the sync loop's intake resolves it from the root provider; the
        // settings store it reads is one too. Not a join between contexts, but
        // registered with them because both hosts that call this own a workspace.
        services.TryAddSingleton<IInboxAttachmentFiles>(sp =>
            new WorkspaceInboxAttachmentFiles(() => sp.GetRequiredService<WorkspaceSettingsStore>().InboxDirectory));

        return services;
    }
}
