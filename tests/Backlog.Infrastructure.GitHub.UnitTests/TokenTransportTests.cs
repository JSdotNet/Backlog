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
