namespace Backlog.Mobile.UI.UnitTests;

/// <summary>A <see cref="CloudSyncClient"/> whose every request the test answers.</summary>
internal static class TestCloudSync
{
    public static CloudSyncClient Client(Func<HttpRequestMessage, HttpResponseMessage> answer) =>
        new(new HttpClient(new Handler(answer)) { BaseAddress = new Uri("https://sync.test") });

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }
}
