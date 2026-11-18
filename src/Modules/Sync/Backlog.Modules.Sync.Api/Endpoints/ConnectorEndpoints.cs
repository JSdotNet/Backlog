using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Security;
using Backlog.Modules.Sync.Features.Connector;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Api.Endpoints;

/// <summary>
/// The remote connector as the owner's desktop manages it (local ADR 0052 §8): describe it,
/// mint a client secret shown once, and revoke it. Bearer and owner-scoped like every sync
/// route; the handlers also refuse a device of any owner but the configured one.
/// </summary>
internal static class ConnectorEndpoints
{
    internal static IEndpointRouteBuilder MapConnectorEndpoints(this IEndpointRouteBuilder sync)
    {
        var connector = sync.MapGroup(string.Empty)
            .WithTags("Connector")
            .RequireAuthorization(SyncPolicies.PairedDevice)
            .RequireOwnerScope()
            .AddEndpointFilter<ReplicaFaultFilter>();

        connector.MapGet(SyncRoutes.Connector, Describe)
            .WithSummary("Describes the remote connector: whether a secret exists, and when it was made and last used.");

        connector.MapPost(SyncRoutes.ConnectorSecret, CreateSecret)
            .WithSummary("Mints a new client secret, replacing any previous one, and answers it once.");

        connector.MapDelete(SyncRoutes.ConnectorSecret, RevokeSecret)
            .WithSummary("Revokes the client secret and every refresh token issued under it.");

        return sync;
    }

    private static async Task<IResult> Describe(
        HttpContext context,
        IQueryHandler<DescribeConnectorQuery, Result<ConnectorStatusResponse>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new DescribeConnectorQuery(context.GetOwnerScope()), cancellationToken);

        return SyncResults.From(context, result, Results.Ok);
    }

    private static async Task<IResult> CreateSecret(
        HttpContext context,
        ICommandHandler<CreateConnectorSecretCommand, Result<ConnectorSecretResponse>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new CreateConnectorSecretCommand(context.GetOwnerScope()), cancellationToken);

        AuthorizationServerEndpoints.NoStore(context.Response);
        return SyncResults.From(context, result, Results.Ok);
    }

    private static async Task<IResult> RevokeSecret(
        HttpContext context,
        ICommandHandler<RevokeConnectorSecretCommand, Result> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new RevokeConnectorSecretCommand(context.GetOwnerScope()), cancellationToken);

        return SyncResults.From(context, result, Results.NoContent);
    }
}
