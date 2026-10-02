using System.Text.Json.Nodes;

using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.SharedKernel;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The working week travelling inside the pace document (local ADR 0019, §3): a change
/// to the device's week writes <c>workingWeek</c> into the pace file and stamps it, a
/// pulled copy carrying the key replaces the device's <c>working-hours.json</c> at the
/// inbound stamp, and a copy without it leaves the device's week alone.
/// </summary>
public sealed class PlanningVelocityWorkingWeekTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);

    private static readonly TimeOnly Nine = new(9, 0);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "planning-velocity-week-tests-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string PaceFileOf(string device) => Path.Combine(_root, device, "planning-velocity.json");

    private string WeekFileOf(string device) => Path.Combine(_root, device, "working-hours.json");

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

    private (WorkingHoursSettingsStore Week, PlanningVelocitySettingsStore Pace) Device(string name, TimeProvider? time = null)
    {
        var week = new WorkingHoursSettingsStore(WeekFileOf(name));
        return (week, new PlanningVelocitySettingsStore(PaceFileOf(name), time, week));
    }

    private static IRoadmapReplicaStore Replica(PlanningVelocitySettingsStore pace) => pace;

    /// <summary>ADR 0019 Verification 7, at the stores: A edits its working week; the
    /// pace document carries it, stamped; B takes the document and its
    /// <c>working-hours.json</c> holds A's week — at A's stamp, so B sends nothing
    /// back.</summary>
    [Fact]
    public async Task The_working_week_travels_with_the_pace()
    {
        var clock = new FakeTimeProvider(Morning);
        var a = Device("a", clock);
        var b = Device("b");
        Assert.Null(a.Pace.Set(9m)); // A keeps a pace, so it has a document to carry the week

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(a.Week.SetDay(DayOfWeek.Friday, true, Nine, new TimeOnly(13, 0)));

        var sent = await Replica(a.Pace).ReadAsync(Cancellation);
        Assert.NotNull(sent);
        Assert.Equal(Morning.AddMinutes(1), sent.UpdatedAt);
        Assert.Equal("13:00", (string)JsonNode.Parse(sent.Content)!["workingWeek"]!["days"]![4]!["end"]!);

        var heard = 0;
        b.Week.Changed += () => heard++;
        Assert.True(await Replica(b.Pace).TryWriteAsync(sent, Cancellation));

        Assert.True(WorkingHoursSettingsStore.SameWeek(a.Week.Current, b.Week.Current));
        Assert.True(WorkingHoursSettingsStore.SameWeek(a.Week.Current, new WorkingHoursSettingsStore(WeekFileOf("b")).Current));
        Assert.True(WorkingHoursSettingsStore.SameWeek(a.Week.Current, b.Pace.WorkingWeek));
        Assert.Equal(1, heard); // the dashboard's grid hears the new week

        // Held at the inbound stamp, not restamped as B's own change.
        Assert.Equal(sent.UpdatedAt, (await Replica(b.Pace).ReadAsync(Cancellation))!.UpdatedAt);
    }

    /// <summary>ADR 0019 §3 with ADR 0018 §3: a device that never set a pace still sends
    /// nothing. Editing its week writes <c>working-hours.json</c> alone — no pace file is
    /// created, so there is no document to push the default pace over another device's.
    /// The key follows at its first pace change.</summary>
    [Fact]
    public async Task Editing_the_week_on_a_device_with_no_pace_file_creates_no_document()
    {
        var a = Device("a");

        Assert.Null(a.Week.SetDay(DayOfWeek.Friday, true, Nine, new TimeOnly(13, 0)));

        Assert.False(File.Exists(PaceFileOf("a")));
        Assert.True(File.Exists(WeekFileOf("a")));
        Assert.Null(await Replica(a.Pace).ReadAsync(Cancellation));
        Assert.Equal(TimeSpan.FromHours(4), a.Pace.WorkingWeek.WorkedOn(DayOfWeek.Friday));

        Assert.Null(a.Pace.Set(9m));

        var first = JsonNode.Parse((await Replica(a.Pace).ReadAsync(Cancellation))!.Content)!;
        Assert.Equal("13:00", (string)first["workingWeek"]!["days"]![4]!["end"]!);
    }

    /// <summary>Once the pace file exists, a week edit writes the key and stamps the
    /// document, so it travels at the next push.</summary>
    [Fact]
    public async Task Editing_the_week_with_a_pace_file_stamps_the_document()
    {
        var clock = new FakeTimeProvider(Morning);
        var a = Device("a", clock);
        Assert.Null(a.Pace.Set(9m));

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Null(a.Week.ResetToDefault());

        var written = await Replica(a.Pace).ReadAsync(Cancellation);
        Assert.NotNull(written);
        Assert.Equal(Morning.AddMinutes(5), written.UpdatedAt);
        Assert.NotNull(JsonNode.Parse(written.Content)!["workingWeek"]);
    }

    /// <summary>ADR 0019 Verification 8: a pace document without <c>workingWeek</c>
    /// leaves the device's <c>working-hours.json</c> alone and placement reads that
    /// file; the device's next pace change writes the key.</summary>
    [Fact]
    public async Task An_absent_key_reads_the_device_week_and_the_next_change_writes_it()
    {
        var b = Device("b");
        Assert.Null(b.Week.SetDay(DayOfWeek.Monday, false, Nine, new TimeOnly(17, 30)));
        var before = File.ReadAllText(WeekFileOf("b"));

        var older = new RoadmapReplicaCopyDto("""{ "storyPointsPerWeek": 9, "source": "Manual" }""", Morning.AddDays(1));
        Assert.True(await Replica(b.Pace).TryWriteAsync(older, Cancellation));

        Assert.Equal(9m, b.Pace.StoryPointsPerWeek);
        Assert.Equal(before, File.ReadAllText(WeekFileOf("b")));
        Assert.False(b.Pace.WorkingWeek.IsWorked(DayOfWeek.Monday));
        Assert.Null(JsonNode.Parse((await Replica(b.Pace).ReadAsync(Cancellation))!.Content)!["workingWeek"]);

        Assert.Null(b.Pace.Set(10m));

        var next = JsonNode.Parse((await Replica(b.Pace).ReadAsync(Cancellation))!.Content)!;
        Assert.False((bool)next["workingWeek"]!["days"]![0]!["working"]!);
        Assert.True(WorkingHoursSettingsStore.SameWeek(
            b.Week.Current,
            WorkingHoursSettingsStore.FromJson(next["workingWeek"])!));
    }

    /// <summary>A week the document carries that this build cannot read costs the
    /// week, never the pace, and leaves the device's week as it was.</summary>
    [Fact]
    public async Task An_unreadable_week_leaves_the_device_week_alone()
    {
        var b = Device("b");
        Assert.Null(b.Week.SetDay(DayOfWeek.Tuesday, false, Nine, new TimeOnly(17, 30)));

        var copy = new RoadmapReplicaCopyDto("""{ "storyPointsPerWeek": 6, "workingWeek": "every day" }""", Morning);
        Assert.True(await Replica(b.Pace).TryWriteAsync(copy, Cancellation));

        Assert.Equal(6m, b.Pace.StoryPointsPerWeek);
        Assert.False(b.Week.Current.IsWorked(DayOfWeek.Tuesday));
    }

    /// <summary>A store given no device week reads the week its file carries, and the
    /// default week when it carries none.</summary>
    [Fact]
    public void Without_a_device_week_the_file_is_read()
    {
        Directory.CreateDirectory(Path.Combine(_root, "c"));
        File.WriteAllText(PaceFileOf("c"), """
            { "storyPointsPerWeek": 5,
              "workingWeek": { "days": [ { "day": "Friday", "working": true, "start": "09:00", "end": "13:00" } ] } }
            """);

        var pace = new PlanningVelocitySettingsStore(PaceFileOf("c"));

        Assert.Equal(TimeSpan.FromHours(4), pace.WorkingWeek.WorkedOn(DayOfWeek.Friday));
        Assert.Equal(TimeSpan.FromHours(8.5), pace.WorkingWeek.WorkedOn(DayOfWeek.Monday));
        Assert.Equal(WorkingHours.Default.PerWeek, new PlanningVelocitySettingsStore(PaceFileOf("d")).WorkingWeek.PerWeek);
    }

    /// <summary>A change to the week is news to the roadmap, which redraws every bar
    /// still sized by its effort.</summary>
    [Fact]
    public void A_week_change_raises_the_pace_change()
    {
        var a = Device("a");
        var heard = 0;
        a.Pace.Changed += () => heard++;

        Assert.Null(a.Week.SetDay(DayOfWeek.Saturday, true, Nine, new TimeOnly(12, 0)));

        Assert.Equal(1, heard);
        Assert.True(a.Pace.WorkingWeek.IsWorked(DayOfWeek.Saturday));
    }
}
