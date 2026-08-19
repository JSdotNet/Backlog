using System.Net;
using System.Text;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

public sealed class TokenTransportTests
{
    [Fact]
    public async Task Requests_use_the_configured_endpoint_root()
    {
        var handler = new RecordingHandler();
        var transport = new TokenTransport(
            _ => "ghp_example",
            () => "https://ghe.example.internal/api/v3/",
            new HttpClient(handler));

        await transport.SendAsync(HttpMethod.Get, "orgs/acme/copilot/billing/seats");

        Assert.Equal(
            "https://ghe.example.internal/api/v3/orgs/acme/copilot/billing/seats",
            handler.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Invalid_endpoint_is_rejected_before_any_http_call()
    {
        var handler = new RecordingHandler();
        var transport = new TokenTransport(_ => "ghp_example", () => "not-a-url", new HttpClient(handler));

        await Assert.ThrowsAsync<GitHubNotConfiguredException>(() =>
            transport.SendAsync(HttpMethod.Get, "orgs/acme/copilot/billing/seats"));

        Assert.Equal(0, handler.RequestCount);
    }

    /// <summary>
    /// The version travels per request rather than being pinned on the client, because
    /// GitHub does not move its endpoints to a new version together: the billing usage
    /// reports only exist from 2026-03-10 while everything else this app calls is
    /// documented against 2022-11-28.
    /// </summary>
    [Fact]
    public async Task A_caller_that_asks_for_an_api_version_gets_it()
    {
        var handler = new RecordingHandler();
        var transport = new TokenTransport(_ => "ghp_example", http: new HttpClient(handler));

        await transport.SendAsync(
            HttpMethod.Get,
            "users/jsdotnet/settings/billing/ai_credit/usage",
            apiVersion: "2026-03-10");

        Assert.Equal(
            "2026-03-10",
            Assert.Single(handler.Request!.Headers.GetValues("X-GitHub-Api-Version")));
    }

    [Fact]
    public async Task A_caller_that_asks_for_nothing_gets_the_version_the_rest_of_the_app_uses()
    {
        var handler = new RecordingHandler();
        var transport = new TokenTransport(_ => "ghp_example", http: new HttpClient(handler));

        await transport.SendAsync(HttpMethod.Get, "orgs/acme/copilot/billing/seats");

        Assert.Equal(
            IGitHubTransport.DefaultApiVersion,
            Assert.Single(handler.Request!.Headers.GetValues("X-GitHub-Api-Version")));
    }

    /// <summary>
    /// One header, not two. The version used to be a default header on the client, and
    /// leaving that in place alongside the per-request one would send both values and
    /// let GitHub pick.
    /// </summary>
    [Fact]
    public async Task The_api_version_is_sent_once()
    {
        var handler = new RecordingHandler();
        var transport = new TokenTransport(_ => "ghp_example", http: new HttpClient(handler));

        await transport.SendAsync(HttpMethod.Get, "orgs/acme/copilot/billing/seats", apiVersion: "2026-03-10");

        Assert.Single(handler.Request!.Headers.GetValues("X-GitHub-Api-Version"));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
