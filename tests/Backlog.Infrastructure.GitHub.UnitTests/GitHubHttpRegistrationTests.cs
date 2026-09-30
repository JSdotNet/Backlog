using System.Net;
using System.Text;

using Backlog.Infrastructure.GitHub;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly.Timeout;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The token route's client as the hosts register it: the pipeline on it, and what
/// the transport makes of that pipeline giving up.
/// <para>
/// Each test composes <see cref="GitHubHttpRegistration.AddGitHubHttpClient"/> for
/// real and swaps only the primary handler, so what is asserted is the pipeline the
/// desktop app and the web harness send through. A test that needs a short clock
/// moves the budgets on that pipeline's own options rather than building another.
/// </para>
/// </summary>
public sealed class GitHubHttpRegistrationTests
{
    /// <summary>The options name <c>AddStandardResilienceHandler</c> gives the
    /// named client's pipeline.</summary>
    private const string PipelineOptions = TokenTransport.HttpClientName + "-standard";

    /// <summary>A 503 on a write does not say the write did not land, so a retried
    /// <c>POST</c> is a second issue. Writes go out once.</summary>
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task A_write_that_fails_is_sent_once(string method)
    {
        var handler = new CountingHandler(HttpStatusCode.ServiceUnavailable);
        var transport = Transport(handler, NoRetryDelay);

        await Assert.ThrowsAsync<GitHubException>(() =>
            transport.SendAsync(new HttpMethod(method), "repos/octo/demo/issues", new { title = "x" }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(1, handler.RequestCount);
    }

    /// <summary>A read is safe to send again, so a transient refusal is still
    /// retried: the pipeline is narrowed for writes, not taken away.</summary>
    [Fact]
    public async Task A_read_that_fails_is_retried()
    {
        var handler = new CountingHandler(HttpStatusCode.ServiceUnavailable);
        var transport = Transport(handler, NoRetryDelay);

        await Assert.ThrowsAsync<GitHubException>(() =>
            transport.SendAsync(HttpMethod.Get, "repos/octo/demo", cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(handler.RequestCount > 1, $"Expected a retry, saw {handler.RequestCount} request(s).");
    }

    /// <summary>When the pipeline runs out of time it throws Polly's own exception,
    /// which no caller of the transport was written for. The transport answers it as
    /// what it is — GitHub not answering — in the exception callers already
    /// handle.</summary>
    [Fact]
    public async Task A_call_the_pipeline_times_out_is_a_github_exception()
    {
        var transport = Transport(new HangingHandler(), options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(100);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMilliseconds(500);
        });

        var failed = await Assert.ThrowsAsync<GitHubException>(() =>
            transport.SendAsync(HttpMethod.Put, "repos/octo/demo/contents/backlog/backlog.db", new { content = "AA==" }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.IsType<TimeoutRejectedException>(failed.InnerException);
    }

    /// <summary>The budgets are sized for a database-sized backup <c>PUT</c>, not
    /// for the service-to-service defaults of ten seconds an attempt.</summary>
    [Fact]
    public void The_budgets_fit_a_large_transfer()
    {
        var options = new HttpStandardResilienceOptions();

        GitHubHttpRegistration.ConfigurePipeline(options);

        Assert.Equal(GitHubHttpRegistration.AttemptBudget, options.AttemptTimeout.Timeout);
        Assert.Equal(GitHubHttpRegistration.TotalBudget, options.TotalRequestTimeout.Timeout);
        Assert.True(options.AttemptTimeout.Timeout >= TimeSpan.FromSeconds(100));
    }

    /// <summary>The headers GitHub wants travel on each request, so a client handed
    /// in is sent over, not reconfigured.</summary>
    [Fact]
    public async Task The_client_it_is_given_keeps_its_own_default_headers()
    {
        var handler = new CountingHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var transport = new TokenTransport(StubCredentialResolver.WithToken(), http: client);

        await transport.SendAsync(HttpMethod.Get, "repos/octo/demo", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(client.DefaultRequestHeaders.UserAgent);
        Assert.Empty(client.DefaultRequestHeaders.Accept);
        Assert.Equal("Backlog", Assert.Single(handler.Request!.Headers.UserAgent).ToString());
        Assert.Equal("application/vnd.github+json", Assert.Single(handler.Request.Headers.Accept).MediaType);
    }

    private static void NoRetryDelay(HttpStandardResilienceOptions options) =>
        options.Retry.Delay = TimeSpan.Zero;

    private static TokenTransport Transport(HttpMessageHandler handler, Action<HttpStandardResilienceOptions> adjust)
    {
        var services = new ServiceCollection();
        services.AddGitHubHttpClient();
        services.AddHttpClient(TokenTransport.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.PostConfigure(PipelineOptions, adjust);

        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        return new TokenTransport(StubCredentialResolver.WithToken(), httpClients: factory);
    }

    private sealed class CountingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        private int _count;

        public int RequestCount => _count;

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>GitHub not answering: the request waits until it is cancelled.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }
}
