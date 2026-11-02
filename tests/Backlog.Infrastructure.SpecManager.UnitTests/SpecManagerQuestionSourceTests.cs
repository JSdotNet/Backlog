using System.Collections.Concurrent;
using System.Threading.Channels;

using Backlog.Infrastructure.SpecManager.Questions;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// The loop that holds the notification stream: a pull on each event and each time the
/// stream opens, the two-minute poll while it is down, and the growing wait before it is
/// opened again. The stream is a fake the test feeds; every wait is one the test ends.
/// </summary>
public sealed class SpecManagerQuestionSourceTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Opening_the_stream_pulls_whole_and_each_changed_event_pulls_again()
    {
        using var scenario = new QuestionScenario();
        var connector = new FakeStreams();
        var stream = connector.Open();
        using var source = Start(scenario, connector, out var delays);

        await Idle(source, delays, open: true);
        Assert.NotEmpty(Queries(scenario));
        Assert.All(Queries(scenario), query => Assert.DoesNotContain("changedSince", query, StringComparison.Ordinal));
        Assert.Equal(2, scenario.Ledger.Published.Count);
        var before = Queries(scenario).Count;

        await stream.Send("changed");

        await Eventually(() => Queries(scenario).Count >= before + 2);
        Assert.All(Queries(scenario).Skip(before), query => Assert.Contains("changedSince", query, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_event_of_another_name_pulls_nothing()
    {
        using var scenario = new QuestionScenario();
        var connector = new FakeStreams();
        var stream = connector.Open();
        using var source = Start(scenario, connector, out var delays);
        await Idle(source, delays, open: true);
        var before = Queries(scenario).Count;

        await stream.Send("message");
        await stream.Send("changed");

        await Eventually(() => Queries(scenario).Count >= before + 2);
        await Idle(source, delays, open: true);
        Assert.Equal(before + 2, Queries(scenario).Count);
    }

    [Fact]
    public async Task While_the_stream_is_open_the_backup_pull_waits_five_minutes()
    {
        using var scenario = new QuestionScenario();
        var connector = new FakeStreams();
        connector.Open();
        using var source = Start(scenario, connector, out var delays);
        await Idle(source, delays, open: true);
        var before = Queries(scenario).Count;

        delays.End(SpecManagerQuestionSource.BackupInterval);

        await Eventually(() => Queries(scenario).Count >= before + 2);
    }

    [Fact]
    public async Task A_dropped_stream_pulls_at_once_polls_every_two_minutes_and_opens_again_after_five_seconds()
    {
        using var scenario = new QuestionScenario();
        var connector = new FakeStreams();
        var stream = connector.Open();
        using var source = Start(scenario, connector, out var delays);
        await Idle(source, delays, open: true);
        var opened = Queries(scenario).Count;

        stream.Drop(new IOException("The connection was reset."));

        await Idle(source, delays, open: false);
        Assert.True(Queries(scenario).Count >= opened + 2);
        await Eventually(() => delays.Waiting(SpecManagerQuestionSource.FirstRetryDelay));
        var dropped = Queries(scenario).Count;

        delays.End(SpecManagerQuestionSource.FallbackInterval);
        await Eventually(() => Queries(scenario).Count >= dropped + 2);
        await Idle(source, delays, open: false);
        var polled = Queries(scenario).Count;

        connector.Open();
        scenario.Connector.Time.Advance(SpecManagerQuestionSource.WholePullFreshFor);
        delays.End(SpecManagerQuestionSource.FirstRetryDelay);
        await Eventually(() => source.IsOpen);
        await Eventually(() => Queries(scenario).Count >= polled + 2);
        Assert.All(Queries(scenario).Skip(polled).Take(2), query => Assert.DoesNotContain("changedSince", query, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_stream_that_will_not_open_is_tried_again_on_a_growing_wait_while_the_poll_carries_on()
    {
        using var scenario = new QuestionScenario();
        var connector = new FakeStreams();
        using var source = Start(scenario, connector, out var delays);

        await Idle(source, delays, open: false);
        Assert.NotEmpty(Queries(scenario));

        var attempts = 1;
        foreach (var wait in new[] { 5, 10, 20, 40, 80 }.Select(seconds => TimeSpan.FromSeconds(seconds)))
        {
            await Eventually(() => delays.Waiting(wait));
            delays.End(wait);
            attempts++;
            await Eventually(() => connector.Attempts >= attempts);
        }

        await Eventually(() => delays.Waiting(SpecManagerQuestionSource.MaxRetryDelay));
        Assert.False(source.IsOpen);
    }

    /// <summary>A stream a proxy closes as soon as it opens is not reopened every five
    /// seconds: only one that stayed open a while resets the wait.</summary>
    [Fact]
    public async Task A_stream_that_drops_at_once_is_reopened_on_the_growing_wait()
    {
        using var scenario = new QuestionScenario();
        var connector = new FakeStreams();
        var first = connector.Open();
        using var source = Start(scenario, connector, out var delays);
        await Idle(source, delays, open: true);

        first.Drop(new IOException("Closed by the proxy."));
        await Eventually(() => delays.Waiting(SpecManagerQuestionSource.FirstRetryDelay));

        var second = connector.Open();
        delays.End(SpecManagerQuestionSource.FirstRetryDelay);
        await Eventually(() => source.IsOpen);
        second.Drop(new IOException("Closed by the proxy."));

        await Eventually(() => delays.Waiting(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task A_stream_that_stayed_open_resets_the_wait()
    {
        using var scenario = new QuestionScenario();
        var connector = new FakeStreams();
        var stream = connector.Open();
        using var source = Start(scenario, connector, out var delays);
        await Idle(source, delays, open: true);

        scenario.Connector.Time.Advance(SpecManagerQuestionSource.StableConnection);
        stream.End();

        await Eventually(() => delays.Waiting(SpecManagerQuestionSource.FirstRetryDelay));
    }

    [Fact]
    public async Task Turning_on_only_questions_assigned_to_me_pulls_whole_at_once()
    {
        using var scenario = new QuestionScenario();
        var connector = new FakeStreams();
        connector.Open();
        using var source = Start(scenario, connector, out var delays);
        await Idle(source, delays, open: true);
        var before = Queries(scenario).Count;
        Assert.Equal(2, scenario.Ledger.Published.Count);

        scenario.OnlyAssignedToMe(true);

        await Eventually(() => Queries(scenario).Count >= before + 2);
        Assert.All(Queries(scenario).Skip(before), query => Assert.DoesNotContain("changedSince", query, StringComparison.Ordinal));
        await Eventually(() => scenario.Ledger.StateOf(scenario.IdOf(QuestionScenario.Unassigned)) == Backlog.Modules.Notifications.Abstractions.NotificationState.Closed);
    }

    [Fact]
    public async Task Signing_out_stops_the_stream()
    {
        using var scenario = new QuestionScenario();
        var connector = new FakeStreams();
        connector.Open();
        using var source = Start(scenario, connector, out var delays);
        await Idle(source, delays, open: true);

        await scenario.Connector.SignIn.SignOutAsync(TestContext.Current.CancellationToken);

        await Eventually(() => !source.IsOpen);
        await source.Loops.WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(5, 10)]
    [InlineData(60, 120)]
    [InlineData(120, 120)]
    public void The_wait_doubles_up_to_two_minutes(int seconds, int next) =>
        Assert.Equal(TimeSpan.FromSeconds(next), SpecManagerQuestionSource.Next(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public async Task Nobody_signed_in_holds_no_stream_and_pulls_nothing()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Store.Remove(scenario.Root);
        var connector = new FakeStreams();
        using var source = Start(scenario, connector, out _);

        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(0, connector.Attempts);
        Assert.Empty(Queries(scenario));
    }

    [Fact]
    public async Task An_answer_waiting_on_a_sign_in_is_handed_over_after_the_pull()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.SignIn.SetAnswersWaiting(true);
        var connector = new FakeStreams();
        using var source = Start(scenario, connector, out _);

        await Eventually(() => scenario.Ledger.Replays >= 1);
    }

    [Fact]
    public async Task A_notification_pull_that_fails_stops_neither_the_question_pull_the_answer_replay_nor_the_next_pull()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.SignIn.SetAnswersWaiting(true);
        var asked = 0;
        scenario.Connector.Server.Route(HttpMethod.Get, NotificationScenario.NotificationsPath, (_, _) =>
        {
            Interlocked.Increment(ref asked);
            throw new InvalidOperationException("Something nobody expected.");
        });
        var connector = new FakeStreams();
        var stream = connector.Open();
        using var source = Start(scenario, connector, out var delays, Notifications(scenario));
        await Idle(source, delays, open: true);
        Assert.Equal(2, scenario.Ledger.Published.Count);
        await Eventually(() => scenario.Ledger.Replays >= 1);
        var before = Volatile.Read(ref asked);

        await stream.Send("changed");

        await Eventually(() => Volatile.Read(ref asked) > before);
    }

    [Fact]
    public async Task A_question_pull_that_fails_leaves_the_notifications_to_be_pulled()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Server.Route(HttpMethod.Get, QuestionScenario.QuestionsPath, (_, _) => throw new InvalidOperationException("Something nobody expected."));
        scenario.Connector.Server.Json(HttpMethod.Get, NotificationScenario.NotificationsPath, "notifications-unread.json");
        var connector = new FakeStreams();
        connector.Open();
        using var source = Start(scenario, connector, out var delays, Notifications(scenario));

        await Idle(source, delays, open: true);

        await Eventually(() => scenario.Ledger.Published.Any(entry => entry.Draft.Source.Key.StartsWith("notification/", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task The_source_listens_for_acknowledgements_until_it_is_disposed()
    {
        using var scenario = new QuestionScenario();
        var centre = new FakeNotificationCentre();
        var reads = new SpecManagerNotificationReads(scenario.Connector.Client, scenario.Connector.SignIn, centre);
        var acknowledged = SpecManagerNotificationReadsTests.ViewOf(scenario.Root, NotificationScenario.Mention, Backlog.Modules.Notifications.Abstractions.NotificationState.Acknowledged);
        var source = Start(scenario, new FakeStreams(), out _, Notifications(scenario), reads);

        centre.Raise(acknowledged);
        await reads.Sends.WaitAsync(TestContext.Current.CancellationToken);
        source.Dispose();
        centre.Raise(acknowledged);
        await reads.Sends.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Single(scenario.Connector.Server.To(NotificationScenario.ReadPath(NotificationScenario.Mention)));
    }

    private static SpecManagerNotificationSync Notifications(QuestionScenario scenario) =>
        new(scenario.Connector.Client, scenario.Connector.SignIn, scenario.Ledger, scenario.Ledger, defaultInstallation: false);

    private static SpecManagerQuestionSource Start(
        QuestionScenario scenario,
        FakeStreams connector,
        out Delays delays,
        SpecManagerNotificationSync? notifications = null,
        SpecManagerNotificationReads? reads = null)
    {
        delays = new Delays();
        var source = new SpecManagerQuestionSource(
            scenario.Sync,
            connector,
            scenario.Connector.SignIn,
            scenario.Connector.Targets,
            scenario.Ledger,
            scenario.Connector.Time,
            log: null,
            start: false,
            notifications,
            reads);

        source.Delay = delays.Wait;
        source.Start();
        return source;
    }

    /// <summary>Waits until the pull loop waits on the interval the stream's state calls
    /// for — nothing is pulling — and the stream is as asked.</summary>
    private static async Task Idle(SpecManagerQuestionSource source, Delays delays, bool open)
    {
        await Eventually(() => source.IsOpen == open);
        await Eventually(() => delays.Waiting(open ? SpecManagerQuestionSource.BackupInterval : SpecManagerQuestionSource.FallbackInterval));
    }

    private static List<string> Queries(QuestionScenario scenario) =>
        [.. scenario.Connector.Server.To(QuestionScenario.QuestionsPath).Select(request => request.Uri.Query)];

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("The condition did not become true in time.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Every wait the source asks for, each ended by the test.</summary>
    private sealed class Delays
    {
        private readonly ConcurrentQueue<(TimeSpan Length, TaskCompletionSource Done)> _waits = new();

        public Task Wait(TimeSpan length, CancellationToken cancellationToken)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => done.TrySetCanceled(cancellationToken));
            _waits.Enqueue((length, done));
            return done.Task;
        }

        public bool Waiting(TimeSpan length) => _waits.Any(wait => wait.Length == length && !wait.Done.Task.IsCompleted);

        public void End(TimeSpan length)
        {
            foreach (var wait in _waits.Where(wait => wait.Length == length)) wait.Done.TrySetResult();
        }
    }

    /// <summary>Streams the test opens: each connect takes the next one queued, and with
    /// none queued it fails as a refused stream does.</summary>
    private sealed class FakeStreams : ISpecManagerEventStreamConnector
    {
        private readonly ConcurrentQueue<FakeStream> _queued = new();
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public FakeStream Open()
        {
            var stream = new FakeStream();
            _queued.Enqueue(stream);
            return stream;
        }

        public Task<ISpecManagerEventStream> ConnectAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            return _queued.TryDequeue(out var stream)
                ? Task.FromResult<ISpecManagerEventStream>(stream)
                : Task.FromException<ISpecManagerEventStream>(new HttpRequestException("spec-manager could not be reached."));
        }
    }

    private sealed class FakeStream : ISpecManagerEventStream
    {
        private readonly Channel<string> _events = Channel.CreateUnbounded<string>();

        public ValueTask Send(string name) => _events.Writer.WriteAsync(name);

        public void Drop(Exception failure) => _events.Writer.TryComplete(failure);

        public void End() => _events.Writer.TryComplete();

        public async Task<string?> ReadEventAsync(CancellationToken cancellationToken)
        {
            if (!await _events.Reader.WaitToReadAsync(cancellationToken)) return null;
            return _events.Reader.TryRead(out var name) ? name : null;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
