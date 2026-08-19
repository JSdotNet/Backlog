using Backlog.Modules.Backlog.Abstractions.DataTransferObjects;
using Backlog.Modules.Backlog.Features.DeleteEntry;
using Backlog.Modules.Backlog.Features.LinkEntryToIssue;
using Backlog.Modules.Backlog.Features.ListEntries;
using Backlog.Modules.Backlog.Features.RecordEntryUsage;
using Backlog.Modules.Backlog.Features.ReorderEntries;
using Backlog.Modules.Backlog.Features.SaveEntryFromText;
using Backlog.Modules.Backlog.Abstractions.Services;
using Backlog.Modules.Backlog.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Backlog.Extensions;

/// <summary>
/// The module's composition root. A host calls this once and gets every use case
/// the Backlog context offers; it never registers a handler itself, and never
/// sees the aggregate.
/// <para>
/// <see cref="IBacklogRepository"/> is deliberately not registered here — it is
/// an internal port, and which adapter implements it is the host's decision
/// (today the file-system one, pointed at wherever the person keeps their
/// backlog).
/// </para>
/// </summary>
public static class BacklogModuleRegistration
{
    public static IServiceCollection AddBacklogModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IQueryHandler<ListEntriesQuery, IReadOnlyList<BacklogEntryDto>>, ListEntriesQueryHandler>();
        services.AddScoped<ICommandHandler<SaveEntryFromTextCommand, Result<SavedEntryDto>>, SaveEntryFromTextCommandHandler>();
        services.AddScoped<ICommandHandler<LinkEntryToIssueCommand, Result<BacklogEntryDto>>, LinkEntryToIssueCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteEntryCommand>, DeleteEntryCommandHandler>();
        services.AddScoped<ICommandHandler<ReorderEntriesCommand>, ReorderEntriesCommandHandler>();
        services.AddScoped<ICommandHandler<RecordEntryUsageCommand>, RecordEntryUsageCommandHandler>();

        services.AddScoped<IBacklogEntries, BacklogEntries>();

        return services;
    }
}
