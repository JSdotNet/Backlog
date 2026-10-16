using System.Net;
using System.Text;

using Backlog.Mobile.UI.AppUpdate;
using Backlog.UI.Components.AppUpdate;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// Asking GitHub whether a newer APK than the installed one is published. The
/// answer is always one of Available, UpToDate or Failed — offline, a refused
/// request, an unreadable answer and a ten-second wait all end as Failed rather
/// than as an exception the shell would have to catch.
/// </summary>
public sealed class AppUpdateCheckTests
{
    private const long Installed_0_1_59 = 100059L;

    private static readonly ApkUpdateChannel Channel = new("JSdotNet/Backlog");

    private static string FeedWith(params string[] versions) =>
        "[" + string.Join(",", versions.Select(version =>
            $$"""{"tag_name":"mobile-v{{version}}","draft":false,"assets":[{"name":"Backlog.Mobile_{{version}}.apk","state":"uploaded","size":31457280,"browser_download_url":"https://github.com/JSdotNet/Backlog/releases/download/mobile-v{{version}}/Backlog.Mobile_{{version}}.apk"}]}""")) + "]";

    private static GitHubApkUpdateCheck CheckOver(ScriptedGitHub github, ApkUpdateChannel? channel = null, TimeProvider? time = null) =>
        new(new HttpClient(github), channel ?? Channel, time ?? TimeProvider.System, NullLogger<GitHubApkUpdateCheck>.Instance);

    [Fact]
    public async Task A_newer_apk_is_offered_by_its_version()
    {
        var github = ScriptedGitHub.Answering(FeedWith("0.1.58", "0.1.60", "0.1.59"));

        var check = await CheckOver(github).CheckAsync(Installed_0_1_59, TestContext.Current.CancellationToken);

        Assert.Equal(AppUpdateAvailability.Available, check.Result.Availability);
        Assert.Equal("0.1.60", check.Result.AvailableVersion);
        Assert.Equal("Version 0.1.60 is available.", check.Result.Message);
        Assert.Equal(
            new Uri("https://github.com/JSdotNet/Backlog/releases/download/mobile-v0.1.60/Backlog.Mobile_0.1.60.apk"),
            check.Release?.DownloadUrl);
    }

    [Theory]
    [InlineData("0.1.59")]
    [InlineData("0.1.12")]
    public async Task The_installed_version_or_an_older_one_is_up_to_date(string newestPublished)
    {
        var github = ScriptedGitHub.Answering(FeedWith(newestPublished));

        var check = await CheckOver(github).CheckAsync(Installed_0_1_59, TestContext.Current.CancellationToken);

        Assert.Equal(AppUpdateAvailability.UpToDate, check.Result.Availability);
        Assert.Null(check.Release);
    }

    [Fact]
    public async Task No_apk_published_at_all_is_up_to_date()
    {
        var github = ScriptedGitHub.Answering("[]");

        var check = await CheckOver(github).CheckAsync(Installed_0_1_59, TestContext.Current.CancellationToken);

        Assert.Equal(AppUpdateAvailability.UpToDate, check.Result.Availability);
    }

    [Fact]
    public async Task The_request_lists_the_channels_releases_as_github_asks_to_be_called()
    {
        var github = ScriptedGitHub.Answering("[]");

        await CheckOver(github).CheckAsync(Installed_0_1_59, TestContext.Current.CancellationToken);

        var request = Assert.Single(github.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.github.com/repos/JSdotNet/Backlog/releases?per_page=100", request.Uri.AbsoluteUri);
        Assert.Contains("application/vnd.github+json", request.Accept);
        Assert.False(string.IsNullOrWhiteSpace(request.UserAgent));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_refused_request_fails_the_check(HttpStatusCode status)
    {
        var github = ScriptedGitHub.Answering(status, """{"message":"API rate limit exceeded"}""");

        var check = await CheckOver(github).CheckAsync(Installed_0_1_59, TestContext.Current.CancellationToken);

        Assert.Equal(AppUpdateAvailability.Failed, check.Result.Availability);
        Assert.Null(check.Release);
    }

    [Fact]
    public async Task An_unreadable_answer_fails_the_check()
    {
        var github = ScriptedGitHub.Answering("<html>captive portal</html>");

        var check = await CheckOver(github).CheckAsync(Installed_0_1_59, TestContext.Current.CancellationToken);

        Assert.Equal(AppUpdateAvailability.Failed, check.Result.Availability);
    }

    [Fact]
    public async Task Being_offline_fails_the_check()
    {
        var github = ScriptedGitHub.Offline();

        var check = await CheckOver(github).CheckAsync(Installed_0_1_59, TestContext.Current.CancellationToken);

        Assert.Equal(AppUpdateAvailability.Failed, check.Result.Availability);
        Assert.Contains("connection", check.Result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_check_github_does_not_answer_within_ten_seconds_fails()
    {
        var time = new FakeTimeProvider();
        var github = ScriptedGitHub.Hanging();

        var pending = CheckOver(github, time: time).CheckAsync(Installed_0_1_59, TestContext.Current.CancellationToken);
        await github.Asked.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        time.Advance(TimeSpan.FromSeconds(9));
        Assert.False(pending.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(1));
        var check = await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(AppUpdateAvailability.Failed, check.Result.Availability);
    }

    [Fact]
    public async Task A_build_without_a_channel_never_asks_and_is_unsupported()
    {
        var github = ScriptedGitHub.Answering(FeedWith("9.0.0"));

        var check = await CheckOver(github, channel: ApkUpdateChannel.None).CheckAsync(Installed_0_1_59, TestContext.Current.CancellationToken);

        Assert.Equal(AppUpdateAvailability.Unsupported, check.Result.Availability);
        Assert.Empty(github.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Only_an_empty_channel_is_no_channel(string? repository)
    {
        Assert.False(new ApkUpdateChannel(repository).IsConfigured);
    }

    [Fact]
    public void A_repository_is_a_channel()
    {
        Assert.True(new ApkUpdateChannel(" JSdotNet/Backlog ").IsConfigured);
    }

    internal sealed record SentRequest(HttpMethod Method, Uri Uri, string Accept, string UserAgent);

    /// <summary>GitHub's releases endpoint, answering from a script.</summary>
    internal sealed class ScriptedGitHub : HttpMessageHandler
    {
        private readonly Func<CancellationToken, Task<HttpResponseMessage>> _answer;
        private readonly TaskCompletionSource _asked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private ScriptedGitHub(Func<CancellationToken, Task<HttpResponseMessage>> answer) => _answer = answer;

        public List<SentRequest> Requests { get; } = [];

        /// <summary>Completes once the first request has been sent.</summary>
        public Task Asked => _asked.Task;

        public static ScriptedGitHub Answering(string json) => Answering(HttpStatusCode.OK, json);

        public static ScriptedGitHub Answering(HttpStatusCode status, string body) =>
            new(_ => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            }));

        public static ScriptedGitHub Offline() =>
            new(_ => Task.FromException<HttpResponseMessage>(new HttpRequestException("No such host is known.")));

        public static ScriptedGitHub Hanging() =>
            new(async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                throw new InvalidOperationException("Unreachable.");
            });

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Accept.ToString(),
                request.Headers.UserAgent.ToString()));
            _asked.TrySetResult();

            return _answer(cancellationToken);
        }
    }
}
