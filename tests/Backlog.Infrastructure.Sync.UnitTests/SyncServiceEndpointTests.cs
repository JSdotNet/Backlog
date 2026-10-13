using Backlog.Infrastructure.Sync.Extensions;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The three rungs a head outside Aspire can stand on, in the order it tries them:
/// what the person typed into Settings, what the environment says, and the
/// service-discovery name that only resolves under an AppHost run.
/// </summary>
public sealed class SyncServiceEndpointTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-sync-endpoint-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void The_settings_value_wins_over_everything()
    {
        var settings = NewStore();
        settings.SetServiceUrl("https://settings.example.test");
        var endpoint = new SyncServiceEndpoint(settings, Environment(
            (SyncServiceEndpoint.EnvironmentVariable, "https://env.example.test"),
            ("services__sync__https__0", "https://localhost:5555")));

        var resolved = endpoint.Resolve();

        Assert.Equal(new Uri("https://settings.example.test"), resolved.Address);
        Assert.Equal(SyncServiceAddressSource.Settings, resolved.Source);
    }

    [Fact]
    public void The_environment_variable_is_next()
    {
        var endpoint = new SyncServiceEndpoint(NewStore(), Environment(
            (SyncServiceEndpoint.EnvironmentVariable, "https://env.example.test/"),
            ("services__sync__https__0", "https://localhost:5555")));

        var resolved = endpoint.Resolve();

        Assert.Equal(new Uri("https://env.example.test/"), resolved.Address);
        Assert.Equal(SyncServiceAddressSource.EnvironmentVariable, resolved.Source);
    }

    [Fact]
    public void An_unparseable_environment_variable_is_skipped_rather_than_thrown()
    {
        var endpoint = new SyncServiceEndpoint(NewStore(), Environment(
            (SyncServiceEndpoint.EnvironmentVariable, "not a url"),
            ("services__sync__https__0", "https://localhost:5555")));

        var resolved = endpoint.Resolve();

        Assert.Equal(SyncServiceEndpoint.ServiceDiscoveryAddress, resolved.Address);
        Assert.Equal(SyncServiceAddressSource.ServiceDiscovery, resolved.Source);
    }

    [Theory]
    [InlineData("services__sync__https__0")]
    [InlineData("services__sync__http__0")]
    public void Under_an_apphost_run_the_service_discovery_name_is_used(string discoveryVariable)
    {
        var endpoint = new SyncServiceEndpoint(NewStore(), Environment((discoveryVariable, "https://localhost:5555")));

        var resolved = endpoint.Resolve();

        Assert.Equal(SyncServiceEndpoint.ServiceDiscoveryAddress, resolved.Address);
        Assert.Equal(SyncServiceAddressSource.ServiceDiscovery, resolved.Source);
    }

    /// <summary>
    /// The installed app with nothing set: the address is still the discovery
    /// name, so the request fails the way it always did — but the source says
    /// why, which is what the Settings page shows instead of a DNS error.
    /// </summary>
    [Fact]
    public void With_nothing_configured_the_source_is_none()
    {
        var endpoint = new SyncServiceEndpoint(NewStore(), Environment());

        var resolved = endpoint.Resolve();

        Assert.Equal(SyncServiceEndpoint.ServiceDiscoveryAddress, resolved.Address);
        Assert.Equal(SyncServiceAddressSource.None, resolved.Source);
    }

    [Fact]
    public void A_saved_url_reaches_the_next_client_the_factory_hands_out()
    {
        var settings = NewStore();
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton(settings);
        services.AddSingleton(sp => new SyncServiceEndpoint(sp.GetRequiredService<SyncServiceSettingsStore>(), Environment()));
        services.AddSyncClient(sp => sp.GetRequiredService<SyncServiceEndpoint>().Resolve().Address);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        var before = factory.CreateClient(SyncTokenProvider.HttpClientName).BaseAddress;
        settings.SetServiceUrl("https://later.example.test");
        var after = factory.CreateClient(SyncTokenProvider.HttpClientName).BaseAddress;

        Assert.Equal(SyncServiceEndpoint.ServiceDiscoveryAddress, before);
        Assert.Equal(new Uri("https://later.example.test"), after);
    }

    // ResolveStandalone: the phone's ladder. No Settings page and no service
    // discovery, so the discovery variable's value is used literally and the
    // address baked in at build time sits between the environment and the
    // emulator fallback.

    private static readonly Uri EmulatorFallback = new("http://10.0.2.2:5000");

    [Fact]
    public void Standalone_the_discovery_value_wins_over_everything()
    {
        var resolved = SyncServiceEndpoint.ResolveStandalone(
            Environment(
                ("services__sync__http__0", "http://localhost:5555"),
                (SyncServiceEndpoint.EnvironmentVariable, "https://env.example.test")),
            "https://baked.example.test",
            EmulatorFallback);

        Assert.Equal(new Uri("http://localhost:5555"), resolved.Address);
        Assert.Equal(SyncServiceAddressSource.ServiceDiscovery, resolved.Source);
    }

    [Fact]
    public void Standalone_the_environment_variable_beats_the_build_default()
    {
        var resolved = SyncServiceEndpoint.ResolveStandalone(
            Environment((SyncServiceEndpoint.EnvironmentVariable, "https://env.example.test")),
            "https://baked.example.test",
            EmulatorFallback);

        Assert.Equal(new Uri("https://env.example.test"), resolved.Address);
        Assert.Equal(SyncServiceAddressSource.EnvironmentVariable, resolved.Source);
    }

    [Fact]
    public void Standalone_the_build_default_is_used_when_no_variable_is_set()
    {
        var resolved = SyncServiceEndpoint.ResolveStandalone(
            Environment(),
            "https://baked.example.test",
            EmulatorFallback);

        Assert.Equal(new Uri("https://baked.example.test"), resolved.Address);
        Assert.Equal(SyncServiceAddressSource.BuildDefault, resolved.Source);
    }

    [Fact]
    public void Standalone_with_nothing_set_the_fallback_is_used()
    {
        var resolved = SyncServiceEndpoint.ResolveStandalone(Environment(), null, EmulatorFallback);

        Assert.Equal(new Uri("http://10.0.2.2:5000"), resolved.Address);
        Assert.Equal(SyncServiceAddressSource.None, resolved.Source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("ftp://files.example.test")]
    [InlineData("/relative/path")]
    public void Standalone_a_blank_or_unusable_value_at_any_rung_is_skipped_rather_than_thrown(string unusable)
    {
        var resolved = SyncServiceEndpoint.ResolveStandalone(
            Environment(
                ("services__sync__http__0", unusable),
                (SyncServiceEndpoint.EnvironmentVariable, unusable)),
            unusable,
            EmulatorFallback);

        Assert.Equal(new Uri("http://10.0.2.2:5000"), resolved.Address);
        Assert.Equal(SyncServiceAddressSource.None, resolved.Source);
    }

    private SyncServiceSettingsStore NewStore() =>
        new(Path.Combine(_root, Guid.NewGuid().ToString("N"), "sync-service.json"));

    private static Func<string, string?> Environment(params (string Name, string Value)[] variables)
    {
        var lookup = variables.ToDictionary(v => v.Name, v => v.Value, StringComparer.OrdinalIgnoreCase);
        return name => lookup.GetValueOrDefault(name);
    }
}
