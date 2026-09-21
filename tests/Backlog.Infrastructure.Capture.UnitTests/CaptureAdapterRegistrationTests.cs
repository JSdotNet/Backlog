using Backlog.Infrastructure.Capture.Extensions;
using Backlog.Infrastructure.Capture.Inbox;
using Backlog.Infrastructure.Capture.Website;
using Backlog.Infrastructure.Capture.YouTube;
using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.Extensions;
using Backlog.Modules.Capture.Ports;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.Capture.UnitTests;

/// <summary>
/// What a host gets for calling <c>AddCaptureAdapters()</c> beside the module,
/// asserted the way a host finds out: by building the provider with the
/// validation the generic host turns on in Development, and resolving the run
/// inside a scope the way the shell does.
/// </summary>
public sealed class CaptureAdapterRegistrationTests
{
    [Fact]
    public void The_run_composes_with_both_adapters_and_the_inbox_delivery()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICaptureSourceSettings>(new NoSettings());
        services.AddSingleton<ICaptureRunLog>(new NoLog());
        services.AddSingleton<IInboxIntake>(new NoIntake());
        services.AddCaptureModule();
        services.AddCaptureAdapters();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICaptureRunner>());
        Assert.IsType<InboxCaptureDelivery>(scope.ServiceProvider.GetRequiredService<ICaptureDelivery>());

        var adapters = scope.ServiceProvider.GetServices<ICaptureSourceAdapter>().ToList();
        Assert.Contains(adapters, adapter => adapter is YouTubeChannelAdapter);
        Assert.Contains(adapters, adapter => adapter is WebsiteFeedAdapter);
    }

    /// <summary>A run happens on the reader's machine when they press the
    /// button, so a feed that never answers must not be a pane that never
    /// answers (inherited ADR 0015).</summary>
    [Fact]
    public void The_feed_client_has_a_timeout_and_says_who_is_asking()
    {
        var services = new ServiceCollection();
        services.AddCaptureAdapters();

        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(CaptureHttpClients.Feeds);

        Assert.Equal(TimeSpan.FromSeconds(15), client.Timeout);
        Assert.NotEmpty(client.DefaultRequestHeaders.UserAgent);
    }

    /// <summary>The standard pipeline's defaults are three retries with
    /// backoff inside a thirty-second budget — right for a service, wrong for
    /// a button press. The options are read back by the name the handler
    /// registers them under, which is the client's name and <c>-standard</c>.</summary>
    [Fact]
    public void The_feed_client_retries_once_and_gives_up_within_fifteen_seconds()
    {
        var services = new ServiceCollection();
        services.AddCaptureAdapters();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get($"{CaptureHttpClients.Feeds}-standard");

        Assert.Equal(1, options.Retry.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(10), options.AttemptTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(15), options.TotalRequestTimeout.Timeout);
    }

    /// <summary>Both hosts call <c>AddServiceDefaults()</c>, which puts the
    /// standard pipeline on every client. This project's own must replace
    /// that one rather than sit inside it — two pipelines would be the outer
    /// one's three retries over the inner one's one.</summary>
    [Fact]
    public void The_feed_client_carries_one_resilience_handler_even_under_the_hosts_defaults()
    {
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        services.AddCaptureAdapters();

        using var provider = services.BuildServiceProvider();
        var chain = HandlerChain(provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(CaptureHttpClients.Feeds));

        Assert.Single(chain.OfType<ResilienceHandler>());
    }

    /// <summary>A consent cookie YouTube sets on the page fetch must not ride
    /// along on the next request to anyone, and a redirect loop must not be
    /// a fifty-hop loop.</summary>
    [Fact]
    public void The_feed_client_keeps_no_cookies_and_follows_a_bounded_number_of_redirects()
    {
        var services = new ServiceCollection();
        services.AddCaptureAdapters();

        using var provider = services.BuildServiceProvider();
        var chain = HandlerChain(provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(CaptureHttpClients.Feeds));

        var primary = Assert.IsType<SocketsHttpHandler>(chain[^1]);
        Assert.False(primary.UseCookies);
        Assert.True(primary.AllowAutoRedirect);
        Assert.Equal(5, primary.MaxAutomaticRedirections);
    }

    /// <summary>The factory's handler, the additional handlers in order, and
    /// the primary handler last — walked the way a request travels.</summary>
    private static List<HttpMessageHandler> HandlerChain(HttpMessageHandler handler)
    {
        var chain = new List<HttpMessageHandler>();

        for (var current = handler; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
        {
            chain.Add(current);
        }

        return chain;
    }

    private sealed class NoSettings : ICaptureSourceSettings
    {
        public event Action? Changed { add { } remove { } }

        public CaptureSourceSettings Current { get; } = new();

        public string SettingsPath => "memory";

        public string? SetEnabled(CaptureSourceKind kind, bool enabled) => null;

        public string? SetTargets(CaptureSourceKind kind, IReadOnlyList<string> targets) => null;
    }

    /// <summary>The run log, the host's fourth port: a run is written to it,
    /// and nothing is ever read back here.</summary>
    private sealed class NoLog : ICaptureRunLog
    {
        public event Action? Changed { add { } remove { } }

        public CaptureRunLogEntry? LastRunFor(CaptureSourceKind kind) => null;

        public IReadOnlyList<CaptureRunLogEntry> EntriesFor(CaptureSourceKind kind) => [];

        public void Record(CaptureRunResultDto run)
        {
        }
    }

    private sealed class NoIntake : IInboxIntake
    {
        public Task<InboxIntakeOutcome> ReceiveAsync(InboxCaptureDto capture, CancellationToken cancellationToken = default) =>
            Task.FromResult(InboxIntakeOutcome.Ignored);
    }
}
