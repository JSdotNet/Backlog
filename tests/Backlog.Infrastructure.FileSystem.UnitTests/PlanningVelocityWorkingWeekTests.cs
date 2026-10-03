using System.Text.Json.Nodes;

using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
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
        Assert.Null(a.Week.SetDay(DayOfWeek.Friday, true, Nine, new TimeOnly(13, 0)));

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

    /// <summary>An object with no day this build can read — empty, or a shape a later
    /// build writes — is no week either: it leaves the device's week as it was rather
    /// than replacing it with the default.</summary>
    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{ "pattern": "Mon-Fri", "hours": 40 }""")]
    public async Task A_week_object_with_no_readable_day_leaves_the_device_week_alone(string workingWeek)
    {
        var b = Device("b");
        Assert.Null(b.Week.SetDay(DayOfWeek.Tuesday, false, Nine, new TimeOnly(17, 30)));

        var copy = new RoadmapReplicaCopyDto($$"""{ "storyPointsPerWeek": 6, "workingWeek": {{workingWeek}} }""", Morning);
        Assert.True(await Replica(b.Pace).TryWriteAsync(copy, Cancellation));

        Assert.Equal(6m, b.Pace.StoryPointsPerWeek);
        Assert.False(b.Week.Current.IsWorked(DayOfWeek.Tuesday));
        Assert.False(b.Pace.WorkingWeek.IsWorked(DayOfWeek.Tuesday));
    }

    /// <summary>Going back to the default week on a device already on it is no change,
    /// so the pace document keeps its stamp: a re-stamp would let this device's pace win
    /// over a newer one from another device.</summary>
    [Fact]
    public async Task Going_back_to_the_default_week_from_it_leaves_the_stamp()
    {
        var clock = new FakeTimeProvider(Morning);
        var a = Device("a", clock);
        Assert.Null(a.Pace.Set(9m));

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Null(a.Week.ResetToDefault());

        Assert.Equal(Morning, (await Replica(a.Pace).ReadAsync(Cancellation))!.UpdatedAt);
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

    // --- Day overrides (local ADR 0019, §§3 and 5) ---------------------------

    private static readonly DateOnly Friday9Oct = new(2026, 10, 9);

    /// <summary>A toggle on the axis stamps the pace document the way a settings change
    /// does, and the override travels inside <c>workingWeek</c>.</summary>
    [Fact]
    public async Task A_toggle_stamps_the_document_and_writes_the_override_inside_the_week()
    {
        var clock = new FakeTimeProvider(Morning);
        var a = Device("a", clock);
        Assert.Null(a.Pace.Set(9m));

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Null(a.Week.ToggleDate(Friday9Oct));

        var written = await Replica(a.Pace).ReadAsync(Cancellation);
        Assert.NotNull(written);
        Assert.Equal(Morning.AddMinutes(5), written.UpdatedAt);

        var overrides = JsonNode.Parse(written.Content)!["workingWeek"]!["overrides"]!.AsArray();
        Assert.Equal("2026-10-09", (string)Assert.Single(overrides)!["date"]!);
        Assert.False((bool)overrides[0]!["worked"]!);
    }

    /// <summary>ADR 0019 Verification 15 and requirement "A blocked day reaches the
    /// other device": A blocks a date and syncs; B holds the same override in its
    /// <c>working-hours.json</c> too, and a later pace change on B keeps it.</summary>
    [Fact]
    public async Task Overrides_travel_and_survive_a_later_pace_change()
    {
        var clock = new FakeTimeProvider(Morning);
        var a = Device("a", clock);
        var b = Device("b", new FakeTimeProvider(Morning.AddHours(1)));
        Assert.Null(a.Pace.Set(9m));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(a.Week.ToggleDate(Friday9Oct));

        Assert.True(await Replica(b.Pace).TryWriteAsync((await Replica(a.Pace).ReadAsync(Cancellation))!, Cancellation));

        Assert.False(b.Week.Current.IsWorked(Friday9Oct));
        Assert.False(b.Pace.WorkingWeek.IsWorked(Friday9Oct));
        Assert.False(new WorkingHoursSettingsStore(WeekFileOf("b")).Current.IsWorked(Friday9Oct));

        Assert.Null(b.Pace.Set(10m));

        var next = JsonNode.Parse((await Replica(b.Pace).ReadAsync(Cancellation))!.Content)!;
        Assert.False(WorkingHoursSettingsStore.FromJson(next["workingWeek"])!.IsWorked(Friday9Oct));
    }

    /// <summary>Requirement "A newer change to the week wins with its overrides": the
    /// week travels whole under one stamp, so B's newer change to Monday brings B's
    /// overrides — none — and Friday 9 October is worked on both devices.</summary>
    [Fact]
    public async Task A_newer_change_to_the_week_wins_with_its_overrides()
    {
        var clockA = new FakeTimeProvider(Morning);
        var clockB = new FakeTimeProvider(Morning);
        var a = Device("a", clockA);
        var b = Device("b", clockB);
        Assert.Null(a.Pace.Set(9m));
        Assert.Null(b.Pace.Set(9m));

        clockA.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(a.Week.ToggleDate(Friday9Oct));

        clockB.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(b.Week.SetDay(DayOfWeek.Monday, true, new TimeOnly(8, 0), new TimeOnly(16, 0)));

        // Last write wins: B's document is the newer one, and A takes it.
        var fromB = (await Replica(b.Pace).ReadAsync(Cancellation))!;
        Assert.True(fromB.UpdatedAt > (await Replica(a.Pace).ReadAsync(Cancellation))!.UpdatedAt);
        Assert.True(await Replica(a.Pace).TryWriteAsync(fromB, Cancellation));

        Assert.True(WorkingHoursSettingsStore.SameWeek(a.Week.Current, b.Week.Current));
        Assert.True(a.Week.Current.IsWorked(Friday9Oct));
        Assert.Empty(a.Week.Current.Overrides);
    }

    /// <summary>ADR 0019 Verification 16: a <c>workingWeek</c> without the array reads
    /// as no overrides, and places as the pattern alone.</summary>
    [Fact]
    public async Task A_week_without_overrides_reads_none()
    {
        var b = Device("b");
        Assert.Null(b.Week.ToggleDate(Friday9Oct));

        var copy = new RoadmapReplicaCopyDto("""
            { "storyPointsPerWeek": 7,
              "workingWeek": { "days": [ { "day": "Monday", "working": true, "start": "09:00", "end": "17:30" } ] } }
            """, Morning);
        Assert.True(await Replica(b.Pace).TryWriteAsync(copy, Cancellation));

        Assert.Empty(b.Week.Current.Overrides);
        Assert.Empty(b.Pace.WorkingWeek.Overrides);
        Assert.Equal(new DateOnly(2026, 10, 9), b.Pace.WorkingWeek.LastDayOf(new DateOnly(2026, 10, 5), 7m, 7m));
    }

    /// <summary>A pulled copy carrying overrides keeps every key it carries, the ones this
    /// build does not read included.</summary>
    [Fact]
    public async Task A_copy_with_overrides_keeps_the_keys_this_build_does_not_know()
    {
        var b = Device("b");

        var copy = new RoadmapReplicaCopyDto("""
            { "storyPointsPerWeek": 7, "horizon": "quarter",
              "workingWeek": {
                "days": [ { "day": "Monday", "working": true, "start": "09:00", "end": "17:30" } ],
                "overrides": [ { "date": "2026-10-09", "worked": false, "reason": "leave" } ],
                "timeZone": "Europe/Amsterdam" } }
            """, Morning);
        Assert.True(await Replica(b.Pace).TryWriteAsync(copy, Cancellation));

        var written = JsonNode.Parse(File.ReadAllText(PaceFileOf("b")))!;
        Assert.Equal("quarter", (string)written["horizon"]!);
        Assert.Equal("Europe/Amsterdam", (string)written["workingWeek"]!["timeZone"]!);
        Assert.Equal("leave", (string)written["workingWeek"]!["overrides"]![0]!["reason"]!);
        Assert.False(b.Week.Current.IsWorked(Friday9Oct));
    }

    /// <summary>The roadmap's port toggles through the device's week, and the roadmap
    /// hears of it at once, so the band re-places its bars (ADR 0019 Verification 23).</summary>
    [Fact]
    public void The_roadmap_port_toggles_a_date_and_announces_it()
    {
        var a = Device("a");
        Assert.Null(a.Pace.Set(9m));
        IPlanningVelocitySettings port = new PlanningVelocitySource(a.Pace);
        var heard = 0;
        port.Changed += () => heard++;

        Assert.Null(port.ToggleWorkedDay(Friday9Oct));

        Assert.Equal(1, heard);
        Assert.False(port.WorkingWeek.IsWorked(Friday9Oct));
        Assert.False(a.Week.Current.IsWorked(Friday9Oct));
    }

    /// <summary>A store that keeps no device week has none to toggle, and says so.</summary>
    [Fact]
    public void Without_a_device_week_a_toggle_is_refused()
    {
        IPlanningVelocitySettings port = new PlanningVelocitySource(new PlanningVelocitySettingsStore(PaceFileOf("c")));
        var heard = 0;
        port.Changed += () => heard++;

        Assert.NotNull(port.ToggleWorkedDay(Friday9Oct));

        Assert.Equal(0, heard);
        Assert.True(port.WorkingWeek.IsWorked(Friday9Oct));
    }
}
