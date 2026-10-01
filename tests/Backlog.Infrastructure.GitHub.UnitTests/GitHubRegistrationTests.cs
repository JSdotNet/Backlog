using Backlog.Infrastructure.GitHub;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// What a host gets for calling <c>AddGitHub()</c>: the credential route, the
/// transport and every client over it, each a singleton, with the transport's two
/// probes answered by the transport itself. The settings store and the file caches
/// are the host's to give, because they follow a workspace this project may not
/// see.
/// </summary>
public sealed class GitHubRegistrationTests
{
    [Theory]
    [InlineData(typeof(IGhCliAccountSource))]
    [InlineData(typeof(IGitHubCredentialResolver))]
    [InlineData(typeof(ResolvingGitHubTransport))]
    [InlineData(typeof(IGitHubConnectionProbe))]
    [InlineData(typeof(IGitHubAccountProbe))]
    [InlineData(typeof(IGitHubBranchCatalog))]
    [InlineData(typeof(IGitHubTreeClient))]
    [InlineData(typeof(ILocalGitRepositoryService))]
    [InlineData(typeof(IGitFileHistoryService))]
    [InlineData(typeof(IGitHubClient))]
    [InlineData(typeof(ICopilotUsageClient))]
    [InlineData(typeof(IGitHubIdentityClient))]
    [InlineData(typeof(IGitHubActivityClient))]
    [InlineData(typeof(IGitHubActivityBaselineClient))]
    [InlineData(typeof(IGitHubBillingClient))]
    [InlineData(typeof(GitHubIntegration))]
    public void Every_github_port_is_registered_once_as_a_singleton(Type port)
    {
        var services = new ServiceCollection();
        services.AddGitHub();

        var descriptor = Assert.Single(services, d => d.ServiceType == port);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void The_token_route_client_comes_with_it()
    {
        var services = new ServiceCollection();
        services.AddGitHub();

        Assert.Contains(services, d => d.ServiceType == typeof(IHttpClientFactory));
    }

    [Fact]
    public void Both_probes_are_the_transport_every_client_sends_through()
    {
        var path = Path.Combine(Path.GetTempPath(), "backlog-github-registration", Guid.NewGuid().ToString("N"), "github.settings.json");
        var services = new ServiceCollection();
        services.AddSingleton(new GitHubSettingsStore(path));
        services.AddGitHub();

        using var provider = services.BuildServiceProvider();

        var transport = provider.GetRequiredService<ResolvingGitHubTransport>();
        Assert.Same(transport, provider.GetRequiredService<IGitHubConnectionProbe>());
        Assert.Same(transport, provider.GetRequiredService<IGitHubAccountProbe>());
        Assert.NotNull(provider.GetRequiredService<IGitHubClient>());
        Assert.NotNull(provider.GetRequiredService<GitHubIntegration>());
    }
}
