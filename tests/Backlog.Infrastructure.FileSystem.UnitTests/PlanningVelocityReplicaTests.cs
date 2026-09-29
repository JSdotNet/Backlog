using System.Text.Json.Nodes;

using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The pace file as the second document that travels between a person's devices
/// (local ADR 0018, Decision §2): it gains an <c>updatedAt</c> written on every
/// change, a file from before that is stamped from when it was last written, and a
/// copy from another device replaces it whole at the stamp it arrived with.
/// </summary>
public sealed class PlanningVelocityReplicaTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "planning-velocity-replica-tests-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string FileOf(string device) => Path.Combine(_root, device, "planning-velocity.json");

    private PlanningVelocitySettingsStore Store(string device = "a", TimeProvider? time = null) =>
        new(FileOf(device), time);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void The_file_holds_the_pace_document()
    {
        Assert.Equal(RoadmapReplicaDocument.Pace, ((IRoadmapReplicaStore)Store()).Document);
    }

    /// <summary>ADR 0018 Verification 5: no velocity file means no pace document, so
    /// a device that never set a pace never sends the default of seven.</summary>
    [Fact]
    public async Task A_reader_who_never_set_a_pace_has_no_document()
    {
        Assert.Null(await ((IRoadmapReplicaStore)Store()).ReadAsync(Cancellation));
    }

    [Fact]
    public async Task Every_change_writes_its_stamp_into_the_file()
    {
        var clock = new FakeTimeProvider(Morning);
        var store = Store(time: clock);

        store.Set(5m);
        var first = await ((IRoadmapReplicaStore)store).ReadAsync(Cancellation);

        clock.Advance(TimeSpan.FromMinutes(3));
        store.Choose(PaceSource.LastFourWeeks, "backlog");
        var second = await ((IRoadmapReplicaStore)store).ReadAsync(Cancellation);

        Assert.Equal(Morning, first!.UpdatedAt);
        Assert.Equal(Morning.AddMinutes(3), second!.UpdatedAt);
        Assert.Equal(Morning.AddMinutes(3), (DateTimeOffset)JsonNode.Parse(File.ReadAllText(FileOf("a")))!["updatedAt"]!);
    }

    /// <summary>A file written before the pace travelled has no stamp. It is stamped
    /// from its last-write time, because that is when the reader last set it — the
    /// honest value, as <c>created_at</c> was for the tasks that predated
    /// <c>updated_at</c>.</summary>
    [Fact]
    public async Task A_file_from_before_the_stamp_reads_as_stamped_when_it_was_last_written()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileOf("a"))!);
        File.WriteAllText(FileOf("a"), """{ "storyPointsPerWeek": 4, "source": "Manual" }""");
        var written = new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(FileOf("a"), written);

        var copy = await ((IRoadmapReplicaStore)Store()).ReadAsync(Cancellation);

        Assert.NotNull(copy);
        Assert.Equal(new DateTimeOffset(written), copy.UpdatedAt);
        Assert.Equal(4m, (decimal)JsonNode.Parse(copy.Content)!["storyPointsPerWeek"]!);
    }

    /// <summary>ADR 0018 Verification 4, at the store: the typed pace, the source and
    /// a repository's pair set on one device read the same on another, at the stamp
    /// they were set at.</summary>
    [Fact]
    public async Task A_copy_from_another_device_is_read_as_the_same_paces()
    {
        var a = Store("a", new FakeTimeProvider(Morning));
        a.Set(12m);
        a.Choose(PaceSource.LastTwoWeeks);
        a.Set(3.5m, "backlog");
        var sent = (await ((IRoadmapReplicaStore)a).ReadAsync(Cancellation))!;

        var b = Store("b");
        var raised = 0;
        b.Changed += () => raised++;

        Assert.True(await ((IRoadmapReplicaStore)b).TryWriteAsync(sent, Cancellation));

        Assert.Equal(12m, b.StoryPointsPerWeek);
        Assert.Equal(PaceSource.LastTwoWeeks, b.Source);
        Assert.Equal(3.5m, b.StoryPointsPerWeekFor("backlog"));
        Assert.Equal(1, raised);
        Assert.Equal(sent.UpdatedAt, (await ((IRoadmapReplicaStore)b).ReadAsync(Cancellation))!.UpdatedAt);

        // And the file survives a restart as what arrived.
        Assert.Equal(3.5m, Store("b").StoryPointsPerWeekFor("backlog"));
    }

    /// <summary>Written as it arrived, with the inbound stamp — a key a newer build
    /// added survives here even though this build does not read it.</summary>
    [Fact]
    public async Task A_copy_keeps_the_keys_this_build_does_not_know()
    {
        var store = Store();

        await ((IRoadmapReplicaStore)store).TryWriteAsync(
            new RoadmapReplicaCopyDto("""{"storyPointsPerWeek":9,"source":"Manual","horizon":"quarter"}""", Morning),
            Cancellation);

        var written = JsonNode.Parse(File.ReadAllText(FileOf("a")))!;
        Assert.Equal("quarter", (string)written["horizon"]!);
        Assert.Equal(Morning, (DateTimeOffset)written["updatedAt"]!);
        Assert.Equal(9m, store.StoryPointsPerWeek);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[7]")]
    [InlineData("""{"storyPointsPerWeek":"lots"}""")]
    [InlineData("")]
    public async Task A_payload_that_is_not_a_pace_is_refused_and_the_file_is_untouched(string content)
    {
        var store = Store(time: new FakeTimeProvider(Morning));
        store.Set(5m);
        var before = File.ReadAllText(FileOf("a"));

        Assert.False(await ((IRoadmapReplicaStore)store).TryWriteAsync(
            new RoadmapReplicaCopyDto(content, Morning.AddDays(1)), Cancellation));

        Assert.Equal(before, File.ReadAllText(FileOf("a")));
        Assert.Equal(5m, store.StoryPointsPerWeek);
    }

    /// <summary>A change made after taking a copy from a PC whose clock runs ahead
    /// stamps past it, so the change is the later version rather than one the replica
    /// refuses.</summary>
    [Fact]
    public async Task A_change_after_taking_a_copy_from_the_future_stamps_past_it()
    {
        var store = Store(time: new FakeTimeProvider(Morning));
        var ahead = Morning.AddHours(3);
        await ((IRoadmapReplicaStore)store).TryWriteAsync(
            new RoadmapReplicaCopyDto("""{"storyPointsPerWeek":9}""", ahead), Cancellation);

        store.Set(4m);

        Assert.True((await ((IRoadmapReplicaStore)store).ReadAsync(Cancellation))!.UpdatedAt > ahead);
    }
}
