using Azure.Core;
using Azure.Identity;

namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>
/// A bearer token for Azure Resource Manager, which is what Cost Management is
/// read with. Its own port rather than <see cref="TokenCredential"/> directly so
/// the harness can hand the cost client a token that nothing signed, and so the
/// one real implementation can say in its own words why there is no sign-in.
/// </summary>
public interface IAzureManagementTokenSource
{
    /// <summary>The token, or an <see cref="AzureFoundryException"/> saying why
    /// there is none — in words meant for the dashboard, not for a log.</summary>
    Task<string> GetTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The developer sign-in already on this machine: Azure CLI, then Azure Developer
/// CLI, then Visual Studio.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not <see cref="DefaultAzureCredential"/>. That chain probes a
/// managed identity endpoint first, which on a laptop is a few seconds of
/// timeouts before every dashboard refresh, and it can end in a browser prompt —
/// a sign-in window opening because a chart asked for its data is not something a
/// person expects from a dashboard. The three here are silent: each either has a
/// cached sign-in or says it does not, and the reason names all three so the
/// person knows which tools would fix it.
/// </para>
/// <para>
/// The token is kept until shortly before it expires. Azure CLI mints one by
/// spawning a process, and the three cost parts ask within the same second, so
/// without this the same token would be fetched three times per refresh.
/// </para>
/// </remarks>
public sealed class DeveloperSignInTokenSource : IAzureManagementTokenSource
{
    /// <summary>Resource Manager's own scope; every ARM call, Cost Management
    /// included, is authorized against it.</summary>
    private const string ManagementScope = "https://management.azure.com/.default";

    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly TokenCredential _credential;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AccessToken? _cached;

    public DeveloperSignInTokenSource(TimeProvider? time = null)
        : this(
            new ChainedTokenCredential(
                new AzureCliCredential(),
                new AzureDeveloperCliCredential(),
                new VisualStudioCredential()),
            time)
    {
    }

    /// <summary>For a test to hand in a credential of its own.</summary>
    internal DeveloperSignInTokenSource(TokenCredential credential, TimeProvider? time = null)
    {
        _credential = credential;
        _time = time ?? TimeProvider.System;
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is { } token && token.ExpiresOn - RefreshMargin > _time.GetUtcNow())
            {
                return token.Token;
            }

            try
            {
                var fresh = await _credential
                    .GetTokenAsync(new TokenRequestContext([ManagementScope]), cancellationToken)
                    .ConfigureAwait(false);
                _cached = fresh;
                return fresh.Token;
            }
            catch (CredentialUnavailableException ex)
            {
                throw new AzureFoundryException(NoSignInMessage, ex);
            }
            catch (AuthenticationFailedException ex)
            {
                throw new AzureFoundryException(NoSignInMessage, ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal const string NoSignInMessage =
        "No Azure sign-in was found on this machine. Sign in with the Azure CLI (az login), the Azure "
        + "Developer CLI (azd auth login), or Visual Studio, as an account that can read costs on the "
        + "Foundry resource, and refresh.";
}
