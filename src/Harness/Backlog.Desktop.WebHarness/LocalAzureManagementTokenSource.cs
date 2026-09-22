using Backlog.Infrastructure.AzureFoundry;

namespace Backlog.Desktop.WebHarness;

/// <summary>
/// The token the harness hands the Foundry cost client when its settings are the
/// local seed. The stand-in service checks no bearer, so any string will do; a
/// real one would need an Azure sign-in on the machine, which a harness session
/// should not depend on.
/// </summary>
/// <remarks>Public rather than internal, like <c>LocalDevelopmentDevToolService</c>
/// beside it: the harness project's public surface is what its tests see.</remarks>
public sealed class LocalAzureManagementTokenSource : IAzureManagementTokenSource
{
    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult("local-development");
}
