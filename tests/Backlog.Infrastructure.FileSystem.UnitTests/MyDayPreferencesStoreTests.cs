using System.Text.Json.Nodes;

using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The My Day preferences file (local ADR 0032, §1–§2): one switch, Clear My Day
/// overnight, on until the person turns it off; every change stamped past the one held;
/// and the same file as the document that travels, replaced whole by a later copy and
/// left alone by an echo, an older copy or one it cannot read.
/// </summary>
public sealed class MyDayPreferencesStoreTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "my-day-preferences-tests-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string FileOf(string device) => Path.Combine(_root, device, "my-day-preferences.json");

    private MyDayPreferencesStore Store(string device = "a", TimeProvider? time = null) =>
        new(FileOf(device), time ?? new FakeTimeProvider(Morning));

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

    private static MyDayPreferencesCopyDto Copy(bool clearsOvernight, DateTimeOffset at, string extra = "") =>
        new($$"""{"updatedAt":"{{at:O}}","clearsOvernight":{{(clearsOvernight ? "true" : "false")}}{{extra}}}""", at);

    // --- The switch --------------------------------------------------------------

    /// <summary>ADR 0032, §1 "The default": no file is today's rule, and no
    /// document to send.</summary>
    [Fact]
    public async Task With_no_file_my_day_clears_overnight_and_there_is_nothing_to_send()
    {
        var store = Store();

        Assert.True(store.ClearsOvernight);
        Assert.Null(await ((IMyDayPreferencesReplication)store).ReadAsync(Cancellation));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[false]")]
    [InlineData("""{"clearsOvernight":"no"}""")]
    [InlineData("")]
    public void A_file_that_cannot_be_read_clears_overnight(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileOf("a"))!);
        File.WriteAllText(FileOf("a"), content);

        Assert.True(Store().ClearsOvernight);
    }

    [Fact]
    public void Turning_the_switch_off_is_kept_for_next_time_with_its_stamp()
    {
        var store = Store();

        Assert.Null(store.SetClearsOvernight(false));

        Assert.False(store.ClearsOvernight);
        Assert.False(Store().ClearsOvernight);

        var written = JsonNode.Parse(File.ReadAllText(FileOf("a")))!;
        Assert.False((bool)written["clearsOvernight"]!);
        Assert.Equal(Morning, (DateTimeOffset)written["updatedAt"]!);
    }

    /// <summary>Two changes under a clock that has not moved are still two versions,
    /// the later one stamped past the earlier (the pace store's <c>Next</c>).</summary>
    [Fact]
    public async Task Every_change_is_stamped_past_the_one_before_under_a_frozen_clock()
    {
        var store = Store();
        IMyDayPreferencesReplication replica = store;

        store.SetClearsOvernight(false);
        var first = (await replica.ReadAsync(Cancellation))!.UpdatedAt;

        store.SetClearsOvernight(true);
        var second = (await replica.ReadAsync(Cancellation))!.UpdatedAt;

        Assert.Equal(Morning, first);
        Assert.True(second > first);
    }

    [Fact]
    public void A_change_is_announced_once_to_the_screen_and_to_the_sync()
    {
        var store = Store();
        var screen = 0;
        var sync = 0;
        ((IMyDayPreferences)store).Changed += () => screen++;
        ((IMyDayPreferencesReplication)store).Changed += () => sync++;

        store.SetClearsOvernight(false);

        Assert.Equal(1, screen);
        Assert.Equal(1, sync);
    }

    /// <summary>Setting the value it already has writes nothing and says nothing, so a
    /// device that never turned the switch off still has no document to send.</summary>
    [Fact]
    public async Task Setting_the_value_already_held_changes_nothing()
    {
        var store = Store();
        var raised = 0;
        ((IMyDayPreferences)store).Changed += () => raised++;
        ((IMyDayPreferencesReplication)store).Changed += () => raised++;

        Assert.Null(store.SetClearsOvernight(true));

        Assert.Equal(0, raised);
        Assert.False(File.Exists(FileOf("a")));
        Assert.Null(await ((IMyDayPreferencesReplication)store).ReadAsync(Cancellation));
    }

    [Fact]
    public void A_file_that_cannot_be_written_keeps_the_change_for_now_and_says_so()
    {
        // A folder where the file should be: every write is refused.
        Directory.CreateDirectory(FileOf("a"));
        var store = Store();

        var message = store.SetClearsOvernight(false);

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.False(store.ClearsOvernight);
    }

    // --- The document that travels ----------------------------------------------

    [Fact]
    public async Task The_document_is_the_file_as_stored_with_its_stamp()
    {
        var store = Store();
        store.SetClearsOvernight(false);

        var copy = (await ((IMyDayPreferencesReplication)store).ReadAsync(Cancellation))!;

        Assert.Equal(File.ReadAllText(FileOf("a")), copy.Content);
        Assert.Equal(Morning, copy.UpdatedAt);
    }

    /// <summary>ADR 0032 Verification 2: a later stamp is taken whole, at the inbound
    /// stamp, keys this build does not know included. The screen hears of it; the
    /// sync does not, so no push follows a pull.</summary>
    [Fact]
    public async Task A_later_copy_is_taken_whole_and_announced_to_the_screen_only()
    {
        var store = Store();
        store.SetClearsOvernight(false);
        var screen = 0;
        var sync = 0;
        ((IMyDayPreferences)store).Changed += () => screen++;
        ((IMyDayPreferencesReplication)store).Changed += () => sync++;
        var later = Morning.AddMinutes(10);

        var outcome = await ((IMyDayPreferencesReplication)store).ApplyAsync(
            Copy(clearsOvernight: true, later, extra: ""","agendaStart":"09:00" """), Cancellation);

        Assert.Equal(MyDayPreferencesReplicaOutcome.Taken, outcome);
        Assert.True(store.ClearsOvernight);
        Assert.Equal(1, screen);
        Assert.Equal(0, sync);

        var written = JsonNode.Parse(File.ReadAllText(FileOf("a")))!;
        Assert.Equal("09:00", (string)written["agendaStart"]!);
        Assert.Equal(later, (DateTimeOffset)written["updatedAt"]!);
        Assert.True(Store().ClearsOvernight);
    }

    /// <summary>A newly paired device that never set the switch takes the other
    /// device's choice.</summary>
    [Fact]
    public async Task A_device_with_no_file_takes_any_copy()
    {
        var store = Store();

        var outcome = await ((IMyDayPreferencesReplication)store).ApplyAsync(Copy(false, Morning), Cancellation);

        Assert.Equal(MyDayPreferencesReplicaOutcome.Taken, outcome);
        Assert.False(store.ClearsOvernight);
    }

    [Fact]
    public async Task A_copy_at_the_stamp_already_held_is_an_echo_and_writes_nothing()
    {
        var store = Store();
        store.SetClearsOvernight(false);
        var before = File.ReadAllText(FileOf("a"));

        var outcome = await ((IMyDayPreferencesReplication)store).ApplyAsync(Copy(true, Morning), Cancellation);

        Assert.Equal(MyDayPreferencesReplicaOutcome.Echo, outcome);
        Assert.Equal(before, File.ReadAllText(FileOf("a")));
        Assert.False(store.ClearsOvernight);
    }

    [Fact]
    public async Task An_older_copy_is_refused()
    {
        var store = Store();
        store.SetClearsOvernight(false);
        var before = File.ReadAllText(FileOf("a"));

        var outcome = await ((IMyDayPreferencesReplication)store).ApplyAsync(Copy(true, Morning.AddMinutes(-1)), Cancellation);

        Assert.Equal(MyDayPreferencesReplicaOutcome.Refused, outcome);
        Assert.Equal(before, File.ReadAllText(FileOf("a")));
        Assert.False(store.ClearsOvernight);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[true]")]
    [InlineData("""{"clearsOvernight":"no"}""")]
    [InlineData("")]
    public async Task A_copy_that_is_not_the_preferences_is_unreadable_and_the_file_is_untouched(string content)
    {
        var store = Store();
        store.SetClearsOvernight(false);
        var before = File.ReadAllText(FileOf("a"));

        var outcome = await ((IMyDayPreferencesReplication)store).ApplyAsync(
            new MyDayPreferencesCopyDto(content, Morning.AddDays(1)), Cancellation);

        Assert.Equal(MyDayPreferencesReplicaOutcome.Unreadable, outcome);
        Assert.Equal(before, File.ReadAllText(FileOf("a")));
        Assert.False(store.ClearsOvernight);
    }

    /// <summary>A change made after taking a copy from a PC whose clock runs ahead
    /// stamps past it, so it is the later version rather than one the replica
    /// refuses.</summary>
    [Fact]
    public async Task A_change_after_taking_a_copy_from_the_future_stamps_past_it()
    {
        var store = Store();
        var ahead = Morning.AddHours(3);
        await ((IMyDayPreferencesReplication)store).ApplyAsync(Copy(false, ahead), Cancellation);

        store.SetClearsOvernight(true);

        Assert.True((await ((IMyDayPreferencesReplication)store).ReadAsync(Cancellation))!.UpdatedAt > ahead);
    }
}
