using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Adapters;
using Backlog.Modules.Sync.Features.AcknowledgeInboxItem;
using Backlog.Modules.Sync.Features.CaptureInboxItem;
using Backlog.Modules.Sync.Features.DescribeOwner;
using Backlog.Modules.Sync.Features.IssueDeviceToken;
using Backlog.Modules.Sync.Features.IssuePairingCode;
using Backlog.Modules.Sync.Features.ListInbox;
using Backlog.Modules.Sync.Features.PullTasks;
using Backlog.Modules.Sync.Features.PushTasks;
using Backlog.Modules.Sync.Features.RedeemPairingCode;
using Backlog.Modules.Sync.Features.RegisterFirstDevice;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Modules.Sync.Extensions;

/// <summary>
/// The module's composition root. A host calls this once and gets every device
/// use case the Sync context offers.
/// <para>
/// <see cref="IDeviceTokenIssuer"/> is deliberately not registered here.
/// Minting a token means holding a signing key, and the host that validates
/// those tokens is the one that should hold it — registering an issuer here
/// would put the signing half in the module and the validating half in the
/// service, which is exactly how the two come to disagree.
/// </para>
/// </summary>
public static class SyncModuleRegistration
{
    public static IServiceCollection AddSyncModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICommandHandler<RegisterFirstDeviceCommand, Result<DeviceRegistrationResponse>>, RegisterFirstDeviceCommandHandler>();
        services.AddScoped<ICommandHandler<IssuePairingCodeCommand, Result<PairingCodeResponse>>, IssuePairingCodeCommandHandler>();
        services.AddScoped<ICommandHandler<RedeemPairingCodeCommand, Result<DeviceRegistrationResponse>>, RedeemPairingCodeCommandHandler>();
        services.AddScoped<ICommandHandler<IssueDeviceTokenCommand, Result<DeviceTokenResponse>>, IssueDeviceTokenCommandHandler>();
        services.AddScoped<IQueryHandler<DescribeOwnerQuery, Result<DeviceStatusResponse>>, DescribeOwnerQueryHandler>();

        services.AddScoped<ICommandHandler<PushTasksCommand, Result<PushTasksResponse>>, PushTasksCommandHandler>();
        services.AddScoped<IQueryHandler<PullTasksQuery, Result<PullTasksResponse>>, PullTasksQueryHandler>();
        services.AddScoped<IQueryHandler<ListInboxQuery, Result<IReadOnlyList<InboxItem>>>, ListInboxQueryHandler>();
        services.AddScoped<ICommandHandler<CaptureInboxItemCommand, Result<InboxItem>>, CaptureInboxItemCommandHandler>();
        services.AddScoped<ICommandHandler<AcknowledgeInboxItemCommand, Result>, AcknowledgeInboxItemCommandHandler>();

        services.TryAddSingleton<ICredentialHasher, Sha256CredentialHasher>();
        services.TryAddSingleton<IPairingCodeGenerator, RandomPairingCodeGenerator>();
        services.TryAddSingleton<IRegistrationCredentialGenerator, RandomRegistrationCredentialGenerator>();
        services.TryAddSingleton(TimeProvider.System);

        // Singletons because the in-memory adapters are the store: a scoped
        // registration would give every request an empty one. TryAdd rather
        // than Add so a host can register the Cosmos-backed adapters before
        // this call and keep them — replacing the store is expected, and is
        // meant to be one line rather than a fork of this method.
        services.TryAddSingleton<IDeviceRegistry, InMemoryDeviceRegistry>();
        services.TryAddSingleton<IPairingCodeStore, InMemoryPairingCodeStore>();
        services.TryAddSingleton<ITaskReplica, InMemoryTaskReplica>();

        return services;
    }
}
