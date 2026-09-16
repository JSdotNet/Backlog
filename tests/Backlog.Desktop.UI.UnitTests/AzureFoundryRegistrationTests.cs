using System.Net;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.AzureFoundry.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What a host gets for calling <c>AddAzureFoundryChatClient()</c>: the typed
/// chat client on a pipeline sized for a chat completion, asserted the way a
/// host finds out — by building the provider and reading the options back
/// under the name the standard handler registers them.
/// </summary>
public sealed class AzureFoundryRegistrationTests
{
    /// <summary>The host's defaults — a ten-second attempt, three retries and
    /// thirty seconds in all — are a web service's budget, not a model's. An
    /// answer regularly runs past ten seconds, every retry re-sends the whole
    /// prompt, and the thirty-second cap is the timeout that stopped the Home
    /// page. This client's own budget is one long attempt.</summary>
    [Fact]
    public void The_chat_client_pipeline_is_sized_for_a_chat_completion()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new AzureFoundrySettingsStore(NewSettingsPath()));
        services.AddAzureFoundryChatClient();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get($"{AzureFoundryHttpClients.Chat}-standard");

        Assert.Equal(1, options.Retry.MaxRetryAttempts);
        Assert.Equal(AzureFoundryRegistration.AttemptTimeout, options.AttemptTimeout.Timeout);
        Assert.Equal(AzureFoundryRegistration.TotalTimeout, options.TotalRequestTimeout.Timeout);
        Assert.True(options.TotalRequestTimeout.Timeout >= options.AttemptTimeout.Timeout);
        Assert.True(options.CircuitBreaker.SamplingDuration >= options.AttemptTimeout.Timeout * 2);
        Assert.True(options.AttemptTimeout.Timeout >= TimeSpan.FromSeconds(60));
    }

    /// <summary>A retry is for a refusal that cost nothing — a connection that
    /// never opened, a 429, a 503. An attempt that timed out already spent the
    /// prompt once and the reader's patience with it; sending it again would
    /// double both, so the timeout is the one transient failure the retry
    /// leaves alone.</summary>
    [Fact]
    public async Task The_chat_client_retries_a_refusal_but_not_a_timed_out_attempt()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new AzureFoundrySettingsStore(NewSettingsPath()));
        services.AddAzureFoundryChatClient();

        using var provider = services.BuildServiceProvider();
        var retry = provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get($"{AzureFoundryHttpClients.Chat}-standard")
            .Retry;

        Assert.False(await ShouldRetry(retry, Outcome.FromException<HttpResponseMessage>(new TimeoutRejectedException())));
        Assert.True(await ShouldRetry(retry, Outcome.FromException<HttpResponseMessage>(new HttpRequestException("refused"))));
        using var tooMany = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        Assert.True(await ShouldRetry(retry, Outcome.FromResult(tooMany)));
        using var unavailable = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        Assert.True(await ShouldRetry(retry, Outcome.FromResult(unavailable)));
        using var badRequest = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.False(await ShouldRetry(retry, Outcome.FromResult(badRequest)));
    }

    /// <summary>Both hosts call <c>AddServiceDefaults()</c>, which puts the
    /// standard pipeline on every client. This project's own must replace that
    /// one rather than sit inside it — two pipelines would be the outer one's
    /// thirty seconds over the inner one's minutes, which is the crash again.</summary>
    [Fact]
    public void The_chat_client_carries_one_resilience_handler_even_under_the_hosts_defaults()
    {
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        services.AddSingleton(new AzureFoundrySettingsStore(NewSettingsPath()));
        services.AddAzureFoundryChatClient();

        using var provider = services.BuildServiceProvider();
        var chain = HandlerChain(provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(AzureFoundryHttpClients.Chat));

        Assert.Single(chain.OfType<ResilienceHandler>());
        Assert.IsType<AzureFoundryChatClient>(provider.GetRequiredService<IAzureFoundryChatClient>());
    }

    private static ValueTask<bool> ShouldRetry(HttpRetryStrategyOptions retry, Outcome<HttpResponseMessage> outcome) =>
        retry.ShouldHandle(new RetryPredicateArguments<HttpResponseMessage>(ResilienceContextPool.Shared.Get(), outcome, 0));

    /// <summary>The handlers from the outermost to the primary handler last —
    /// walked the way a request travels.</summary>
    private static List<HttpMessageHandler> HandlerChain(HttpMessageHandler handler)
    {
        var chain = new List<HttpMessageHandler>();

        for (var current = handler; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
        {
            chain.Add(current);
        }

        return chain;
    }

    private static string NewSettingsPath() =>
        Path.Combine(Path.GetTempPath(), "backlog-foundry-registration", Guid.NewGuid().ToString("n"), "azure-foundry.json");
}
