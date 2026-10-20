using System.Text.Json.Nodes;

using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The iteration settings file (local ADR 0036, §1–§3, §5): the length and the two-week
/// start as one document, every change stamped past the one held; the same file as the
/// document that travels, replaced whole by a later copy and left alone by an echo, an
/// older copy or one it cannot read; and the legacy per-device value seeding it once, at
/// the floor stamp.
/// </summary>
public sealed class IterationSettingsStoreTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Monday19 = new(2026, 10, 19);
    private static readonly DateOnly Wednesday21 = new(2026, 10, 21);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "iteration-settings-tests-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string FileOf(string device) => Path.Combine(_root, device, IterationSettingsStore.FileName);

    private string ShellOf(string device) => Path.Combine(_root, device, "shell-navigation.json");

    private IterationSettingsStore Store(string device = "a", ShellNavigationStore? legacy = null, TimeProvider? time = null) =>
        new(FileOf(device), legacy, time ?? new FakeTimeProvider(Morning));

    public void Dispose()
    {
        ShellNavigationStore.WaitForPendingWrites(ShellOf("a"));
        ShellNavigationStore.WaitForPendingWrites(ShellOf("b"));

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

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

    /// <summary>Two changes under a clock that has not moved are still two versions, the
    /// later one stamped past the earlier (the pace store's <c>Next</c>).</summary>
    [Fact]
    public async Task Every_change_is_stamped_past_the_one_before_under_a_frozen_clock()
    {
        var store = Store();

        store.Set("two-weeks", Monday19);
        var first = (await Replica(store).ReadAsync(Cancellation))!.UpdatedAt;

        store.Set("week", Monday19);
        var second = (await Replica(store).ReadAsync(Cancellation))!.UpdatedAt;

        Assert.Equal(Morning, first);
        Assert.True(second > first);
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

    [Fact]
    public void A_file_that_cannot_be_written_keeps_the_change_for_now_and_says_so()
    {
        // A folder where the file should be: every write is refused.
        Directory.CreateDirectory(FileOf("a"));
        var store = Store();

        var message = store.Set("two-weeks", Monday19);

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.Equal(new IterationChoice("two-weeks", Monday19), store.Choice);
    }

    // --- The document that travels ------------------------------------------------

    [Fact]
    public async Task The_document_is_the_file_as_stored_with_its_stamp()
    {
        var store = Store();
        store.Set("two-weeks", Monday19);

        var copy = (await Replica(store).ReadAsync(Cancellation))!;

        Assert.Equal(File.ReadAllText(FileOf("a")), copy.Content);
        Assert.Equal(Morning, copy.UpdatedAt);
    }

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

        Assert.Equal(IterationSettingsReplicaOutcome.Taken, outcome);
        Assert.Equal(new IterationChoice("two-weeks", Monday19), store.Choice);
        Assert.Equal(1, taken);
        Assert.Equal(0, sync);

        var written = JsonNode.Parse(File.ReadAllText(FileOf("a")))!;
        Assert.Equal("sunday", (string)written["weekStartsOn"]!);
        Assert.Equal(later, (DateTimeOffset)written["updatedAt"]!);
        Assert.Equal(new IterationChoice("two-weeks", Monday19), Store().Choice);
    }

    /// <summary>ADR 0036 Verification 4: a newly paired device that never chose takes the
    /// other device's choice.</summary>
    [Fact]
    public async Task A_device_with_no_file_takes_any_copy()
    {
        var store = Store();

        var outcome = await Replica(store).ApplyAsync(Copy("two-weeks", Monday19, Morning), Cancellation);

        Assert.Equal(IterationSettingsReplicaOutcome.Taken, outcome);
        Assert.Equal(new IterationChoice("two-weeks", Monday19), store.Choice);
    }

    /// <summary>A length this build does not know is still taken verbatim, so it rides
    /// along (ADR 0036 §4); what it reads as is <see cref="IterationSettings.Parse"/>'s
    /// business.</summary>
    [Fact]
    public async Task A_length_this_build_does_not_know_is_taken_verbatim()
    {
        var store = Store();

        var outcome = await Replica(store).ApplyAsync(Copy("quarter", null, Morning), Cancellation);

        Assert.Equal(IterationSettingsReplicaOutcome.Taken, outcome);
        Assert.Equal("quarter", store.Choice!.Length);
    }

    /// <summary>ADR 0036 Verification 2: an echo writes nothing.</summary>
    [Fact]
    public async Task A_copy_at_the_stamp_already_held_is_an_echo_and_writes_nothing()
    {
        var store = Store();
        store.Set("two-weeks", Monday19);
        var before = File.ReadAllText(FileOf("a"));

        var outcome = await Replica(store).ApplyAsync(Copy("month", null, Morning), Cancellation);

        Assert.Equal(IterationSettingsReplicaOutcome.Echo, outcome);
        Assert.Equal(before, File.ReadAllText(FileOf("a")));
        Assert.Equal(new IterationChoice("two-weeks", Monday19), store.Choice);
    }

    /// <summary>ADR 0036 Verification 2: an older stamp is refused.</summary>
    [Fact]
    public async Task An_older_copy_is_refused()
    {
        var store = Store();
        store.Set("two-weeks", Monday19);
        var before = File.ReadAllText(FileOf("a"));

        var outcome = await Replica(store).ApplyAsync(Copy("month", null, Morning.AddMinutes(-1)), Cancellation);

        Assert.Equal(IterationSettingsReplicaOutcome.Refused, outcome);
        Assert.Equal(before, File.ReadAllText(FileOf("a")));
        Assert.Equal(new IterationChoice("two-weeks", Monday19), store.Choice);
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

        Assert.Equal(IterationSettingsReplicaOutcome.Unreadable, outcome);
        Assert.Equal(before, File.ReadAllText(FileOf("a")));
        Assert.Equal(new IterationChoice("two-weeks", Monday19), store.Choice);
    }

    /// <summary>A change made after taking a copy from a PC whose clock runs ahead stamps
    /// past it, so it is the later version rather than one the replica refuses.</summary>
    [Fact]
    public async Task A_change_after_taking_a_copy_from_the_future_stamps_past_it()
    {
        var store = Store();
        var ahead = Morning.AddHours(3);
        await Replica(store).ApplyAsync(Copy("month", null, ahead), Cancellation);

        store.Set("week", null);

        Assert.True((await Replica(store).ReadAsync(Cancellation))!.UpdatedAt > ahead);
    }

    // --- The legacy per-device value (ADR 0036 §5) -----------------------------------

    private ShellNavigationStore LegacyChose(string device, string length, DateOnly? start)
    {
        var shell = new ShellNavigationStore(ShellOf(device));
        shell.SetGoalsIteration(length, start);
        shell.Flush();
        return new ShellNavigationStore(ShellOf(device));
    }

    /// <summary>ADR 0036 Verification 5: the legacy value seeds the file at the floor
    /// stamp, length and anchor, and that is the document to send.</summary>
    [Fact]
    public async Task The_legacy_value_seeds_the_file_at_the_floor_stamp()
    {
        var store = Store(legacy: LegacyChose("a", "two-weeks", Monday19));

        Assert.Equal(new IterationChoice("two-weeks", Monday19), store.Choice);

        var copy = (await Replica(store).ReadAsync(Cancellation))!;
        Assert.Equal(DateTimeOffset.UnixEpoch, copy.UpdatedAt);
        Assert.Equal(IterationSettingsStore.FloorStamp, copy.UpdatedAt);

        var written = JsonNode.Parse(File.ReadAllText(FileOf("a")))!;
        Assert.Equal("two-weeks", (string)written["length"]!);
        Assert.Equal("2026-10-19", (string)written["twoWeekStart"]!);
    }

    /// <summary>A legacy value written under a member name seeds as the setting's own
    /// value, so every desktop reads the document the same way.</summary>
    [Fact]
    public void A_legacy_member_name_seeds_as_the_settings_value()
    {
        var store = Store(legacy: LegacyChose("a", "TwoWeeks", Monday19));

        Assert.Equal("two-weeks", store.Choice!.Length);
    }

    /// <summary>Nothing chosen on the old build seeds nothing, so there is nothing to send.</summary>
    [Fact]
    public async Task No_legacy_value_seeds_nothing()
    {
        var store = Store(legacy: new ShellNavigationStore(ShellOf("a")));

        Assert.Null(store.Choice);
        Assert.Null(await Replica(store).ReadAsync(Cancellation));
        Assert.False(File.Exists(FileOf("a")));
    }

    /// <summary>Seeding happens once: a file already there is never replaced by the legacy
    /// value.</summary>
    [Fact]
    public void A_file_already_there_is_not_seeded_over()
    {
        Store().Set("month", null);

        var store = Store(legacy: LegacyChose("a", "two-weeks", Monday19));

        Assert.Equal(new IterationChoice("month", null), store.Choice);
    }

    /// <summary>ADR 0036 Verification 5: a document already on the feed — any real stamp —
    /// wins over the seeded copy.</summary>
    [Fact]
    public async Task A_document_from_the_feed_wins_over_the_seeded_copy()
    {
        var store = Store(legacy: LegacyChose("a", "two-weeks", Monday19));

        var outcome = await Replica(store).ApplyAsync(Copy("month", null, Morning), Cancellation);

        Assert.Equal(IterationSettingsReplicaOutcome.Taken, outcome);
        Assert.Equal(new IterationChoice("month", null), store.Choice);
    }

    /// <summary>ADR 0036 Verification 5 "Two seeded desktops converge": at the floor stamp
    /// alone, an equal stamp carrying another choice is taken rather than an echo.</summary>
    [Fact]
    public async Task At_the_floor_an_equal_stamp_with_another_choice_is_taken()
    {
        var store = Store(legacy: LegacyChose("a", "two-weeks", Monday19));
        var taken = 0;
        store.Replaced += () => taken++;

        var outcome = await Replica(store).ApplyAsync(Copy("month", null, IterationSettingsStore.FloorStamp), Cancellation);

        Assert.Equal(IterationSettingsReplicaOutcome.Taken, outcome);
        Assert.Equal(new IterationChoice("month", null), store.Choice);
        Assert.Equal(1, taken);
        Assert.Equal(IterationSettingsStore.FloorStamp, (await Replica(store).ReadAsync(Cancellation))!.UpdatedAt);
    }

    /// <summary>This device's own seeded push coming back carries the choice it holds:
    /// there is nothing to take, so it is the echo it looks like.</summary>
    [Fact]
    public async Task At_the_floor_its_own_seeded_copy_coming_back_is_an_echo()
    {
        var store = Store(legacy: LegacyChose("a", "two-weeks", Monday19));
        var before = File.ReadAllText(FileOf("a"));

        var outcome = await Replica(store).ApplyAsync(Copy("two-weeks", Monday19, IterationSettingsStore.FloorStamp), Cancellation);

        Assert.Equal(IterationSettingsReplicaOutcome.Echo, outcome);
        Assert.Equal(before, File.ReadAllText(FileOf("a")));
    }

    /// <summary>A change made after the upgrade is stamped normally, so it beats every
    /// seeded copy.</summary>
    [Fact]
    public async Task A_change_after_seeding_is_stamped_past_the_floor_and_beats_a_seeded_copy()
    {
        var store = Store(legacy: LegacyChose("a", "two-weeks", Monday19));
        store.Set("month", Monday19);

        Assert.Equal(Morning, (await Replica(store).ReadAsync(Cancellation))!.UpdatedAt);
        Assert.Equal(
            IterationSettingsReplicaOutcome.Refused,
            await Replica(store).ApplyAsync(Copy("day", null, IterationSettingsStore.FloorStamp), Cancellation));
    }

    /// <summary>ADR 0036 §5: after seeding, the shell navigation file's two fields are no
    /// longer written — a change goes to the new file only.</summary>
    [Fact]
    public void A_change_after_seeding_leaves_the_legacy_fields_alone()
    {
        var legacy = LegacyChose("a", "two-weeks", Monday19);
        var settings = new IterationSettings(Store(legacy: legacy));

        settings.SetLength(IterationLength.Month, Wednesday21);
        legacy.Flush();

        var shell = new ShellNavigationStore(ShellOf("a"));
        Assert.Equal("two-weeks", shell.GoalsIterationLength);
        Assert.Equal(Monday19, shell.GoalsTwoWeekStart);
        Assert.Equal("month", Store().Choice!.Length);
    }
}
