using Backlog.SharedKernel.Mcp;

namespace Backlog.SharedKernel.UnitTests;

/// <summary>The one dropped-answer record the notification exchange and the question relay
/// both write when an answer comes from a device not paired now (local ADR 0058, Phase 2).</summary>
public sealed class DroppedAnswerAuditTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Device = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Target = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Theory]
    [InlineData(DeviceStanding.Revoked, "revoked device")]
    [InlineData(DeviceStanding.Unknown, "unknown device")]
    public void A_device_not_paired_is_named_by_its_standing(DeviceStanding standing, string label) =>
        Assert.Equal(label, DroppedAnswerAudit.DeviceLabel(standing));

    [Fact]
    public void A_paired_device_drops_nothing() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DroppedAnswerAudit.DeviceLabel(DeviceStanding.Paired));

    [Fact]
    public async Task The_record_names_the_device_its_standing_and_the_target_and_is_refused()
    {
        var audit = new RecordingAuditLog();

        await DroppedAnswerAudit.AppendAsync(audit, Noon, DeviceStanding.Revoked, Device, Target, _ => Assert.Fail("The write did not fail."), TestContext.Current.CancellationToken);

        var record = Assert.Single(audit.Records);
        Assert.Equal(Noon, record.At);
        Assert.Equal($"revoked device {Device:D}", record.Caller);
        Assert.Equal(McpAudit.DroppedAnswer, record.Tool);
        Assert.Equal(Target.ToString("D"), record.TargetId);
        Assert.Equal(McpCallOutcome.Refused, record.Outcome);
        Assert.False(string.IsNullOrEmpty(record.CorrelationId));
    }

    /// <summary>A failed write is the caller's to log; the answer is dropped all the same.</summary>
    [Fact]
    public async Task A_failed_write_is_handed_to_the_caller_instead_of_thrown()
    {
        Exception? failed = null;

        await DroppedAnswerAudit.AppendAsync(new RecordingAuditLog { Broken = true }, Noon, DeviceStanding.Unknown, Device, Target, exception => failed = exception, TestContext.Current.CancellationToken);

        Assert.IsType<IOException>(failed);
    }

    [Fact]
    public async Task No_audit_log_writes_nothing() =>
        await DroppedAnswerAudit.AppendAsync(null, Noon, DeviceStanding.Unknown, Device, Target, _ => Assert.Fail("Nothing was written."), TestContext.Current.CancellationToken);

    private sealed class RecordingAuditLog : IMcpAuditLog
    {
        public event Action? Changed;

        public bool Broken { get; init; }

        public List<McpAuditRecord> Records { get; } = [];

        public Task AppendAsync(McpAuditRecord record, CancellationToken cancellationToken = default)
        {
            if (Broken) return Task.FromException(new IOException("The audit log is locked."));

            Records.Add(record);
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<McpAuditRecord>> LatestAsync(int count, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<McpAuditRecord>>([.. Records.Take(count)]);
    }
}
