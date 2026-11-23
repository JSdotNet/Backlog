using System.Text.Json.Nodes;

using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Replication;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The My Day preferences file (local ADR 0032, §1–§2): one switch, Clear My Day
/// overnight, on until the person turns it off; the screen and the sync told apart; and a
/// copy that does not read as the preferences left alone. The stamping and the
/// last-write-wins rule both settings files share are <see cref="StampedSettingsStoreTests"/>'.
/// </summary>
public sealed class MyDayPreferencesStoreTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _temp = new("my-day-preferences-tests");

    private string _root => _temp.Combine("data");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string FileOf(string device) => Path.Combine(_root, device, "my-day-preferences.json");

    private MyDayPreferencesStore Store(string device = "a", TimeProvider? time = null) =>
        new(FileOf(device), time ?? new FakeTimeProvider(Morning));

    public void Dispose() => _temp.Dispose();

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

    // --- The document that travels ----------------------------------------------

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

        Assert.Equal(ReplicaOutcome.Taken, outcome);
        Assert.True(store.ClearsOvernight);
        Assert.Equal(1, screen);
        Assert.Equal(0, sync);

        var written = JsonNode.Parse(File.ReadAllText(FileOf("a")))!;
        Assert.Equal("09:00", (string)written["agendaStart"]!);
        Assert.Equal(later, (DateTimeOffset)written["updatedAt"]!);
        Assert.True(Store().ClearsOvernight);
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

        Assert.Equal(ReplicaOutcome.Unreadable, outcome);
        Assert.Equal(before, File.ReadAllText(FileOf("a")));
        Assert.False(store.ClearsOvernight);
    }

}
