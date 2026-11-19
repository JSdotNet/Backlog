using System.Text.Json.Nodes;

using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.SharedKernel.Replication;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The iteration settings file (local ADR 0036, §1–§3, §5): the length and the two-week
/// start as one document; a copy that does not read as the settings left alone; and a
/// copy an earlier build seeded at the floor stamp. The stamping and the last-write-wins
/// rule both settings files share are <see cref="StampedSettingsStoreTests"/>'.
/// </summary>
public sealed class IterationSettingsStoreTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Monday19 = new(2026, 10, 19);
    private static readonly DateOnly Wednesday21 = new(2026, 10, 21);

    private readonly TemporaryDirectory _temp = new("iteration-settings-tests");

    private string _root => _temp.Combine("data");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string FileOf(string device) => Path.Combine(_root, device, IterationSettingsStore.FileName);

    private IterationSettingsStore Store(string device = "a", TimeProvider? time = null) =>
        new(FileOf(device), time ?? new FakeTimeProvider(Morning));

    public void Dispose() => _temp.Dispose();

    private static IterationSettingsCopyDto Copy(string length, DateOnly? start, DateTimeOffset at, string extra = "") =>
        new(
            $$"""{"updatedAt":"{{at:O}}","length":"{{length}}"{{(start is { } day ? $$""","twoWeekStart":"{{day:yyyy-MM-dd}}" """ : "")}}{{extra}}}""",
            at);

    private static IIterationSettingsReplication Replica(IterationSettingsStore store) => store;

    // --- The settings --------------------------------------------------------------

    /// <summary>ADR 0036 §4 "A device that never set it sends nothing": no file is Week,
    /// and no document to send.</summary>
    [Fact]
    public async Task With_no_file_there_is_no_choice_and_nothing_to_send()
    {
        var store = Store();

        Assert.Null(store.Choice);
        Assert.Null(await Replica(store).ReadAsync(Cancellation));
        Assert.False(File.Exists(FileOf("a")));
    }

    [Fact]
    public void A_choice_is_kept_for_next_time_with_its_stamp()
    {
        var store = Store();

        Assert.Null(store.Set("two-weeks", Monday19));

        Assert.Equal(new IterationChoice("two-weeks", Monday19), store.Choice);
        Assert.Equal(new IterationChoice("two-weeks", Monday19), Store().Choice);

        var written = JsonNode.Parse(File.ReadAllText(FileOf("a")))!;
        Assert.Equal("two-weeks", (string)written["length"]!);
        Assert.Equal("2026-10-19", (string)written["twoWeekStart"]!);
        Assert.Equal(Morning, (DateTimeOffset)written["updatedAt"]!);
    }

    [Fact]
    public void A_choice_without_a_start_writes_no_start()
    {
        var store = Store();

        store.Set("month", null);

        Assert.False(JsonNode.Parse(File.ReadAllText(FileOf("a")))!.AsObject().ContainsKey("twoWeekStart"));
        Assert.Equal(new IterationChoice("month", null), Store().Choice);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[\"week\"]")]
    [InlineData("""{"length":7}""")]
    [InlineData("""{"length":"two-weeks","twoWeekStart":"the fifth"}""")]
    [InlineData("")]
    public void A_file_that_cannot_be_read_is_no_choice(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileOf("a"))!);
        File.WriteAllText(FileOf("a"), content);

        Assert.Null(Store().Choice);
    }

    /// <summary>A person's change is news for the sync, and never a taken copy.</summary>
    [Fact]
    public void A_change_is_announced_to_the_sync_and_not_as_a_taken_copy()
    {
        var store = Store();
        var sync = 0;
        var taken = 0;
        Replica(store).Changed += () => sync++;
        store.Replaced += () => taken++;

        store.Set("day", null);

        Assert.Equal(1, sync);
        Assert.Equal(0, taken);
    }

    [Fact]
    public void Setting_the_choice_already_held_changes_nothing()
    {
        var store = Store();
        store.Set("two-weeks", Monday19);
        var before = File.ReadAllText(FileOf("a"));
        var sync = 0;
        Replica(store).Changed += () => sync++;

        Assert.Null(store.Set("two-weeks", Monday19));

        Assert.Equal(0, sync);
        Assert.Equal(before, File.ReadAllText(FileOf("a")));
    }

    // --- The document that travels ------------------------------------------------

    /// <summary>ADR 0036 Verification 2: a later stamp is taken whole — length and start
    /// together, at the inbound stamp, keys this build does not know included. The screen
    /// hears of it; the sync does not, so no push follows a pull.</summary>
    [Fact]
    public async Task A_later_copy_is_taken_whole_and_announced_as_taken_only()
    {
        var store = Store();
        store.Set("week", null);
        var sync = 0;
        var taken = 0;
        Replica(store).Changed += () => sync++;
        store.Replaced += () => taken++;
        var later = Morning.AddMinutes(10);

        var outcome = await Replica(store).ApplyAsync(
            Copy("two-weeks", Monday19, later, extra: ""","weekStartsOn":"sunday" """), Cancellation);

        Assert.Equal(ReplicaOutcome.Taken, outcome);
        Assert.Equal(new IterationChoice("two-weeks", Monday19), store.Choice);
        Assert.Equal(1, taken);
        Assert.Equal(0, sync);

        var written = JsonNode.Parse(File.ReadAllText(FileOf("a")))!;
        Assert.Equal("sunday", (string)written["weekStartsOn"]!);
        Assert.Equal(later, (DateTimeOffset)written["updatedAt"]!);
        Assert.Equal(new IterationChoice("two-weeks", Monday19), Store().Choice);
    }

    /// <summary>A length this build does not know is still taken verbatim, so it rides
    /// along (ADR 0036 §4); what it reads as is <see cref="IterationSettings.Parse"/>'s
    /// business.</summary>
    [Fact]
    public async Task A_length_this_build_does_not_know_is_taken_verbatim()
    {
        var store = Store();

        var outcome = await Replica(store).ApplyAsync(Copy("quarter", null, Morning), Cancellation);

        Assert.Equal(ReplicaOutcome.Taken, outcome);
        Assert.Equal("quarter", store.Choice!.Length);
    }

    /// <summary>ADR 0036 Verification 2: an unreadable payload leaves the local value.</summary>
    [Theory]
    [InlineData("not json")]
    [InlineData("[\"week\"]")]
    [InlineData("""{"twoWeekStart":"2026-10-19"}""")]
    [InlineData("""{"length":2}""")]
    [InlineData("""{"length":"two-weeks","twoWeekStart":"the fifth"}""")]
    [InlineData("")]
    public async Task A_copy_that_is_not_the_settings_is_unreadable_and_the_file_is_untouched(string content)
    {
        var store = Store();
        store.Set("two-weeks", Monday19);
        var before = File.ReadAllText(FileOf("a"));

        var outcome = await Replica(store).ApplyAsync(new IterationSettingsCopyDto(content, Morning.AddDays(1)), Cancellation);

        Assert.Equal(ReplicaOutcome.Unreadable, outcome);
        Assert.Equal(before, File.ReadAllText(FileOf("a")));
        Assert.Equal(new IterationChoice("two-weeks", Monday19), store.Choice);
    }

    // --- A copy an earlier build seeded at the floor (ADR 0036 §5) -----------------

    /// <summary>The file as an earlier build seeded it: the choice, at the floor stamp.</summary>
    private IterationSettingsStore Seeded(string device, string length, DateOnly? start)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileOf(device))!);
        File.WriteAllText(FileOf(device), Copy(length, start, IterationSettingsStore.FloorStamp).Content);
        return Store(device);
    }

    /// <summary>ADR 0036 Verification 5: a document already on the feed — any real stamp —
    /// wins over the seeded copy.</summary>
    [Fact]
    public async Task A_document_from_the_feed_wins_over_the_seeded_copy()
    {
        var store = Seeded("a", "two-weeks", Monday19);

        var outcome = await Replica(store).ApplyAsync(Copy("month", null, Morning), Cancellation);

        Assert.Equal(ReplicaOutcome.Taken, outcome);
        Assert.Equal(new IterationChoice("month", null), store.Choice);
    }

    /// <summary>ADR 0036 Verification 5 "Two seeded desktops converge": at the floor stamp
    /// alone, an equal stamp carrying another choice is taken rather than an echo.</summary>
    [Fact]
    public async Task At_the_floor_an_equal_stamp_with_another_choice_is_taken()
    {
        var store = Seeded("a", "two-weeks", Monday19);
        var taken = 0;
        store.Replaced += () => taken++;

        var outcome = await Replica(store).ApplyAsync(Copy("month", null, IterationSettingsStore.FloorStamp), Cancellation);

        Assert.Equal(ReplicaOutcome.Taken, outcome);
        Assert.Equal(new IterationChoice("month", null), store.Choice);
        Assert.Equal(1, taken);
        Assert.Equal(IterationSettingsStore.FloorStamp, (await Replica(store).ReadAsync(Cancellation))!.UpdatedAt);
    }

    /// <summary>This device's own seeded push coming back carries the choice it holds:
    /// there is nothing to take, so it is the echo it looks like.</summary>
    [Fact]
    public async Task At_the_floor_its_own_seeded_copy_coming_back_is_an_echo()
    {
        var store = Seeded("a", "two-weeks", Monday19);
        var before = File.ReadAllText(FileOf("a"));

        var outcome = await Replica(store).ApplyAsync(Copy("two-weeks", Monday19, IterationSettingsStore.FloorStamp), Cancellation);

        Assert.Equal(ReplicaOutcome.Echo, outcome);
        Assert.Equal(before, File.ReadAllText(FileOf("a")));
    }

    /// <summary>A change made after the upgrade is stamped normally, so it beats every
    /// seeded copy.</summary>
    [Fact]
    public async Task A_change_after_seeding_is_stamped_past_the_floor_and_beats_a_seeded_copy()
    {
        var store = Seeded("a", "two-weeks", Monday19);
        store.Set("month", Monday19);

        Assert.Equal(Morning, (await Replica(store).ReadAsync(Cancellation))!.UpdatedAt);
        Assert.Equal(
            ReplicaOutcome.Refused,
            await Replica(store).ApplyAsync(Copy("day", null, IterationSettingsStore.FloorStamp), Cancellation));
    }
}
