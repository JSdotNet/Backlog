using Backlog.Infrastructure.Claude;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Claude.UnitTests;

/// <summary>
/// What a host gets for calling <c>AddClaude()</c>: the settings store it names,
/// the Admin API transport as a typed client, and the probe, usage client and
/// spend cache over them, each once. The settings file and the cache folder are
/// the host's to say, because each host keeps them somewhere different.
/// </summary>
public sealed class ClaudeRegistrationTests
{
    [Fact]
    public void The_settings_store_is_the_one_the_host_names()
    {
        var store = new ClaudeSettingsStore(TempPath("claude.settings.json"));
        var services = new ServiceCollection();
        services.AddClaude(_ => store, _ => TempPath("spend"));

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ClaudeSettingsStore));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.Same(store, provider.GetRequiredService<ClaudeSettingsStore>());
    }

    [Theory]
    [InlineData(typeof(IClaudeAccountProbe))]
    [InlineData(typeof(IClaudeUsageClient))]
    [InlineData(typeof(IClaudeCodeUsageCache))]
    public void Every_claude_port_over_the_transport_is_a_singleton(Type port)
    {
        var services = new ServiceCollection();
        services.AddClaude(_ => new ClaudeSettingsStore(TempPath("claude.settings.json")), _ => TempPath("spend"));

        var descriptor = Assert.Single(services, d => d.ServiceType == port);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void The_transport_is_a_typed_client_and_everything_resolves()
    {
        var services = new ServiceCollection();
        services.AddClaude(_ => new ClaudeSettingsStore(TempPath("claude.settings.json")), _ => TempPath("spend"));

        Assert.Contains(services, d => d.ServiceType == typeof(IHttpClientFactory));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<ClaudeAdminTransport>(provider.GetRequiredService<IClaudeTransport>());
        Assert.NotNull(provider.GetRequiredService<IClaudeAccountProbe>());
        Assert.NotNull(provider.GetRequiredService<IClaudeUsageClient>());
        Assert.NotNull(provider.GetRequiredService<IClaudeCodeUsageCache>());
    }

    private static string TempPath(string name) =>
        Path.Combine(Path.GetTempPath(), "backlog-claude-registration", Guid.NewGuid().ToString("N"), name);
}
