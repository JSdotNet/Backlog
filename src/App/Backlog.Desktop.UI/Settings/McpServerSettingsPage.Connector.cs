using Backlog.Infrastructure.Sync;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;

// The component's published namespace, not the folder's: see the note at the top
// of Settings.razor for why the two must differ.
namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The Remote connector card on Settings › MCP server (local ADR 0058 §8): the claude.ai
/// connector's client id and redirect URI, where its secret stands, and Create, Replace
/// and Revoke over <see cref="IRemoteConnector"/>.
/// <para>
/// A partial of its own because the card's state is self-contained and the page's file is
/// long enough already. The port is read off the service provider rather than injected: a
/// host with no sync client still renders the page, and the card says so.
/// </para>
/// <para>
/// The desktop keeps nothing of the connector (local ADR 0030): a secret just created lives
/// in <see cref="_connectorSecret"/> until Done, a revoke, a replacement or the page going
/// away, and is never logged or written anywhere. Leaving the page while a Create or Replace
/// is on its way cancels the call; if the service minted the secret anyway, it is lost with
/// the page - the service keeps only its hash - and the person creates another.
/// </para>
/// </summary>
public partial class McpServerSettingsPage
{
    /// <summary>The card with the claude.ai connector - one of the page's
    /// <c>SettingsSection.CardTitles</c>, between Agents and Activity.</summary>
    public const string RemoteConnectorCardTitle = McpRemoteConnector.CardTitle;

    /// <summary>Which destructive act is waiting for its second press.</summary>
    private enum ConnectorConfirm
    {
        None,
        Replace,
        Revoke
    }

    private IRemoteConnector? _connector;

    private TimeProvider _connectorClock = TimeProvider.System;

    /// <summary>Cancelled when the page goes away, so no connector call outlives it.</summary>
    private readonly CancellationTokenSource _connectorCancel = new();

    /// <summary>Why the card offers nothing - no sync client, or not paired - or null.</summary>
    private string? _connectorUnavailable;

    /// <summary>The connector as last read, or null before the first read answers.</summary>
    private ConnectorStatusResponse? _connectorStatus;

    /// <summary>The secret just created, shown once; null otherwise.</summary>
    private string? _connectorSecret;

    /// <summary>Why the last call failed, or null.</summary>
    private string? _connectorError;

    private ConnectorConfirm _connectorConfirm;

    private bool _connectorBusy;

    /// <summary>Starts the card. The status read is a network call, so the page does not
    /// wait for it: when it answers after the page's first render, the card draws again.</summary>
    private void StartConnector()
    {
        _connector = Services.GetService<IRemoteConnector>();
        _connectorClock = Services.GetService<TimeProvider>() ?? TimeProvider.System;

        if (_connector is null)
        {
            _connectorUnavailable = McpRemoteConnector.NoSyncClient;
            return;
        }

        if (!_connector.IsPaired)
        {
            _connectorUnavailable = McpRemoteConnector.NotPaired;
            return;
        }

        var load = LoadConnectorAsync();
        if (!load.IsCompleted) _ = DrawWhenAnsweredAsync(load);
    }

    private async Task DrawWhenAnsweredAsync(Task load)
    {
        await load;
        if (!_disposed) StateHasChanged();
    }

    private async Task LoadConnectorAsync()
    {
        if (_connector is null || _disposed) return;

        try
        {
            var status = await _connector.GetStatusAsync(_connectorCancel.Token);
            if (_disposed) return;

            // A switch expression, so CS8509 proves both cases handled (app-defaults ADR 0005).
            _connectorError = status switch
            {
                Ok<ConnectorStatusResponse>(var read) => Shown(read),
                Error error => error.Message,
            };

            string? Shown(ConnectorStatusResponse read)
            {
                _connectorStatus = read;
                return null;
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
            // The page went away; nobody is left to tell.
        }
    }

    /// <summary>Reads the status again after the first read failed.</summary>
    private async Task RetryConnectorAsync()
    {
        if (_connectorBusy) return;

        _connectorBusy = true;
        try
        {
            await LoadConnectorAsync();
        }
        finally
        {
            _connectorBusy = false;
        }
    }

    /// <summary>Create secret when there is none; with one set - or one just shown, whatever
    /// the last read said - Replace secret asks first.</summary>
    private async Task CreateConnectorSecretAsync()
    {
        var hasSecret = _connectorStatus is { HasSecret: true } || _connectorSecret is not null;
        if (hasSecret && _connectorConfirm != ConnectorConfirm.Replace)
        {
            _connectorConfirm = ConnectorConfirm.Replace;
            return;
        }

        await RunConnectorAsync(async (connector, cancellationToken) =>
        {
            _connectorSecret = null;
            var created = await connector.CreateSecretAsync(cancellationToken);
            if (_disposed) return null;
            if (created is not Ok<ConnectorSecretResponse>(var secret)) return ((Error)created.Value!).Message;

            _connectorSecret = secret.ClientSecret;
            // The service holds a secret now even if the read that follows fails, so the
            // card says so and the next press still asks before it replaces this one.
            if (_connectorStatus is { } status)
            {
                _connectorStatus = status with { HasSecret = true, CreatedAt = secret.CreatedAt, LastUsedAt = null };
            }

            return null;
        });
    }

    private void AskRevokeConnector() => _connectorConfirm = ConnectorConfirm.Revoke;

    private void KeepConnector() => _connectorConfirm = ConnectorConfirm.None;

    private Task RevokeConnectorAsync() => RunConnectorAsync(async (connector, cancellationToken) =>
    {
        var revoked = await connector.RevokeSecretAsync(cancellationToken);
        if (revoked is Error error) return error.Message;

        _connectorSecret = null;
        return null;
    });

    /// <summary>The act waiting for its second press: Replace mints again, Revoke cuts off.</summary>
    private Task ConfirmConnectorAsync() =>
        _connectorConfirm == ConnectorConfirm.Revoke ? RevokeConnectorAsync() : CreateConnectorSecretAsync();

    private string ConnectorQuestion =>
        _connectorConfirm == ConnectorConfirm.Revoke ? McpRemoteConnector.RevokeQuestion : McpRemoteConnector.ReplaceQuestion;

    /// <summary>Closes the secret: from here on only a new one can be shown.</summary>
    private void CloseConnectorSecret() => _connectorSecret = null;

    /// <summary>Runs one call, then reads the status again so the card says what the service
    /// holds. A failure leaves its sentence on the card.</summary>
    private async Task RunConnectorAsync(Func<IRemoteConnector, CancellationToken, Task<string?>> call)
    {
        if (_connector is null || _connectorBusy || _disposed) return;

        _connectorBusy = true;
        try
        {
            var failure = await call(_connector, _connectorCancel.Token);
            if (_disposed) return;

            _connectorConfirm = ConnectorConfirm.None;
            if (failure is not null)
            {
                _connectorError = failure;
                return;
            }

            await LoadConnectorAsync();
        }
        catch (OperationCanceledException) when (_disposed)
        {
            // The page went away mid-call; nobody is left to tell.
        }
        finally
        {
            _connectorBusy = false;
        }
    }

    /// <summary>Drops the secret and cancels whatever connector call is still on its way.</summary>
    private void DisposeConnector()
    {
        _connectorSecret = null;
        _connectorCancel.Cancel();
        _connectorCancel.Dispose();
    }

    private string ConnectorState(ConnectorStatusResponse status) =>
        McpRemoteConnector.State(status, _connectorClock.GetUtcNow());
}
