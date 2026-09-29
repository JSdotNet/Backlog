using System.Globalization;

using Backlog.Infrastructure.Sqlite.Roadmap;
using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.DomainModels;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The plan's row as the document that travels between devices (local ADR 0018,
/// Decision §1): read as its stored text and stamp, written back verbatim at the
/// stamp it arrived with, and never overwritten with something that is not a plan.
/// </summary>
public sealed class SqliteRoadmapPlanReplicaTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "roadmap-replica-tests-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Device(string name) => Path.Combine(_root, name);

    [Fact]
    public void The_row_holds_the_plan_document()
    {
        Assert.Equal(RoadmapReplicaDocument.Plan, new SqliteRoadmapPlanRepository(Device("a")).Document);
    }

    /// <summary>ADR 0018 Verification 5: no row means no plan document, so a device
    /// that never planned sends nothing.</summary>
    [Fact]
    public async Task A_workspace_that_never_saved_a_plan_has_no_document()
    {
        Assert.Null(await new SqliteRoadmapPlanRepository(Device("a")).ReadAsync(Cancellation));
    }

    [Fact]
    public async Task The_document_is_the_stored_column_and_its_stamp()
    {
        var plans = new SqliteRoadmapPlanRepository(Device("a"));
        var plan = RoadmapPlan.Empty();
        plan.AddItem("Ship sync", PlannedWindow.Of(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9)));
        await plans.SaveAsync(plan, Cancellation);

        var copy = await plans.ReadAsync(Cancellation);

        Assert.NotNull(copy);
        Assert.Equal(await ScalarAsync(plans.DatabasePath, "SELECT document FROM roadmap_plan;"), copy.Content);
        Assert.Equal(
            DateTimeOffset.Parse(
                (await ScalarAsync(plans.DatabasePath, "SELECT updated_at FROM roadmap_plan;"))!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind),
            copy.UpdatedAt);
    }

    /// <summary>ADR 0018 Verification 1, at the store: what one device wrote, another
    /// takes whole — the same items, milestones and band colours, at the same
    /// <c>updated_at</c>.</summary>
    [Fact]
    public async Task A_copy_written_on_another_device_loads_as_the_same_plan_at_the_same_stamp()
    {
        var a = new SqliteRoadmapPlanRepository(Device("a"));
        var b = new SqliteRoadmapPlanRepository(Device("b"));

        var plan = RoadmapPlan.Rehydrate([], [], BandColours.Of([new KeyValuePair<string, int>("backlog", 4)]));
        plan.AddItem("Ship sync", PlannedWindow.Of(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9)));
        plan.AddMilestone("1.0", new DateOnly(2026, 10, 31), MilestoneKind.Release);
        await a.SaveAsync(plan, Cancellation);

        var sent = (await a.ReadAsync(Cancellation))!;
        Assert.True(await b.TryWriteAsync(sent, Cancellation));

        var loaded = await b.LoadAsync(Cancellation);
        Assert.Equal("Ship sync", Assert.Single(loaded.Items).Title);
        Assert.Equal("1.0", Assert.Single(loaded.Milestones).Title);
        Assert.Equal(4, loaded.BandColours.For("backlog"));
        Assert.Equal(sent, await b.ReadAsync(Cancellation));
    }

    /// <summary>Verbatim, never re-serialised: a newer build's field survives on disk
    /// even though this build does not know it (ADR 0018, Decision §3).</summary>
    [Fact]
    public async Task A_copy_is_stored_verbatim_with_fields_this_build_does_not_know()
    {
        var plans = new SqliteRoadmapPlanRepository(Device("a"));
        var content = """{"version":2,"items":[],"milestones":[],"bands":{},"swimlanes":["later"]}""";

        Assert.True(await plans.TryWriteAsync(new RoadmapReplicaCopyDto(content, Morning), Cancellation));

        Assert.Equal(content, await ScalarAsync(plans.DatabasePath, "SELECT document FROM roadmap_plan;"));
        Assert.Equal(
            Morning.ToString("O", CultureInfo.InvariantCulture),
            await ScalarAsync(plans.DatabasePath, "SELECT updated_at FROM roadmap_plan;"));
    }

    /// <summary>ADR 0018 Verification 6, at the store: a payload that is not a plan is
    /// refused and the row stays as it is.</summary>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"storyPointsPerWeek":5}""")]
    [InlineData("""{"version":1,"items":"many"}""")]
    [InlineData("")]
    public async Task A_payload_that_is_not_a_plan_is_refused_and_the_row_is_untouched(string content)
    {
        var plans = new SqliteRoadmapPlanRepository(Device("a"));
        var plan = RoadmapPlan.Empty();
        plan.AddItem("Keep me", PlannedWindow.Of(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9)));
        await plans.SaveAsync(plan, Cancellation);
        var before = await plans.ReadAsync(Cancellation);

        Assert.False(await plans.TryWriteAsync(new RoadmapReplicaCopyDto(content, Morning.AddYears(1)), Cancellation));

        Assert.Equal(before, await plans.ReadAsync(Cancellation));
    }

    /// <summary>
    /// A save after a copy was taken stamps past it, whatever this machine's clock
    /// says. A copy from a PC whose clock runs ahead would otherwise outrank every edit
    /// made here until this clock caught up, and the edit would lose to the plan it was
    /// made on — the rule the task aggregate keeps for its own stamps.
    /// </summary>
    [Fact]
    public async Task A_save_after_taking_a_copy_from_the_future_stamps_past_it()
    {
        var plans = new SqliteRoadmapPlanRepository(Device("a"));
        var ahead = DateTimeOffset.UtcNow.AddHours(3);
        await plans.TryWriteAsync(new RoadmapReplicaCopyDto("""{"version":1}""", ahead), Cancellation);

        var plan = await plans.LoadAsync(Cancellation);
        plan.AddItem("Edited here", PlannedWindow.Of(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9)));
        await plans.SaveAsync(plan, Cancellation);

        Assert.True((await plans.ReadAsync(Cancellation))!.UpdatedAt > ahead);
    }

    [Fact]
    public async Task The_rooted_repository_answers_for_the_folder_it_points_at()
    {
        var root = Device("a");
        IRoadmapReplicaStore rooted = new RootedSqliteRoadmapPlanRepository(() => root);

        Assert.Equal(RoadmapReplicaDocument.Plan, rooted.Document);
        Assert.True(await rooted.TryWriteAsync(new RoadmapReplicaCopyDto("""{"version":1}""", Morning), Cancellation));
        Assert.Equal(Morning, (await new SqliteRoadmapPlanRepository(root).ReadAsync(Cancellation))!.UpdatedAt);
    }

    private static async Task<string?> ScalarAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteScalarAsync() as string;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
