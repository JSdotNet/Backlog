using System.Net;

using Backlog.Infrastructure.Sync.Sessions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The transport half of session replication: the two routes it calls, the query
/// it builds, and what it does with an answer that is not a success. Driven
/// through a real <see cref="HttpClient"/> over a scripted wire, the same way
/// <see cref="TaskSyncClientTests"/> is, so what is asserted is the request that
/// would actually have gone out.
/// </summary>
public sealed class SessionSyncClientTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Pushing_posts_the_batch_to_the_sessions_route()
    {
        using var fixture = Fixture.Create((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":2}"""));

        var result = await fixture.Client.PushAsync(
            [Record("first"), Record("second")],
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Accepted);
        Assert.Equal(HttpMethod.Post, fixture.Handler.Requests[0].Method);
        Assert.Equal("/api/sync/sessions", fixture.Handler.Requests[0].Path);
        Assert.Contains("\"first\"", fixture.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pulling_asks_the_sessions_route_for_a_page()
    {
        using var fixture = Fixture.Create((_, _) => StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"sessions":[],"since":"cursor-1","hasMore":false}"""));

        var result = await fixture.Client.PullAsync(since: null, maxItems: 25, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("cursor-1", result.Value.Since);
        Assert.False(result.Value.HasMore);
        Assert.Equal(HttpMethod.Get, fixture.Handler.Requests[0].Method);
        Assert.Equal("maxItems=25", fixture.LastQuery.TrimStart('?'));
    }

    /// <summary>
    /// The cursor is base64 of a signed payload, so <c>+</c> and <c>=</c> are
    /// ordinary characters in it. Sent raw, a <c>+</c> arrives as a space and the
    /// service rejects a signature it minted itself.
    /// </summary>
    [Fact]
    public async Task A_cursor_travels_escaped()
    {
        using var fixture = Fixture.Create((_, _) => StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"sessions":[],"since":"next","hasMore":false}"""));

        await fixture.Client.PullAsync("v1.aGVsbG8+d29ybGQ=", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("since=v1.aGVsbG8%2Bd29ybGQ%3D", fixture.LastQuery, StringComparison.Ordinal);
    }

    /// <summary>
    /// The session codes are the service's own, and the client has to pass them
    /// through rather than fold them into "something went wrong": a batch too
    /// large tells a client to send fewer, and an invalid record tells it the
    /// batch will fail again on every run until something changes.
    /// </summary>
    [Theory]
    [InlineData(SyncErrorCodes.SessionBatchTooLarge, HttpStatusCode.BadRequest)]
    [InlineData(SyncErrorCodes.SessionInvalid, HttpStatusCode.BadRequest)]
    [InlineData(SyncErrorCodes.SessionTooLarge, HttpStatusCode.RequestEntityTooLarge)]
    public async Task A_refused_batch_comes_back_as_its_problem_code(string code, HttpStatusCode status)
    {
        using var fixture = Fixture.Create((_, _) => StubHttpMessageHandler.Problem(status, code, "No."));

        var result = await fixture.Client.PushAsync([Record("first")], TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
    }

    [Fact]
    public async Task An_expired_cursor_comes_back_as_its_problem_code_and_detail()
    {
        using var fixture = Fixture.Create((_, _) => StubHttpMessageHandler.Problem(
            HttpStatusCode.BadRequest,
            SyncErrorCodes.SyncCursorExpired,
            "Start again from the beginning."));

        var result = await fixture.Client.PullAsync("v1.stale", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SyncErrorCodes.SyncCursorExpired, result.Error.Code);
        Assert.Equal("Start again from the beginning.", result.Error.Message);
    }

    /// <summary>
    /// A service that is not there is still not an exception, and it is filed under
    /// a code of the client's own rather than one of <see cref="SyncErrorCodes"/> —
    /// nobody issued it.
    /// </summary>
    [Fact]
    public async Task A_service_that_cannot_be_reached_reports_itself_rather_than_throwing()
    {
        using var fixture = Fixture.Create((_, _) => throw new HttpRequestException("No route to host."));

        var push = await fixture.Client.PushAsync([Record("first")], TestContext.Current.CancellationToken);
        var pull = await fixture.Client.PullAsync(null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(push.IsFailure);
        Assert.Equal(DevicePairingClient.UnreachableCode, push.Error.Code);
        Assert.True(pull.IsFailure);
        Assert.Equal(DevicePairingClient.UnreachableCode, pull.Error.Code);
    }

    private static SessionRecord Record(string id) =>
        new(id, "claude", "Workshop PC", "backlog", "main", null, Noon, 3, 0);

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;
        private readonly List<string> _bodies;
        private readonly List<string> _queries;

        private Fixture(
            HttpClient http,
            StubHttpMessageHandler handler,
            SessionSyncClient client,
            List<string> bodies,
            List<string> queries)
        {
            _http = http;
            _bodies = bodies;
            _queries = queries;
            Handler = handler;
            Client = client;
        }

        public StubHttpMessageHandler Handler { get; }

        public SessionSyncClient Client { get; }

        public string LastBody => _bodies.Count == 0 ? string.Empty : _bodies[^1];

        /// <summary>The query string of the last request. Captured here rather than
        /// read off the recorded request, which keeps only the path.</summary>
        public string LastQuery => _queries.Count == 0 ? string.Empty : _queries[^1];

        public static Fixture Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
        {
            var bodies = new List<string>();
            var queries = new List<string>();

            var handler = new StubHttpMessageHandler((request, index) =>
            {
                queries.Add(request.RequestUri?.Query ?? string.Empty);

                if (request.Content is not null)
                {
                    bodies.Add(request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult());
                }

                return respond(request, index);
            });

            var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };

            return new Fixture(http, handler, new SessionSyncClient(http), bodies, queries);
        }

        public void Dispose() => _http.Dispose();
    }
}
