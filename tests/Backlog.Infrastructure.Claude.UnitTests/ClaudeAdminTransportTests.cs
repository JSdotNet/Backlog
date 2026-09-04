using System.Net;
using System.Text;
using Backlog.Infrastructure.Claude;

namespace Backlog.Infrastructure.Claude.UnitTests;

public sealed class ClaudeAdminTransportTests
{
    [Fact]
    public async Task Requests_use_the_configured_endpoint_root()
    {
        using var directory = new TemporaryDirectory();
        var store = new ClaudeSettingsStore(directory.File("claude.json"));
        store.SetAdminApiKey("sk-ant-admin01-example");
        store.SetApiEndpoint("https://claude.example.internal/admin/");

        var handler = new RecordingHandler();
        var transport = new ClaudeAdminTransport(new HttpClient(handler), store);

        await transport.SendAsync(HttpMethod.Get, "v1/organizations/usage_report/messages");

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
        using var directory = new TemporaryDirectory();
        var store = new ClaudeSettingsStore(directory.File("claude.json"));
        store.SetAdminApiKey("sk-ant-api03-personal-key");

        var handler = new RecordingHandler();
        var transport = new ClaudeAdminTransport(new HttpClient(handler), store);

        await transport.SendAsync(HttpMethod.Get, "v1/organizations/usage_report/messages");

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("sk-ant-api03-personal-key", handler.Request!.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task Invalid_endpoint_is_rejected_before_any_http_call()
    {
        using var directory = new TemporaryDirectory();
        var store = new ClaudeSettingsStore(directory.File("claude.json"));
        store.SetAdminApiKey("sk-ant-admin01-example");
        store.SetApiEndpoint("not-a-url");

        var handler = new RecordingHandler();
        var transport = new ClaudeAdminTransport(new HttpClient(handler), store);

        await Assert.ThrowsAsync<ClaudeNotConfiguredException>(() =>
            transport.SendAsync(HttpMethod.Get, "v1/organizations/usage_report/messages"));

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
