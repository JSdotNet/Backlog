using Backlog.Desktop.UI.Mcp;
using Backlog.SharedKernel.Mcp;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The desktop listener's limit on failed bearer attempts (local ADR 0052, Phase 0):
/// ten within sixty seconds lock every bearer request out for sixty seconds, the trip is
/// one audit record, and a lockout that has run out starts the count afresh. The status
/// codes around it are <c>McpServerWorker.GuardAsync</c>'s, held by
/// <c>McpServerWorkerTests</c> against a running listener.
/// </summary>
public sealed class McpBearerLimiterTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Nine_failures_lock_nothing_and_the_tenth_trips_the_lockout_with_one_record()
    {
        var limiter = new McpBearerLimiter(_time);

        for (var i = 1; i < McpBearerLimiter.MaxFailures; i++)
        {
            Assert.Null(limiter.Fail());
            Assert.Null(limiter.RetryAfter());
        }

        var record = limiter.Fail();

        Assert.NotNull(record);
        Assert.Equal(McpAudit.NoValidTokenCaller, record.Caller);
        Assert.Equal(McpAudit.BearerLockout, record.Tool);
        Assert.Null(record.TargetId);
        Assert.Equal(McpCallOutcome.Refused, record.Outcome);
        Assert.Equal(_time.GetUtcNow(), record.At);
        Assert.False(string.IsNullOrWhiteSpace(record.CorrelationId));
        Assert.Equal(McpBearerLimiter.Lockout, limiter.RetryAfter());
    }

    [Fact]
    public void Failures_spread_wider_than_the_window_never_trip_it()
    {
        var limiter = new McpBearerLimiter(_time);

        for (var i = 0; i < McpBearerLimiter.MaxFailures * 3; i++)
        {
            Assert.Null(limiter.Fail());
            // Just over a ninth of the window apart: the tenth lands as the first leaves it.
            _time.Advance((McpBearerLimiter.Window / (McpBearerLimiter.MaxFailures - 1)) + TimeSpan.FromMilliseconds(1));
        }

        Assert.Null(limiter.RetryAfter());
    }

    [Fact]
    public void The_lockout_counts_down_and_failures_during_it_are_not_counted_or_recorded()
    {
        var limiter = Tripped();

        _time.Advance(TimeSpan.FromSeconds(45));
        Assert.Equal(TimeSpan.FromSeconds(15), limiter.RetryAfter());

        for (var i = 0; i < McpBearerLimiter.MaxFailures; i++) Assert.Null(limiter.Fail());

        Assert.Equal(TimeSpan.FromSeconds(15), limiter.RetryAfter());
    }

    [Fact]
    public void A_lockout_that_has_run_out_admits_again_and_starts_the_count_afresh()
    {
        var limiter = Tripped();

        _time.Advance(McpBearerLimiter.Lockout);

        Assert.Null(limiter.RetryAfter());

        for (var i = 1; i < McpBearerLimiter.MaxFailures; i++) Assert.Null(limiter.Fail());
        Assert.Null(limiter.RetryAfter());

        Assert.NotNull(limiter.Fail());
        Assert.Equal(McpBearerLimiter.Lockout, limiter.RetryAfter());
    }

    [Fact]
    public void The_token_a_rotation_retired_is_not_counted_for_one_window_and_any_other_still_is()
    {
        var limiter = new McpBearerLimiter(_time);
        limiter.Retire("old-token");

        // Every running registration's next request after Rotate.
        for (var i = 0; i < McpBearerLimiter.MaxFailures * 2; i++) Assert.Null(limiter.Fail("Bearer old-token"));
        Assert.Null(limiter.RetryAfter());

        for (var i = 1; i < McpBearerLimiter.MaxFailures; i++) Assert.Null(limiter.Fail("Bearer guess"));
        Assert.NotNull(limiter.Fail(null));
    }

    [Fact]
    public void The_retired_token_counts_again_once_its_window_is_over()
    {
        var limiter = new McpBearerLimiter(_time);
        limiter.Retire("old-token");

        _time.Advance(McpBearerLimiter.Window);

        for (var i = 1; i < McpBearerLimiter.MaxFailures; i++) Assert.Null(limiter.Fail("Bearer old-token"));
        Assert.NotNull(limiter.Fail("Bearer old-token"));
    }

    private McpBearerLimiter Tripped()
    {
        var limiter = new McpBearerLimiter(_time);
        for (var i = 1; i < McpBearerLimiter.MaxFailures; i++) limiter.Fail();
        Assert.NotNull(limiter.Fail());
        return limiter;
    }
}
