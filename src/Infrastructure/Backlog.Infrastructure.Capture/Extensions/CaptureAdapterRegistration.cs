using System.Net.Http.Headers;

using Backlog.Infrastructure.Capture.Feeds;
using Backlog.Infrastructure.Capture.Inbox;
using Backlog.Infrastructure.Capture.Website;
using Backlog.Infrastructure.Capture.YouTube;
using Backlog.Modules.Capture.Ports;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Capture.Extensions;

/// <summary>
/// The adapters behind the Capture module's two ports, registered in one place
/// both hosts call so the lifetimes cannot drift between the desktop app and
/// the web harness.
/// <para>
/// Scoped, because the run's handler is scoped and resolves all of them per
/// run. None of them holds state — the feed client comes from the factory per
/// fetch, and the delivery is a translation over the Inbox's transient intake
/// — so nothing here needs to outlive a run, and nothing here is a captive of
/// anything shorter-lived.
/// </para>
/// <para>
/// The feed client is the one this project fetches everything through, and it
/// is where the timeout lives (inherited ADR 0015). The host's
/// <c>AddServiceDefaults()</c> puts the standard resilience pipeline on every
/// client with the defaults meant for a service talking to a service: three
/// retries with exponential backoff inside a thirty-second budget. A button
/// press cannot wait through three retries of a 503, so this client takes
/// that pipeline off and puts its own on — one retry, ten seconds an attempt,
/// fifteen in all — and the client's fifteen seconds is the cap over all of
/// that, chosen for a run that happens while a person watches the pane. Its
/// own rather than a second one beside the host's, because two pipelines
/// would be the outer one's three retries over the inner one's one.
/// </para>
/// <para>
/// The primary handler is this project's too. No cookie jar: the one fetch
/// that carries a cookie carries it as a request header, and the
/// <c>Set-Cookie</c> YouTube answers with must not be replayed to the next
/// host the client is pointed at. Redirects are followed, because a feed
/// moves, but five deep and no further.
/// </para>
/// </summary>
public static class CaptureAdapterRegistration
{
    /// <summary>What a feed sees asking. A name rather than the runtime's
    /// default, because some hosts answer an anonymous client with a
    /// challenge page and the reader would see "no feed found".</summary>
    private const string UserAgent = "Backlog/1.0 (+https://github.com/JSdotNet/Backlog)";

    /// <summary>
    /// Registers the YouTube and Website source adapters and the Inbox delivery.
    /// Call after <c>AddCaptureModule()</c> and <c>AddInboxModule()</c>: the
    /// first declares the ports these answer, and the second supplies the
    /// intake the delivery captures.
    /// </summary>
    public static IServiceCollection AddCaptureAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var feeds = services.AddHttpClient(CaptureHttpClients.Feeds)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                UseCookies = false,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5
            });

        // The experimental attribute on RemoveAllResilienceHandlers is a
        // warning about the API's shape, not its behaviour; it is the one
        // published way to take the host's default pipeline off a client.
#pragma warning disable EXTEXP0001
        feeds.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        feeds.AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 1;
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
        });

        // After the resilience handler, which sets the client's timeout to
        // infinite so its own strategies can own the clock. The client's
        // timeout is wanted back: it is what bounds the body read, which the
        // pipeline stops watching once the headers are in (see FeedFetcher).
        feeds.ConfigureHttpClient(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/atom+xml"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/rss+xml"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml", 0.9));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html", 0.8));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.5));
        });

        services.AddScoped<FeedFetcher>();
        services.AddScoped<ICaptureSourceAdapter, YouTubeChannelAdapter>();
        services.AddScoped<ICaptureSourceAdapter, WebsiteFeedAdapter>();
        services.AddScoped<ICaptureDelivery, InboxCaptureDelivery>();

        return services;
    }
}
