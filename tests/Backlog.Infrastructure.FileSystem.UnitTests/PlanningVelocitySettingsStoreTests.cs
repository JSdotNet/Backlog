using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The reader's pace on disk, on <see cref="WorkingHoursSettingsStoreTests"/>'
/// terms: what an untouched store answers, what survives a restart, what is
/// refused, and what a file nobody should have hand-edited reads as.
/// <para>
/// The refusals matter more here than in the stores beside it, because the figure
/// is a divisor: the roadmap divides gathered effort by it to decide how long a
/// bar is (ADR 0013, ruling 4). A zero or a negative reaching the store would not
/// be a wrong window, it would be a crash or a bar running backwards — so it never
/// reaches it, from the field or from the file.
/// </para>
/// </summary>
public class PlanningVelocitySettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "planning-velocity-settings-store-tests-" + Guid.NewGuid().ToString("N"));

    public PlanningVelocitySettingsStoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string SettingsFile => Path.Combine(_root, "planning-velocity.json");

    private PlanningVelocitySettingsStore Store() => new(SettingsFile);

    [Fact]
    public void An_untouched_store_reads_one_point_a_day_and_writes_no_file()
    {
        var store = Store();

        Assert.Equal(1m, store.StoryPointsPerDay);
        Assert.False(File.Exists(SettingsFile));
    }

    [Fact]
    public void A_set_pace_survives_a_restart()
    {
        Assert.Null(Store().Set(2.5m));

        var reopened = Store();

        Assert.Equal(2.5m, reopened.StoryPointsPerDay);
    }

    [Fact]
    public void Setting_a_pace_announces_the_change_once_and_says_nothing_when_it_is_unchanged()
    {
        var store = Store();

        var raised = 0;
        store.Changed += () => raised++;

        Assert.Null(store.Set(3m));
        Assert.Equal(1, raised);

        // Setting what is already set is not a change, so nothing is announced and
        // nothing is rewritten.
        Assert.Null(store.Set(3m));
        Assert.Equal(1, raised);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.5)]
    public void A_pace_of_zero_or_less_is_refused_and_changes_nothing(double refused)
    {
        var store = Store();
        _ = store.Set(4m);

        var raised = 0;
        store.Changed += () => raised++;

        var message = store.Set((decimal)refused);

        Assert.NotNull(message);
        Assert.Contains("above zero", message, StringComparison.Ordinal);
        Assert.Equal(4m, store.StoryPointsPerDay);
        Assert.Equal(0, raised);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("quickly")]
    // A comma is refused rather than read as a group separator. Accepting it would
    // turn 2,5 into 25 — a pace ten times the one that was typed, with nothing on
    // screen to say so.
    [InlineData("2,5")]
    [InlineData("1,000")]
    public void A_pace_that_is_not_a_number_is_refused_and_changes_nothing(string typed)
    {
        var store = Store();
        _ = store.Set(4m);

        var message = store.Set(typed);

        Assert.NotNull(message);
        Assert.Contains("as a number", message, StringComparison.Ordinal);
        Assert.Equal(4m, store.StoryPointsPerDay);
    }

    /// <summary>The <c>number</c> input reports its value with a dot whatever the
    /// machine's locale is, so the store reads one — and writes one, so a file
    /// written here opens the same on a machine whose decimal separator differs.</summary>
    [Fact]
    public void A_typed_pace_is_read_and_written_with_a_dot_whatever_the_machine_does()
    {
        Assert.Null(Store().Set("2.5"));

        Assert.Contains("2.5", File.ReadAllText(SettingsFile), StringComparison.Ordinal);
        Assert.Equal(2.5m, Store().StoryPointsPerDay);
    }

    [Fact]
    public void A_stored_pace_reads_back_as_it_was_stored_rather_than_as_it_was_spelled()
    {
        var store = Store();

        Assert.Null(store.Set("2.50"));

        Assert.Equal("2.5", PlanningVelocitySettingsStore.Format(store.StoryPointsPerDay));
    }

    /// <summary>
    /// A pace too fine to write is refused rather than accepted and then lost.
    /// <para>
    /// The file keeps four decimals, so 0.00001 would be written as <c>0</c> — which
    /// this same store refuses on the next read, putting the reader silently back on
    /// the default. Worse, until that restart the roadmap would be dividing by a
    /// figure a hundred thousand times too small. It is refused on the way in, where
    /// there is somebody to tell.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("0.00001")]
    [InlineData("0.000049")]
    public void A_pace_too_fine_to_write_down_is_refused_rather_than_rounded_away(string typed)
    {
        var store = Store();
        _ = store.Set(4m);

        var message = store.Set(typed);

        Assert.NotNull(message);
        Assert.Contains("at least", message, StringComparison.Ordinal);
        Assert.Equal(4m, store.StoryPointsPerDay);
        Assert.Equal(4m, Store().StoryPointsPerDay);
    }

    /// <summary>The finest pace that is storable is accepted, and survives — the
    /// boundary the refusal above stops just short of.</summary>
    [Fact]
    public void The_finest_storable_pace_is_accepted_and_survives_a_restart()
    {
        Assert.Null(Store().Set(PlanningVelocitySettingsStore.Smallest));

        Assert.Equal(PlanningVelocitySettingsStore.Smallest, Store().StoryPointsPerDay);
    }

    /// <summary>A value the store accepted is always one the file can hold: whatever
    /// goes in comes back unchanged, rather than being rounded on the way to disk.</summary>
    [Theory]
    [InlineData("1")]
    [InlineData("2.5")]
    [InlineData("0.3333")]
    [InlineData("13")]
    public void An_accepted_pace_round_trips_through_the_file_unchanged(string typed)
    {
        var store = Store();

        Assert.Null(store.Set(typed));

        var stored = store.StoryPointsPerDay;

        Assert.Equal(typed, PlanningVelocitySettingsStore.Format(stored));
        Assert.Equal(stored, Store().StoryPointsPerDay);
    }

    [Theory]
    [InlineData("""{ "storyPointsPerDay": "quickly" }""")]
    [InlineData("""{ "storyPointsPerDay": "0" }""")]
    [InlineData("""{ "storyPointsPerDay": "-3" }""")]
    [InlineData("""{ "storyPointsPerDay": "" }""")]
    // Hand-edited with a comma: read as the default rather than as 25, so a file
    // somebody typed their own way cannot quietly change every plan's length.
    [InlineData("""{ "storyPointsPerDay": "2,5" }""")]
    [InlineData("not json at all")]
    public void A_hand_edited_file_the_roadmap_could_not_divide_by_reads_as_the_default(string contents)
    {
        File.WriteAllText(SettingsFile, contents);

        Assert.Equal(1m, Store().StoryPointsPerDay);
    }

    /// <summary>The file is meant to be hand-edited, so a pace written the way JSON
    /// writes a number is read as readily as a quoted one. Both spellings are
    /// culture-free; only the two of them are accepted.</summary>
    [Theory]
    [InlineData("""{ "storyPointsPerDay": 2.5 }""")]
    [InlineData("""{ "storyPointsPerDay": "2.5" }""")]
    public void A_hand_edited_pace_is_read_quoted_or_bare(string contents)
    {
        File.WriteAllText(SettingsFile, contents);

        Assert.Equal(2.5m, Store().StoryPointsPerDay);
    }

    // --- Which pace places a plan ------------------------------------------------

    [Fact]
    public void An_untouched_store_places_by_the_typed_pace()
    {
        Assert.Equal(PaceSource.Manual, Store().Source);
    }

    [Fact]
    public void A_chosen_pace_survives_a_restart_and_keeps_the_typed_one()
    {
        var store = Store();
        Assert.Null(store.Set(2.5m));

        Assert.Null(store.Choose(PaceSource.LastFourWeeks));

        var reopened = Store();
        Assert.Equal(PaceSource.LastFourWeeks, reopened.Source);
        Assert.Equal(2.5m, reopened.StoryPointsPerDay);
    }

    [Fact]
    public void Typing_a_pace_keeps_the_choice()
    {
        var store = Store();
        _ = store.Choose(PaceSource.LastTwoWeeks);

        _ = store.Set(3m);

        Assert.Equal(PaceSource.LastTwoWeeks, Store().Source);
    }

    [Fact]
    public void Choosing_raises_changed()
    {
        var store = Store();
        var raised = 0;
        store.Changed += () => raised++;

        _ = store.Choose(PaceSource.LastEightWeeks);
        _ = store.Choose(PaceSource.LastEightWeeks);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void A_value_that_is_not_a_pace_source_is_refused()
    {
        var store = Store();

        Assert.NotNull(store.Choose((PaceSource)42));
        Assert.Equal(PaceSource.Manual, store.Source);
    }

    /// <summary>A file from before the choice existed, a name a later build wrote,
    /// and a number where a name belongs all place by the typed pace — and none of
    /// them costs the reader the pace they typed.</summary>
    [Theory]
    [InlineData("""{ "storyPointsPerDay": 2.5 }""")]
    [InlineData("""{ "storyPointsPerDay": 2.5, "source": "LastSixMonths" }""")]
    [InlineData("""{ "storyPointsPerDay": 2.5, "source": "2" }""")]
    [InlineData("""{ "storyPointsPerDay": 2.5, "source": null }""")]
    public void A_source_the_store_does_not_know_reads_as_the_typed_pace(string contents)
    {
        File.WriteAllText(SettingsFile, contents);

        var store = Store();

        Assert.Equal(PaceSource.Manual, store.Source);
        Assert.Equal(2.5m, store.StoryPointsPerDay);
    }

    [Fact]
    public void A_source_is_read_whatever_its_case()
    {
        File.WriteAllText(SettingsFile, """{ "storyPointsPerDay": 2, "source": "lasttwoweeks" }""");

        Assert.Equal(PaceSource.LastTwoWeeks, Store().Source);
    }

    // --- The port the roadmap asks ------------------------------------------------

    /// <summary>The module never sees the store; it sees this. Both halves of the
    /// contract are asserted through the port rather than through the store, because
    /// the port is the only thing Roadmap is allowed to hold.</summary>
    [Fact]
    public void The_port_answers_one_point_a_day_typed_when_the_reader_has_chosen_nothing()
    {
        IPlanningVelocitySettings port = new PlanningVelocitySource(Store());

        Assert.Equal(1m, port.Manual);
        Assert.Equal(PaceSource.Manual, port.Source);
    }

    [Fact]
    public void The_port_answers_a_pace_changed_after_it_was_built()
    {
        var store = Store();
        IPlanningVelocitySettings port = new PlanningVelocitySource(store);

        _ = store.Set(2m);
        _ = store.Choose(PaceSource.LastFourWeeks);

        // Read through rather than pinned at construction: the roadmap writes the
        // store, and the next placement has to divide by what it now says.
        Assert.Equal(2m, port.Manual);
        Assert.Equal(PaceSource.LastFourWeeks, port.Source);
    }

    [Fact]
    public void The_port_writes_through_to_the_store_and_relays_its_changes()
    {
        var store = Store();
        IPlanningVelocitySettings port = new PlanningVelocitySource(store);
        var raised = 0;
        port.Changed += () => raised++;

        Assert.Null(port.SetManual("3.5"));
        Assert.Null(port.Choose(PaceSource.LastTwoWeeks));
        Assert.NotNull(port.SetManual("0"));

        Assert.Equal(3.5m, store.StoryPointsPerDay);
        Assert.Equal(PaceSource.LastTwoWeeks, store.Source);
        Assert.Equal(2, raised);
    }
}
