using Backlog.Infrastructure.Sessions;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Mcp;
using Backlog.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// The question relay hands an answer to the hook or the Manager only when the device it came
/// from is paired now (local ADR 0041, ADR 0058 Phase 2): a paired device's answer is recorded
/// with its device id, and a revoked or unknown device's is dropped and audited.
/// </summary>
public sealed class QuestionRelayDeviceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly RelayQuestion Pick = new(
        "Which database should the cache use?",
        "Database",
        [new RelayOption("SQLite"), new RelayOption("Postgres")]);

    private static readonly Guid Phone = Guid.Parse("6f0c1a52-0000-4000-8000-000000000001");

    private readonly FakeTimeProvider _clock = new(Noon);
    private readonly RecordingNotifications _notifications = new();
    private readonly HeldWaitingQuestions _store = new();
    private readonly FixedDevices _devices = new();
    private readonly RecordingAuditLog _audit = new();
    private readonly QuestionRelay _relay;

    public QuestionRelayDeviceTests()
    {
        _relay = new QuestionRelay(
            _store,
            new AwayPresence(),
            () => TimeSpan.FromMinutes(15),
            () => _notifications,
            _clock,
            devices: () => _devices,
            audit: () => _audit);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_answer_from_a_paired_device_reaches_the_hook_and_records_its_device()
    {
        _devices.Standing = DeviceStanding.Paired;
        var asked = await AskWithHookPollingAsync();

        Assert.True(await _relay.HandleAnswerAsync(Answer(asked, Phone), Ct));
        var answered = (await _relay.GetAsync(asked.Id, Ct))!;

        Assert.Equal(WaitingQuestionState.Answered, answered.State);
        Assert.Null(answered.Delivery);
        Assert.Equal(Phone, answered.Answers[0]!.FromDeviceId);
        Assert.Equal([Phone], _devices.Asked);
        Assert.Empty(_audit.Records);
    }

    [Fact]
    public async Task An_answer_from_a_paired_device_nobody_polls_for_becomes_the_managers_delivery()
    {
        _devices.Standing = DeviceStanding.Paired;
        var asked = (await _relay.AskAsync("session-1", null, [RelayQuestion.Prose("Ship it?")], Ct)).Value;

        await _relay.HandleAnswerAsync(Answer(asked, Phone, [], "Yes"), Ct);
        var answered = (await _relay.GetAsync(asked.Id, Ct))!;

        Assert.Equal(AnswerDeliveryState.Pending, answered.Delivery);
        Assert.Equal(Phone, answered.Answers[0]!.FromDeviceId);
    }

    [Theory]
    [InlineData(DeviceStanding.Revoked, "revoked device")]
    [InlineData(DeviceStanding.Unknown, "unknown device")]
    public async Task An_answer_from_a_device_not_paired_now_is_dropped_and_audited(DeviceStanding standing, string caller)
    {
        _devices.Standing = standing;
        var asked = await AskWithHookPollingAsync();

        Assert.True(await _relay.HandleAnswerAsync(Answer(asked, Phone), Ct));
        var held = (await _relay.GetAsync(asked.Id, Ct))!;

        Assert.Equal(WaitingQuestionState.Notified, held.State);
        Assert.Null(held.Delivery);
        Assert.All(held.Answers, Assert.Null);

        var record = Assert.Single(_audit.Records);
        Assert.Equal($"{caller} {Phone:D}", record.Caller);
        Assert.Equal(McpAudit.DroppedAnswer, record.Tool);
        Assert.Equal(asked.Id.ToString("D"), record.TargetId);
        Assert.Equal(McpCallOutcome.Refused, record.Outcome);
    }

    [Fact]
    public async Task A_dropped_answer_leaves_the_held_hook_waiting()
    {
        _devices.Standing = DeviceStanding.Revoked;
        var asked = await AskWithHookPollingAsync();

        var waiting = _relay.WaitAsync(asked.Id, TimeSpan.FromSeconds(55), Ct);
        await _relay.HandleAnswerAsync(Answer(asked, Phone), Ct);

        Assert.False(waiting.IsCompleted);
        _clock.Advance(TimeSpan.FromSeconds(55));
        Assert.Equal(WaitingQuestionState.Notified, (await waiting)!.State);
    }

    [Fact]
    public async Task A_device_that_cannot_be_checked_throws_so_the_answer_is_replayed()
    {
        _devices.Failure = new InvalidOperationException("The owner's devices could not be read.");
        var asked = await AskWithHookPollingAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _relay.HandleAnswerAsync(Answer(asked, Phone), Ct));

        Assert.Equal(WaitingQuestionState.Notified, (await _relay.GetAsync(asked.Id, Ct))!.State);
        Assert.Empty(_audit.Records);
    }

    [Fact]
    public async Task An_answer_to_a_question_already_ended_checks_no_device()
    {
        _devices.Standing = DeviceStanding.Paired;
        var asked = await AskWithHookPollingAsync();
        await _relay.HandleAnswerAsync(Answer(asked, Phone), Ct);
        _devices.Standing = DeviceStanding.Revoked;

        Assert.True(await _relay.HandleAnswerAsync(Answer(asked, Phone), Ct));

        Assert.Single(_devices.Asked);
        Assert.Empty(_audit.Records);
    }

    private async Task<WaitingQuestion> AskWithHookPollingAsync()
    {
        var asked = (await _relay.AskAsync("session-1", "toolu_1", [Pick], Ct)).Value;
        await _relay.RecordPollAsync(asked.Id, Ct);
        return asked;
    }

    private NotificationAnswer Answer(WaitingQuestion question, Guid fromDevice, IReadOnlyList<string>? choices = null, string? text = null) =>
        new(
            question.NotificationIds[0],
            new NotificationSource("sessions", question.Id.ToString("D")),
            Guid.NewGuid(),
            fromDevice,
            choices ?? ["option-1"],
            text,
            _clock.GetUtcNow());

    private sealed class AwayPresence : IPresence
    {
        public bool IsAway => true;

        public DateTimeOffset? LastInputAt => null;

        public DateTimeOffset? LastPromptAt(string sessionId) => null;
    }

    private sealed class FixedDevices : IPairedDevices
    {
        public DeviceStanding Standing { get; set; } = DeviceStanding.Paired;

        public Exception? Failure { get; set; }

        public List<Guid> Asked { get; } = [];

        public Task<DeviceStanding> StandingOfAsync(Guid deviceId, CancellationToken cancellationToken = default)
        {
            Asked.Add(deviceId);
            return Failure is { } failure ? Task.FromException<DeviceStanding>(failure) : Task.FromResult(Standing);
        }
    }

    private sealed class RecordingAuditLog : IMcpAuditLog
    {
        public event Action? Changed;

        public List<McpAuditRecord> Records { get; } = [];

        public Task AppendAsync(McpAuditRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<McpAuditRecord>> LatestAsync(int count, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<McpAuditRecord>>([.. Records.Take(count)]);
    }
}
