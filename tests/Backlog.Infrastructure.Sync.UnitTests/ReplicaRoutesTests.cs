using System.Globalization;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The three helpers every replica exchange in this project shares — the pull
/// URL, the cursor answers a device recovers from, and how far a watermark may
/// move after a batch. Pinned here once, so the task, session and annotation
/// exchanges cannot drift apart the way three private copies could.
/// </summary>
public sealed class ReplicaRoutesTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_pull_without_a_cursor_carries_only_maxItems(string? since)
    {
        var route = ReplicaRoutes.PullRoute(SyncRoutes.Tasks, since, 50);

        Assert.Equal($"{SyncRoutes.Absolute(SyncRoutes.Tasks)}?maxItems=50", route);
    }

    [Fact]
    public void A_cursor_is_escaped_so_its_base64_survives_the_query()
    {
        var route = ReplicaRoutes.PullRoute(SyncRoutes.Sessions, "a+b/c=", 10);

        Assert.Equal($"{SyncRoutes.Absolute(SyncRoutes.Sessions)}?maxItems=10&since=a%2Bb%2Fc%3D", route);
    }

    [Fact]
    public void The_route_is_the_one_the_caller_names()
    {
        var route = ReplicaRoutes.PullRoute(SyncRoutes.Annotations, null, 1);

        Assert.StartsWith(SyncRoutes.Absolute(SyncRoutes.Annotations) + "?", route, StringComparison.Ordinal);
    }

    [Fact]
    public void MaxItems_is_formatted_in_the_invariant_culture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NegativeSign = "~";
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            var route = ReplicaRoutes.PullRoute(SyncRoutes.Tasks, null, -1);

            Assert.EndsWith("?maxItems=-1", route, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(SyncErrorCodes.SyncCursorExpired, true)]
    [InlineData(SyncErrorCodes.SyncCursorMalformed, true)]
    [InlineData(SyncErrorCodes.ReplicaUnavailable, false)]
    [InlineData("anything.else", false)]
    public void Only_an_expired_or_malformed_cursor_is_retired(string code, bool retired)
    {
        Assert.Equal(retired, ReplicaRoutes.Retired(code));
    }

    [Fact]
    public void The_final_batch_moves_the_watermark_to_its_last_stamp()
    {
        TaskItem[] batch =
        [
            TaskChanges.Task("First", Noon),
            TaskChanges.Task("Second", Noon.AddMinutes(1)),
            TaskChanges.Task("Third", Noon.AddMinutes(1)),
        ];

        Assert.Equal(Noon.AddMinutes(1), ReplicaRoutes.WatermarkAfter(batch, final: true, task => task.UpdatedAt));
    }

    [Fact]
    public void Any_other_batch_moves_it_to_the_highest_stamp_below_its_last()
    {
        TaskItem[] batch =
        [
            TaskChanges.Task("First", Noon),
            TaskChanges.Task("Second", Noon.AddMinutes(1)),
            TaskChanges.Task("Third", Noon.AddMinutes(2)),
            TaskChanges.Task("Fourth", Noon.AddMinutes(2)),
        ];

        Assert.Equal(Noon.AddMinutes(1), ReplicaRoutes.WatermarkAfter(batch, final: false, task => task.UpdatedAt));
    }

    [Fact]
    public void A_batch_sharing_one_stamp_leaves_the_watermark_where_it_is()
    {
        TaskItem[] batch =
        [
            TaskChanges.Task("First", Noon),
            TaskChanges.Task("Second", Noon),
        ];

        Assert.Null(ReplicaRoutes.WatermarkAfter(batch, final: false, task => task.UpdatedAt));
    }

    [Fact]
    public void A_session_batch_reads_its_last_activity_stamp()
    {
        var sessions = new[]
        {
            AgentSessions.Local(id: "a", lastActivityAt: Noon),
            AgentSessions.Local(id: "b", lastActivityAt: Noon.AddMinutes(3)),
            AgentSessions.Local(id: "c", lastActivityAt: Noon.AddMinutes(5)),
        };

        Assert.Equal(Noon.AddMinutes(3), ReplicaRoutes.WatermarkAfter(sessions, final: false, session => session.LastActivityAt));
        Assert.Equal(Noon.AddMinutes(5), ReplicaRoutes.WatermarkAfter(sessions, final: true, session => session.LastActivityAt));
    }
}
