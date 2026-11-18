using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.Enrolment;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Backlog.Modules.Sync.Features.Connector;

/// <summary>The owner's desktop asks how the remote connector stands.</summary>
public sealed record DescribeConnectorQuery(OwnerScope Scope);

/// <summary>The owner's desktop mints a new client secret, replacing any previous one.</summary>
public sealed record CreateConnectorSecretCommand(OwnerScope Scope);

/// <summary>The owner's desktop revokes the client secret and everything issued under it.</summary>
public sealed record RevokeConnectorSecretCommand(OwnerScope Scope);

/// <summary>Who may manage the connector: a device of the configured owner, and nobody when
/// no owner is configured.</summary>
internal static class ConnectorOwnership
{
    internal static bool IsOwner(OwnerEnrolment owner, OwnerScope scope) =>
        owner.Owner is { } configured && configured == scope.OwnerId;

    internal static Error NotOwner() => Error.Validation(
        SyncErrorCodes.ConnectorNotOwner,
        "Only the owner's devices manage the remote connector.");

    /// <summary>The handler's activity, tagged with the device's owner — on the trace only.</summary>
    internal static System.Diagnostics.Activity? Start(string name, OwnerScope scope)
    {
        var activity = ConnectorRefusals.Start(name);
        activity?.SetTag(SyncTelemetry.OwnerIdTag, scope.OwnerId.ToString());
        return activity;
    }
}

/// <summary>The connector's one client as stored, or an unregistered one when it has never
/// had a secret.</summary>
internal static class ConnectorClients
{
    internal static async Task<ConnectorClient> Current(IConnectorClientStore clients, ConnectorSettings settings, CancellationToken cancellationToken) =>
        await clients.Find(settings.ClientId, cancellationToken) ?? ConnectorClient.Unregistered(settings.ClientId);
}

/// <summary>Whether a secret exists, when it was made and last used, and the client id and
/// redirect to enter in claude.ai. Never the secret.</summary>
public sealed class DescribeConnectorQueryHandler(IConnectorClientStore clients, OwnerEnrolment owner, ConnectorSettings settings)
    : IQueryHandler<DescribeConnectorQuery, Result<ConnectorStatusResponse>>
{
    public async Task<Result<ConnectorStatusResponse>> Handle(DescribeConnectorQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        using var activity = ConnectorOwnership.Start("sync.connector.describe", query.Scope);

        if (!ConnectorOwnership.IsOwner(owner, query.Scope))
        {
            return ConnectorOwnership.NotOwner();
        }

        var client = await ConnectorClients.Current(clients, settings, cancellationToken);

        return new ConnectorStatusResponse(
            client.HasSecret,
            client.HasSecret ? client.CreatedAt : null,
            client.HasSecret ? client.LastUsedAt : null,
            settings.ClientId,
            new Uri(settings.RedirectUri));
    }
}

/// <summary>
/// Mints a 256-bit client secret, keeps only its hash, and answers the plaintext once. The
/// client generation moves on, so every code and refresh token issued under the previous
/// secret dies with it.
/// </summary>
public sealed partial class CreateConnectorSecretCommandHandler(
    IConnectorClientStore clients,
    OwnerEnrolment owner,
    ConnectorSettings settings,
    ICredentialHasher hasher,
    TimeProvider clock,
    ILogger<CreateConnectorSecretCommandHandler> log)
    : ICommandHandler<CreateConnectorSecretCommand, Result<ConnectorSecretResponse>>
{
    public async Task<Result<ConnectorSecretResponse>> Handle(CreateConnectorSecretCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = ConnectorOwnership.Start("sync.connector.secret.create", command.Scope);

        if (!ConnectorOwnership.IsOwner(owner, command.Scope))
        {
            return ConnectorOwnership.NotOwner();
        }

        var current = await ConnectorClients.Current(clients, settings, cancellationToken);
        var secret = EnrolmentCodes.NewSecret();
        var now = clock.GetUtcNow();

        await clients.Save(
            new ConnectorClient(settings.ClientId, hasher.Hash(secret), now, LastUsedAt: null, current.Generation + 1),
            cancellationToken);

        LogCreated(log, command.Scope.DeviceId.Value, current.Generation + 1);
        return new ConnectorSecretResponse(settings.ClientId, secret, now);
    }

    // Inherited ADR 0013: an administrative operation leaves a trace — who and which
    // generation, never the secret.
    [LoggerMessage(Level = LogLevel.Information, Message = "Connector client secret created by device {DeviceId}; client generation is now {Generation}.")]
    private static partial void LogCreated(ILogger logger, Guid deviceId, int generation);
}

/// <summary>
/// Forgets the secret and moves the client generation on: the secret stops authenticating,
/// every refresh token stops refreshing, and a new authorize is refused until a secret is
/// made again. Access tokens already out carry the old generation for the resource server to
/// refuse.
/// </summary>
public sealed partial class RevokeConnectorSecretCommandHandler(
    IConnectorClientStore clients,
    OwnerEnrolment owner,
    ConnectorSettings settings,
    ILogger<RevokeConnectorSecretCommandHandler> log)
    : ICommandHandler<RevokeConnectorSecretCommand, Result>
{
    public async Task<Result> Handle(RevokeConnectorSecretCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = ConnectorOwnership.Start("sync.connector.secret.revoke", command.Scope);

        if (!ConnectorOwnership.IsOwner(owner, command.Scope))
        {
            return Result.Failure(ConnectorOwnership.NotOwner());
        }

        var current = await ConnectorClients.Current(clients, settings, cancellationToken);

        await clients.Save(
            current with { SecretHash = null, CreatedAt = null, LastUsedAt = null, Generation = current.Generation + 1 },
            cancellationToken);

        LogRevoked(log, command.Scope.DeviceId.Value, current.Generation + 1);
        return Result.Success();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connector client secret revoked by device {DeviceId}; client generation is now {Generation}.")]
    private static partial void LogRevoked(ILogger logger, Guid deviceId, int generation);
}
