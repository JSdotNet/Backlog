using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Modules.Tasks.Abstractions.Connectors;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

public sealed class SpecManagerRegistrationTests
{
    [Fact]
    public void The_sync_and_the_settings_screen_get_the_same_connector()
    {
        using var provider = Compose();

        var connector = Assert.Single(provider.GetServices<ITaskConnector>());
        var signIn = Assert.Single(provider.GetServices<ITaskConnectorSignIn>());

        Assert.Same(connector, signIn);
        Assert.Equal("spec-manager", connector.Descriptor.Id);
        Assert.True(connector.Capabilities.HasEffort);
        Assert.True(connector.Capabilities.HasDependencies);
    }

    [Fact]
    public void Adding_it_twice_registers_one_connector()
    {
        var services = new ServiceCollection();
        services.AddSpecManager(_ => new InMemorySpecManagerTokenStore());
        services.AddSpecManager(_ => new InMemorySpecManagerTokenStore());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.Single(provider.GetServices<ITaskConnector>());
        Assert.Single(provider.GetServices<ITaskConnectorSignIn>());
    }

    [Fact]
    public void The_host_supplies_the_token_store()
    {
        var store = new InMemorySpecManagerTokenStore();
        using var provider = Compose(store);

        Assert.Same(store, provider.GetRequiredService<ISpecManagerTokenStore>());
    }

    [Fact]
    public void The_production_installation_is_the_default_and_a_host_may_point_elsewhere()
    {
        using var shipped = Compose();
        Assert.Equal(new Uri(SpecManagerOptions.DefaultBaseUrl), shipped.GetRequiredService<IOptions<SpecManagerOptions>>().Value.BaseUrl);

        var services = new ServiceCollection();
        services.AddSpecManager(_ => new InMemorySpecManagerTokenStore(), options => options.BaseUrl = new Uri("http://localhost:5000"));
        using var local = services.BuildServiceProvider();
        Assert.Equal("http://localhost:5000", local.GetRequiredService<IOptions<SpecManagerOptions>>().Value.Root);
    }

    [Fact]
    public void A_base_url_that_is_not_absolute_http_fails_when_the_options_are_read()
    {
        var services = new ServiceCollection();
        services.AddSpecManager(_ => new InMemorySpecManagerTokenStore(), options => options.BaseUrl = new Uri("ftp://spec.test"));
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<SpecManagerOptions>>().Value);
    }

    [Fact]
    public void The_pipeline_budgets_fit_a_whole_backlog_in_one_answer()
    {
        var options = new HttpStandardResilienceOptions();

        SpecManagerHttpRegistration.ConfigurePipeline(options);

        Assert.Equal(SpecManagerHttpRegistration.AttemptBudget, options.AttemptTimeout.Timeout);
        Assert.Equal(SpecManagerHttpRegistration.TotalBudget, options.TotalRequestTimeout.Timeout);
        Assert.Equal(SpecManagerHttpRegistration.AttemptBudget * 2, options.CircuitBreaker.SamplingDuration);
    }

    /// <summary>A retried registration is a second client at the installation, and a
    /// retried refresh presents a token the first attempt may have rotated.</summary>
    [Fact]
    public async Task A_post_that_fails_is_sent_once()
    {
        var (client, handler) = PipelineClient();

        using var content = new StringContent("{}");
        using var response = await client.PostAsync(new Uri("https://spec.test/oauth/token"), content, TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task A_read_that_fails_is_retried()
    {
        var (client, handler) = PipelineClient();

        using var response = await client.GetAsync(new Uri("https://spec.test/api/producten/x/backlog"), TestContext.Current.CancellationToken);

        Assert.True(handler.RequestCount > 1, $"Expected a retry, saw {handler.RequestCount} request(s).");
    }

    private static (HttpClient Client, CountingHandler Handler) PipelineClient()
    {
        var handler = new CountingHandler();
        var services = new ServiceCollection();
        services.AddSpecManager(_ => new InMemorySpecManagerTokenStore());
        services.AddHttpClient(SpecManagerClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.PostConfigure<HttpStandardResilienceOptions>(SpecManagerClient.HttpClientName + "-standard", options => options.Retry.Delay = TimeSpan.Zero);

        var client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient(SpecManagerClient.HttpClientName);
        return (client, handler);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private int _count;

        public int RequestCount => _count;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        }
    }

    private static ServiceProvider Compose(ISpecManagerTokenStore? store = null)
    {
        var services = new ServiceCollection();
        services.AddSpecManager(_ => store ?? new InMemorySpecManagerTokenStore());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
