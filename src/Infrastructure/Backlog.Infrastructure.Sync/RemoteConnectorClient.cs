using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The owner calls behind <see cref="IRemoteConnector"/>, over the client
/// <c>AddSyncClient</c> configures with the sync pipeline, so each leaves carrying this
/// device's token. An expected 4xx — a device of another owner — comes back as a failed
/// <see cref="Result{TValue}"/> with the service's code, the <see cref="SyncHttp"/>
/// convention every client here shares.
/// <para>
/// Nothing is logged or kept: the secret <see cref="CreateSecretAsync"/> reads is returned
/// to the caller and goes nowhere else.
/// </para>
/// </summary>
public sealed class RemoteConnectorClient : IRemoteConnector
{
    private readonly HttpClient _http;
    private readonly IDeviceCredentialStore _credentials;

    /// <param name="http">The client <c>AddSyncClient</c> configures.</param>
    /// <param name="credentials">Where this device's credential is kept, read only to say
    /// whether there is one.</param>
    public RemoteConnectorClient(HttpClient http, IDeviceCredentialStore credentials)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(credentials);

        _http = http;
        _credentials = credentials;
    }

    /// <inheritdoc />
    public bool IsPaired => _credentials.Current is not null;

    /// <inheritdoc />
    public async Task<Result<ConnectorStatusResponse>> GetStatusAsync(CancellationToken cancellationToken = default) =>
        await SyncHttp.SendAsync<ConnectorStatusResponse>(
            () => _http.GetAsync(SyncRoutes.Absolute(SyncRoutes.Connector), cancellationToken),
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Result<ConnectorSecretResponse>> CreateSecretAsync(CancellationToken cancellationToken = default) =>
        await SyncHttp.SendAsync<ConnectorSecretResponse>(
            () => _http.PostAsync(SyncRoutes.Absolute(SyncRoutes.ConnectorSecret), content: null, cancellationToken),
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Result> RevokeSecretAsync(CancellationToken cancellationToken = default)
    {
        // 204 carries no body, so success is read as the status alone.
        var revoked = await SyncHttp.SendAsync(
            () => _http.DeleteAsync(SyncRoutes.Absolute(SyncRoutes.ConnectorSecret), cancellationToken),
            (_, _) => Task.FromResult(true),
            cancellationToken).ConfigureAwait(false);

        return revoked.IsSuccess ? Result.Success() : Result.Failure(revoked.Error);
    }
}
