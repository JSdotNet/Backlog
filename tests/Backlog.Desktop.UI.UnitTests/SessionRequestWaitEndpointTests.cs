using System.Diagnostics;
using System.Text.Json;

using Backlog.Desktop.UI.Mcp;
using Backlog.Desktop.UI.Shell;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Backlog.Tests;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The long poll a dispatcher waits on (local ADR 0038 §4): it answers <c>200</c> with
/// the oldest Requested request the moment there is one, <c>204</c> once the hold has
/// passed without one, and <c>404</c> while <c>session-requests</c> is off. The hold
/// and the poll are shortened here; the route uses <see cref="SessionRequestWaitEndpoint.Hold"/>.
/// </summary>
public class SessionRequestWaitEndpointTests
{
    private static readonly TimeSpan ShortPoll = TimeSpan.FromMilliseconds(20);

    private static SessionRequest Entry(Guid entryId, DateTimeOffset at) =>
        SessionRequests.ForEntry(entryId, "JSdotNet/Backlog", @"D:\Repos\Backlog", at);

    [Fact]
    public void The_route_holds_a_call_for_fifty_seconds()
    {
        Assert.Equal("/session-requests/wait", SessionRequestWaitEndpoint.RoutePath);
        Assert.Equal(TimeSpan.FromSeconds(50), SessionRequestWaitEndpoint.Hold);
    }

    [Fact]
    public async Task A_waiting_request_is_answered_at_once_oldest_first_as_json_with_its_brief()
    {
        var queue = new InMemorySessionRequestStore();
        var now = DateTimeOffset.UtcNow;
        var older = queue.Add(Entry(Guid.Parse("cec5cf9f-a7c2-4d55-b487-eb1a2f9ded45"), now - TimeSpan.FromMinutes(5)));
        queue.Add(SessionRequests.ForPlan("+session-dispatch", "JSdotNet/Backlog", @"D:\Repos\Backlog", now));

        var reply = await SessionRequestWaitEndpoint.WaitAsync(queue, Features(on: true), TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.Equal(200, reply.StatusCode);
        Assert.Equal(1, queue.NextCalls);

        using var json = JsonDocument.Parse(reply.Body);
        var root = json.RootElement;
        Assert.Equal(older.Id, root.GetProperty("id").GetGuid());
        Assert.Equal("request", root.GetProperty("kind").GetString());
        Assert.Equal("entry", root.GetProperty("requestKind").GetString());
        Assert.Equal(older.EntryId, root.GetProperty("entryId").GetGuid());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("planTag").ValueKind);
        Assert.Equal("JSdotNet/Backlog", root.GetProperty("repository").GetString());
        Assert.Equal(@"D:\Repos\Backlog", root.GetProperty("cloneFolder").GetString());
        Assert.Equal("requested", root.GetProperty("state").GetString());
        Assert.Equal("/backlog-tools:run-plan-item entry `cec5cf9f-a7c2-4d55-b487-eb1a2f9ded45`:", root.GetProperty("brief").GetString());
    }

    [Fact]
    public async Task A_plan_request_carries_its_tag_and_the_execute_plan_brief()
    {
        var queue = new InMemorySessionRequestStore();
        queue.Add(SessionRequests.ForPlan("+session-dispatch", "JSdotNet/Backlog", @"D:\Repos\Backlog", DateTimeOffset.UtcNow));

        var reply = await SessionRequestWaitEndpoint.WaitAsync(queue, Features(on: true), TimeProvider.System, TestContext.Current.CancellationToken);

        using var json = JsonDocument.Parse(reply.Body);
        Assert.Equal("request", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("plan", json.RootElement.GetProperty("requestKind").GetString());
        Assert.Equal("session-dispatch", json.RootElement.GetProperty("planTag").GetString());
        Assert.Equal("/backlog-tools:execute-plan +session-dispatch", json.RootElement.GetProperty("brief").GetString());
    }

    [Fact]
    public async Task A_held_call_answers_as_soon_as_a_request_arrives_well_before_the_hold_ends()
    {
        var queue = new InMemorySessionRequestStore();
        var hold = TimeSpan.FromSeconds(30);
        var watch = Stopwatch.StartNew();

        var waiting = SessionRequestWaitEndpoint.WaitAsync(queue, Features(on: true), TimeProvider.System, TestContext.Current.CancellationToken, hold, ShortPoll);

        await Task.Delay(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted, "The call answered before any request existed.");

        var request = queue.Add(Entry(Guid.NewGuid(), DateTimeOffset.UtcNow));

        var reply = await waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        watch.Stop();

        Assert.Equal(200, reply.StatusCode);
        Assert.True(watch.Elapsed < hold / 2, $"The call took {watch.Elapsed} to answer.");

        using var json = JsonDocument.Parse(reply.Body);
        Assert.Equal(request.Id, json.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_held_call_with_nothing_requested_answers_no_content_when_the_hold_ends()
    {
        var queue = new InMemorySessionRequestStore();
        var hold = TimeSpan.FromMilliseconds(250);
        var watch = Stopwatch.StartNew();

        var reply = await SessionRequestWaitEndpoint.WaitAsync(queue, Features(on: true), TimeProvider.System, TestContext.Current.CancellationToken, hold, ShortPoll);
        watch.Stop();

        Assert.Equal(204, reply.StatusCode);
        Assert.Empty(reply.Body);
        Assert.True(watch.Elapsed >= hold - TimeSpan.FromMilliseconds(20), $"The call answered after {watch.Elapsed}, before the hold.");
        Assert.True(queue.NextCalls > 1, "The queue was asked only once while the call was held.");
    }

    [Fact]
    public async Task Claimed_and_started_requests_are_not_handed_out()
    {
        var queue = new InMemorySessionRequestStore();
        var claimed = queue.Add(Entry(Guid.NewGuid(), DateTimeOffset.UtcNow));
        var started = queue.Add(Entry(Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.True((await queue.ClaimAsync(claimed.Id, "manager-1", TestContext.Current.CancellationToken)).Succeeded);
        Assert.True((await queue.ClaimAsync(started.Id, "manager-1", TestContext.Current.CancellationToken)).Succeeded);
        Assert.True((await queue.ReportStartedAsync(started.Id, "manager-1", "session-a", TestContext.Current.CancellationToken)).Succeeded);

        var reply = await SessionRequestWaitEndpoint.WaitAsync(queue, Features(on: true), TimeProvider.System, TestContext.Current.CancellationToken, TimeSpan.FromMilliseconds(60), ShortPoll);

        Assert.Equal(204, reply.StatusCode);
    }

    /// <summary>A claim whose lease ran out reads as Requested, so the route hands the
    /// request out again.</summary>
    [Fact]
    public async Task A_claim_whose_lease_ran_out_is_handed_out_again()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));
        var queue = new InMemorySessionRequestStore(clock);
        var request = queue.Add(Entry(Guid.NewGuid(), clock.GetUtcNow()));
        Assert.True((await queue.ClaimAsync(request.Id, "manager-1", TestContext.Current.CancellationToken)).Succeeded);

        clock.Advance(SessionRequests.Lease);

        var reply = await SessionRequestWaitEndpoint.WaitAsync(queue, Features(on: true), clock, TestContext.Current.CancellationToken);

        Assert.Equal(200, reply.StatusCode);
        using var json = JsonDocument.Parse(reply.Body);
        Assert.Equal(request.Id, json.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("requested", json.RootElement.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("claimedBy").ValueKind);
    }

    /// <summary>A call held when the flag goes off answers <c>404</c> rather than handing
    /// out a request that arrives afterwards.</summary>
    [Fact]
    public async Task A_held_call_answers_not_found_once_the_flag_goes_off()
    {
        var queue = new InMemorySessionRequestStore();
        var flag = new SessionRequestFlag(on: true);

        var waiting = SessionRequestWaitEndpoint.WaitAsync(queue, flag, TimeProvider.System, TestContext.Current.CancellationToken, TimeSpan.FromSeconds(30), ShortPoll);

        flag.On = false;
        queue.Add(Entry(Guid.NewGuid(), DateTimeOffset.UtcNow));

        var reply = await waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(404, reply.StatusCode);
    }

    /// <summary>A queue that cannot be read answers <c>503</c> with a retry delay and the
    /// failure for the head to log, so a dispatcher backs off.</summary>
    [Fact]
    public async Task A_queue_that_cannot_be_read_answers_service_unavailable_with_a_retry_delay()
    {
        var reply = await SessionRequestWaitEndpoint.WaitAsync(new UnreadableQueue(), Features(on: true), TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.Equal(503, reply.StatusCode);
        Assert.Equal(SessionRequestWaitEndpoint.RetryAfterSeconds, reply.RetryAfterSeconds);
        Assert.IsType<InvalidOperationException>(reply.Failure);
        Assert.Empty(reply.Body);
    }

    [Fact]
    public async Task The_route_answers_not_found_at_once_while_the_flag_is_off()
    {
        var queue = new InMemorySessionRequestStore();
        queue.Add(Entry(Guid.NewGuid(), DateTimeOffset.UtcNow));

        var reply = await SessionRequestWaitEndpoint.WaitAsync(queue, Features(on: false), TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.Equal(404, reply.StatusCode);
        Assert.Empty(reply.Body);
        Assert.Equal(0, queue.NextCalls);
    }

    [Fact]
    public async Task A_caller_that_goes_away_ends_the_wait()
    {
        var queue = new InMemorySessionRequestStore();
        using var gone = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        gone.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SessionRequestWaitEndpoint.WaitAsync(queue, Features(on: true), TimeProvider.System, gone.Token, TimeSpan.FromSeconds(30), ShortPoll));
    }

    [Fact]
    public async Task A_delivery_is_handed_out_before_an_older_session_request()
    {
        var now = DateTimeOffset.UtcNow;
        var queue = new InMemorySessionRequestStore();
        queue.Add(Entry(Guid.NewGuid(), now - TimeSpan.FromHours(1)));
        var questions = new HeldWaitingQuestions();
        var delivery = questions.AddPendingDelivery("cli-1", "Which database?", "Postgres", now - TimeSpan.FromMinutes(1));
        var sessions = new FixedSessionSource(FixedSessionSource.Claude("cli-1", "local_1"));

        var reply = await SessionRequestWaitEndpoint.WaitAsync(
            queue, Features(on: true), TimeProvider.System, TestContext.Current.CancellationToken, questions: questions, sessions: sessions);

        Assert.Equal(200, reply.StatusCode);
        Assert.Equal(0, queue.NextCalls);

        using var json = JsonDocument.Parse(reply.Body);
        var root = json.RootElement;
        Assert.Equal("delivery", root.GetProperty("kind").GetString());
        Assert.Equal(delivery.Id, root.GetProperty("id").GetGuid());
        Assert.Equal("cli-1", root.GetProperty("sessionId").GetString());
        Assert.Equal("local_1", root.GetProperty("desktopSessionId").GetString());
        Assert.Equal("Which database?", root.GetProperty("question").GetString());
        Assert.Equal("Postgres", root.GetProperty("answer").GetString());
        Assert.Equal("pending", root.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Deliveries_are_handed_out_oldest_answer_first()
    {
        var now = DateTimeOffset.UtcNow;
        var questions = new HeldWaitingQuestions();
        questions.AddPendingDelivery("cli-2", "Second?", "b", now - TimeSpan.FromMinutes(1));
        var older = questions.AddPendingDelivery("cli-1", "First?", "a", now - TimeSpan.FromMinutes(5));
        var sessions = new FixedSessionSource(FixedSessionSource.Claude("cli-1", "local_1"), FixedSessionSource.Claude("cli-2", "local_2"));

        var reply = await SessionRequestWaitEndpoint.WaitAsync(
            new InMemorySessionRequestStore(), Features(on: true), TimeProvider.System, TestContext.Current.CancellationToken,
            questions: questions, sessions: sessions);

        using var json = JsonDocument.Parse(reply.Body);
        Assert.Equal(older.Id, json.RootElement.GetProperty("id").GetGuid());
    }

    /// <summary>A CLI session, or one the desktop app archived, has nobody the Manager could
    /// send the answer to, so its delivery waits and the queue behind it is not held up.</summary>
    [Fact]
    public async Task A_delivery_whose_session_the_desktop_app_does_not_hold_open_is_not_handed_out()
    {
        var now = DateTimeOffset.UtcNow;
        var queue = new InMemorySessionRequestStore();
        var request = queue.Add(Entry(Guid.NewGuid(), now));
        var questions = new HeldWaitingQuestions();
        questions.AddPendingDelivery("cli-only", "A?", "a", now);
        questions.AddPendingDelivery("archived", "B?", "b", now);
        var sessions = new FixedSessionSource(FixedSessionSource.Claude("cli-only", null), FixedSessionSource.Claude("archived", "local_9", archived: true));

        var reply = await SessionRequestWaitEndpoint.WaitAsync(
            queue, Features(on: true), TimeProvider.System, TestContext.Current.CancellationToken, questions: questions, sessions: sessions);

        using var json = JsonDocument.Parse(reply.Body);
        Assert.Equal("request", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal(request.Id, json.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_claimed_delivery_is_not_handed_out_until_its_lease_runs_out()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var questions = new HeldWaitingQuestions();
        var delivery = questions.AddPendingDelivery("cli-1", "A?", "a", clock.GetUtcNow());
        await questions.ChangeAsync(delivery.Id, question => WaitingQuestions.ClaimDelivery(question, "manager-1", clock.GetUtcNow()), TestContext.Current.CancellationToken);
        var sessions = new FixedSessionSource(FixedSessionSource.Claude("cli-1", "local_1"));

        var held = await SessionRequestWaitEndpoint.WaitAsync(
            new InMemorySessionRequestStore(), Features(on: true), clock, TestContext.Current.CancellationToken, TimeSpan.Zero, ShortPoll, questions, sessions);

        clock.Advance(WaitingQuestions.DeliveryLease);

        var lapsed = await SessionRequestWaitEndpoint.WaitAsync(
            new InMemorySessionRequestStore(), Features(on: true), clock, TestContext.Current.CancellationToken, TimeSpan.Zero, ShortPoll, questions, sessions);

        Assert.Equal(204, held.StatusCode);
        Assert.Equal(200, lapsed.StatusCode);
        using var json = JsonDocument.Parse(lapsed.Body);
        Assert.Equal(delivery.Id, json.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("pending", json.RootElement.GetProperty("state").GetString());
    }

    /// <summary>A delivery the Manager gave back — its session could not be reached — is
    /// held back for one lease, so the route does not wake the Manager for it on every call.</summary>
    [Fact]
    public async Task A_released_delivery_is_held_back_for_one_lease()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var questions = new HeldWaitingQuestions();
        var delivery = questions.AddPendingDelivery("cli-1", "A?", "a", clock.GetUtcNow());
        await questions.ChangeAsync(delivery.Id, question => WaitingQuestions.ClaimDelivery(question, "manager-1", clock.GetUtcNow()), TestContext.Current.CancellationToken);
        await questions.ChangeAsync(delivery.Id, question => WaitingQuestions.ReleaseDelivery(question, "manager-1", clock.GetUtcNow()), TestContext.Current.CancellationToken);
        var sessions = new FixedSessionSource(FixedSessionSource.Claude("cli-1", "local_1"));

        var held = await SessionRequestWaitEndpoint.WaitAsync(
            new InMemorySessionRequestStore(), Features(on: true), clock, TestContext.Current.CancellationToken, TimeSpan.Zero, ShortPoll, questions, sessions);

        clock.Advance(WaitingQuestions.DeliveryLease);

        var again = await SessionRequestWaitEndpoint.WaitAsync(
            new InMemorySessionRequestStore(), Features(on: true), clock, TestContext.Current.CancellationToken, TimeSpan.Zero, ShortPoll, questions, sessions);

        Assert.Equal(204, held.StatusCode);
        Assert.Equal(200, again.StatusCode);
    }

    [Fact]
    public async Task One_held_call_reads_the_session_list_once_for_a_delivery_it_cannot_send()
    {
        var questions = new HeldWaitingQuestions();
        questions.AddPendingDelivery("cli-only", "A?", "a", DateTimeOffset.UtcNow);
        var sessions = new FixedSessionSource(FixedSessionSource.Claude("cli-only", null));

        var reply = await SessionRequestWaitEndpoint.WaitAsync(
            new InMemorySessionRequestStore(), Features(on: true), TimeProvider.System, TestContext.Current.CancellationToken,
            TimeSpan.FromMilliseconds(200), ShortPoll, questions, sessions);

        Assert.Equal(204, reply.StatusCode);
        Assert.Equal(1, sessions.Reads);
    }

    private static IAppFeatureSettings Features(bool on) => new SessionRequestFlag(on);

    /// <summary>A queue whose database cannot be opened: every read throws.</summary>
    private sealed class UnreadableQueue : ISessionRequestStore
    {
        private static Task<T> Fail<T>() => Task.FromException<T>(new InvalidOperationException("The database is locked."));

        public Task<SessionRequest> RequestAsync(SessionRequest candidate, CancellationToken cancellationToken = default) => Fail<SessionRequest>();

        public Task<SessionRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Fail<SessionRequest?>();

        public Task<IReadOnlyList<SessionRequest>> ListAsync(SessionRequestState? state = null, CancellationToken cancellationToken = default) => Fail<IReadOnlyList<SessionRequest>>();

        public Task<SessionRequest?> NextAsync(CancellationToken cancellationToken = default) => Fail<SessionRequest?>();

        public Task<SessionRequestOutcome> ClaimAsync(Guid id, string dispatcher, CancellationToken cancellationToken = default) => Fail<SessionRequestOutcome>();

        public Task<SessionRequestOutcome> ReleaseAsync(Guid id, string dispatcher, CancellationToken cancellationToken = default) => Fail<SessionRequestOutcome>();

        public Task<SessionRequestOutcome> ReportStartedAsync(Guid id, string dispatcher, string sessionId, CancellationToken cancellationToken = default) => Fail<SessionRequestOutcome>();

        public Task<SessionRequestOutcome> CancelAsync(Guid id, CancellationToken cancellationToken = default) => Fail<SessionRequestOutcome>();

        public Task<IReadOnlyList<SessionRequest>> LinkAsync(Guid entryId, IReadOnlyCollection<string> planTags, string sessionId, CancellationToken cancellationToken = default) =>
            Fail<IReadOnlyList<SessionRequest>>();
    }

    /// <summary>Answers <paramref name="on"/> for <c>session-requests</c> and true for
    /// every other key.</summary>
    private sealed class SessionRequestFlag(bool on) : IAppFeatureSettings
    {
        public event Action? Changed;

        private bool _on = on;

        public bool On
        {
            get => Volatile.Read(ref _on);
            set => Volatile.Write(ref _on, value);
        }

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "(replaced for this test)";

        public bool IsEnabled(string key) =>
            !string.Equals(key, SessionFeatures.SessionRequests, StringComparison.Ordinal) || On;

        public string? SetEnabled(string key, bool enabled)
        {
            Changed?.Invoke();
            return null;
        }
    }
}
