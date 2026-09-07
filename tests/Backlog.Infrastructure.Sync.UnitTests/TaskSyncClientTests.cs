using System.Net;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The transport half of task replication: the two routes it calls, the query it
/// builds, and what it does with an answer that is not a success. Driven through
/// a real <see cref="HttpClient"/> over a scripted wire, the same way
/// <see cref="DevicePairingClientTests"/> is, so what is asserted is the request
/// that would actually have gone out.
/// </summary>
public sealed class TaskSyncClientTests
{
    [Fact]
    public async Task Pushing_posts_the_batch_to_the_tasks_route()
    {
        using var fixture = Fixture.Create((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":2}"""));

        var result = await fixture.Client.PushAsync(
            [Change("First"), Change("Second")],
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Accepted);
        Assert.Equal(HttpMethod.Post, fixture.Handler.Requests[0].Method);
        Assert.Equal("/api/sync/tasks", fixture.Handler.Requests[0].Path);
        Assert.Contains("\"First\"", fixture.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pulling_asks_the_tasks_route_for_a_page()
    {
        using var fixture = Fixture.Create((_, _) => StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"tasks":[],"since":"cursor-1","hasMore":false}"""));

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
            """{"tasks":[],"since":"next","hasMore":false}"""));

        await fixture.Client.PullAsync("v1.aGVsbG8+d29ybGQ=", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("since=v1.aGVsbG8%2Bd29ybGQ%3D", fixture.LastQuery, StringComparison.Ordinal);
    }

    /// <summary>
    /// A cursor the store no longer resumes from is an ordinary outcome of a
    /// device that has been off for a month, and the caller has to be able to
    /// tell it from a cursor that was never ours. It comes back as its problem
    /// code rather than as an exception.
    /// </summary>
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

    [Fact]
    public async Task A_cursor_belonging_to_somebody_else_is_told_apart_from_an_expired_one()
    {
        using var fixture = Fixture.Create((_, _) => StubHttpMessageHandler.Problem(
            HttpStatusCode.Forbidden,
            SyncErrorCodes.SyncCursorNotYours,
            "That cursor is not yours."));

        var result = await fixture.Client.PullAsync("v1.someone-else", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(SyncErrorCodes.SyncCursorNotYours, result.Error.Code);
    }

    /// <summary>
    /// A service that is not there is still not an exception, and it is filed
    /// under a code of the client's own rather than one of
    /// <see cref="SyncErrorCodes"/> — nobody issued it.
    /// </summary>
    [Fact]
    public async Task A_service_that_cannot_be_reached_reports_itself_rather_than_throwing()
    {
        using var fixture = Fixture.Create((_, _) => throw new HttpRequestException("No route to host."));

        var push = await fixture.Client.PushAsync([Change("First")], TestContext.Current.CancellationToken);
        var pull = await fixture.Client.PullAsync(null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(push.IsFailure);
        Assert.Equal(DevicePairingClient.UnreachableCode, push.Error.Code);
        Assert.True(pull.IsFailure);
        Assert.Equal(DevicePairingClient.UnreachableCode, pull.Error.Code);
    }

    private static TaskChange Change(string title) =>
        TaskChanges.Change(title, DateTimeOffset.UtcNow);

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;
        private readonly List<string> _bodies;
        private readonly List<string> _queries;

        private Fixture(HttpClient http, StubHttpMessageHandler handler, TaskSyncClient client, List<string> bodies, List<string> queries)
        {
            _http = http;
            _bodies = bodies;
            _queries = queries;
            Handler = handler;
            Client = client;
        }

        public StubHttpMessageHandler Handler { get; }

        public TaskSyncClient Client { get; }

        public string LastBody => _bodies.Count == 0 ? string.Empty : _bodies[^1];

        /// <summary>The query string of the last request. Captured here rather
        /// than read off the recorded request, which keeps only the path.</summary>
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

            return new Fixture(http, handler, new TaskSyncClient(http), bodies, queries);
        }

        public void Dispose() => _http.Dispose();
    }
}
