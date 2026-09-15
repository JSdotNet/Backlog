using System.Net;
using System.Text;
using Backlog.Infrastructure.Claude;

namespace Backlog.Infrastructure.Claude.UnitTests;

public sealed class ClaudeAdminTransportTests
{
    [Fact]
    public async Task Requests_use_the_configured_endpoint_root()
    {
        var account = new ClaudeAccount
        {
            AdminApiKey = "sk-ant-admin01-example",
            ApiEndpoint = "https://claude.example.internal/admin/"
        };

        var handler = new RecordingHandler();
        var transport = new ClaudeAdminTransport(new HttpClient(handler));

        await transport.SendAsync(account, HttpMethod.Get, "v1/organizations/usage_report/messages", TestContext.Current.CancellationToken);

        Assert.Equal(
            "https://claude.example.internal/admin/v1/organizations/usage_report/messages",
            handler.Request!.RequestUri!.ToString());
    }

    /// <summary>
    /// A key without the admin prefix reaches Anthropic instead of being turned away here.
    /// A personal Console key that is not scoped to a workspace is a documented credential
    /// for these reports, and it is indistinguishable from a workspace-scoped one by its
    /// text alone — so the judgement belongs to the server, which answers 401 or 403 with
    /// an explanation this transport already translates.
    /// </summary>
    [Fact]
    public async Task A_key_without_the_admin_prefix_still_reaches_anthropic()
    {
        var account = new ClaudeAccount { AdminApiKey = "sk-ant-api03-personal-key" };

        var handler = new RecordingHandler();
        var transport = new ClaudeAdminTransport(new HttpClient(handler));

        await transport.SendAsync(account, HttpMethod.Get, "v1/organizations/usage_report/messages", TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("sk-ant-api03-personal-key", handler.Request!.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task Invalid_endpoint_is_rejected_before_any_http_call()
    {
        var account = new ClaudeAccount { AdminApiKey = "sk-ant-admin01-example", ApiEndpoint = "not-a-url" };

        var handler = new RecordingHandler();
        var transport = new ClaudeAdminTransport(new HttpClient(handler));

        await Assert.ThrowsAsync<ClaudeNotConfiguredException>(() =>
            transport.SendAsync(account, HttpMethod.Get, "v1/organizations/usage_report/messages", TestContext.Current.CancellationToken));

        Assert.Equal(0, handler.RequestCount);
    }

    /// <summary>
    /// Two accounts, one transport. The endpoint and the key that reach the wire are
    /// the ones from the account each call named — which is the whole reason the
    /// account travels with the call instead of living on the transport.
    /// </summary>
    [Fact]
    public async Task Each_call_goes_to_the_endpoint_with_the_key_of_the_account_it_named()
    {
        var personal = new ClaudeAccount { AdminApiKey = "sk-ant-admin01-personal" };
        var work = new ClaudeAccount
        {
            AdminApiKey = "sk-ant-admin01-work",
            ApiEndpoint = "https://claude.employer.example"
        };

        var handler = new RecordingHandler();
        var transport = new ClaudeAdminTransport(new HttpClient(handler));

        await transport.SendAsync(personal, HttpMethod.Get, "v1/organizations/cost_report", TestContext.Current.CancellationToken);
        var first = (handler.Request!.RequestUri!.ToString(), handler.Request.Headers.GetValues("x-api-key").Single());

        await transport.SendAsync(work, HttpMethod.Get, "v1/organizations/cost_report", TestContext.Current.CancellationToken);
        var second = (handler.Request!.RequestUri!.ToString(), handler.Request.Headers.GetValues("x-api-key").Single());

        Assert.Equal(("https://api.anthropic.com/v1/organizations/cost_report", "sk-ant-admin01-personal"), first);
        Assert.Equal(("https://claude.employer.example/v1/organizations/cost_report", "sk-ant-admin01-work"), second);
    }

    [Fact]
    public async Task An_account_without_a_key_is_refused_before_any_http_call()
    {
        var handler = new RecordingHandler();
        var transport = new ClaudeAdminTransport(new HttpClient(handler));

        await Assert.ThrowsAsync<ClaudeNotConfiguredException>(() =>
            transport.SendAsync(new ClaudeAccount(), HttpMethod.Get, "v1/organizations/cost_report", TestContext.Current.CancellationToken));

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
