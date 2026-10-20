using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// What the stand-up asks of the desktop: the person's iteration length and two-week start,
/// kept in the per-user iteration settings file that travels between desktops (local ADR
/// 0036; <c>.devbook/domain/progress/context.md#iteration-length</c>, <c>#two-week-start</c>)
/// and seeded once from the shell navigation file an earlier build kept them in, and the
/// global pace Roadmap Planning places by.
/// </summary>
public sealed class StandUpSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-iteration-settings-tests", Guid.NewGuid().ToString("n"));

    private string ShellPath => Path.Combine(_root, "shell-navigation.json");

    private string SettingsPath => Path.Combine(_root, IterationSettingsStore.FileName);

    private IterationSettingsStore Store(ShellNavigationStore? legacy = null) => new(SettingsPath, legacy);

    private void WriteSettings(string content)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(SettingsPath, content);
    }

    private static readonly DateOnly Wednesday21 = new(2026, 10, 21);

    [Fact]
    public void The_store_keeps_the_length_and_start_across_a_restart()
    {
        var store = new ShellNavigationStore(ShellPath);
        Assert.Null(store.GoalsIterationLength);
        Assert.Null(store.GoalsTwoWeekStart);

        store.SetGoalsIteration("two-weeks", new DateOnly(2026, 10, 5));
        store.Flush();

        var reopened = new ShellNavigationStore(ShellPath);
        Assert.Equal("two-weeks", reopened.GoalsIterationLength);
        Assert.Equal(new DateOnly(2026, 10, 5), reopened.GoalsTwoWeekStart);
        var file = File.ReadAllText(ShellPath);
        Assert.Contains("\"goalsIterationLength\": \"two-weeks\"", file, StringComparison.Ordinal);
        Assert.Contains("\"goalsTwoWeekStart\": \"2026-10-05\"", file, StringComparison.Ordinal);
    }

    /// <summary>The setting's stored values are the spec's (<c>context.md#iteration-length</c>),
    /// never the member names: choosing 2 weeks writes <c>two-weeks</c> and reads back as 2 weeks.</summary>
    [Theory]
    [InlineData(IterationLength.Day, "day")]
    [InlineData(IterationLength.Week, "week")]
    [InlineData(IterationLength.TwoWeeks, "two-weeks")]
    [InlineData(IterationLength.Month, "month")]
    public void The_setting_stores_the_specs_values_and_reads_them_back(IterationLength length, string stored)
    {
        var settings = new IterationSettings(Store());
        // Moved off the default first, so choosing Week is a change that writes too.
        settings.SetLength(IterationLength.Month, Wednesday21);
        settings.SetLength(length, Wednesday21);

        Assert.Contains($"\"length\": \"{stored}\"", File.ReadAllText(SettingsPath), StringComparison.Ordinal);
        Assert.Equal(length, new IterationSettings(Store()).Length);
    }

    /// <summary>A file written with a member name still reads, compared without regard to case.</summary>
    [Theory]
    [InlineData("TwoWeeks", IterationLength.TwoWeeks)]
    [InlineData("MONTH", IterationLength.Month)]
    [InlineData("Two-Weeks", IterationLength.TwoWeeks)]
    public void A_member_name_or_another_case_still_reads(string stored, IterationLength expected)
    {
        WriteSettings($$"""{ "length": "{{stored}}" }""");

        Assert.Equal(expected, new IterationSettings(Store()).Length);
    }

    /// <summary>A first choice of 2 weeks writes its length and its start in one save, and
    /// tells the sync once.</summary>
    [Fact]
    public void Choosing_two_weeks_first_saves_once()
    {
        var store = Store();
        var saves = 0;
        ((IIterationSettingsReplication)store).Changed += () => saves++;

        new IterationSettings(store).SetLength(IterationLength.TwoWeeks, Wednesday21);

        Assert.Equal(1, saves);
        Assert.Equal(new IterationChoice("two-weeks", new DateOnly(2026, 10, 19)), store.Choice);
    }

    [Fact]
    public void Nothing_chosen_writes_no_key()
    {
        var store = new ShellNavigationStore(ShellPath);
        store.SetToolsTab("tools");
        store.Flush();

        var file = File.ReadAllText(ShellPath);
        Assert.DoesNotContain("goalsIterationLength", file, StringComparison.Ordinal);
        Assert.DoesNotContain("goalsTwoWeekStart", file, StringComparison.Ordinal);
    }

    [Fact]
    public void A_start_that_is_no_date_reads_as_none()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(ShellPath, """{ "goalsIterationLength": "week", "goalsTwoWeekStart": "the fifth" }""");

        var store = new ShellNavigationStore(ShellPath);

        Assert.Equal("week", store.GoalsIterationLength);
        Assert.Null(store.GoalsTwoWeekStart);
    }

    /// <summary>Scenario "Reopening the app": the person chose 2 weeks, and it reads by 2 weeks again.</summary>
    [Fact]
    public void The_setting_reads_Week_until_chosen_and_keeps_the_choice()
    {
        var settings = new IterationSettings(Store());
        Assert.Equal((IterationLength.Week, (DateOnly?)null), (settings.Length, settings.TwoWeekStart));

        settings.SetLength(IterationLength.TwoWeeks, Wednesday21);

        var reopened = new IterationSettings(Store());
        Assert.Equal((IterationLength.TwoWeeks, (DateOnly?)new DateOnly(2026, 10, 19)), (reopened.Length, reopened.TwoWeekStart));
    }

    /// <summary>Scenario "Choosing 2 weeks for the first time": the start defaults to the Monday of this week.</summary>
    [Fact]
    public void Choosing_two_weeks_first_sets_the_start_to_this_weeks_Monday()
    {
        var settings = new IterationSettings(Store());
        var changes = 0;
        settings.Changed += () => changes++;

        settings.SetLength(IterationLength.TwoWeeks, Wednesday21);

        Assert.Equal((IterationLength.TwoWeeks, (DateOnly?)new DateOnly(2026, 10, 19)), (settings.Length, settings.TwoWeekStart));
        Assert.Equal(1, changes);

        settings.SetTwoWeekStart(new DateOnly(2026, 10, 5));
        settings.SetLength(IterationLength.Week, Wednesday21);
        settings.SetLength(IterationLength.TwoWeeks, new DateOnly(2026, 12, 2));

        Assert.Equal(new DateOnly(2026, 10, 5), settings.TwoWeekStart);
        Assert.Equal(4, changes);
    }

    [Theory]
    [InlineData("Fortnight")]
    [InlineData("7")]
    [InlineData("2")]
    [InlineData("")]
    public void An_unknown_length_reads_as_Week(string stored)
    {
        WriteSettings($$"""{ "length": "{{stored}}" }""");

        Assert.Equal(IterationLength.Week, new IterationSettings(Store()).Length);
    }

    [Fact]
    public void Without_a_store_the_setting_is_kept_in_memory()
    {
        var settings = new IterationSettings(null);

        settings.SetLength(IterationLength.TwoWeeks, Wednesday21);

        Assert.Equal((IterationLength.TwoWeeks, (DateOnly?)new DateOnly(2026, 10, 19)), (settings.Length, settings.TwoWeekStart));
    }

    /// <summary>ADR 0036 §3: a copy another desktop sent, taken by the store, is what the
    /// setting reads, and the Goals view and the stand-up hear it.</summary>
    [Fact]
    public async Task A_copy_the_store_took_is_read_and_announced()
    {
        var store = Store();
        var settings = new IterationSettings(store);
        var changes = 0;
        settings.Changed += () => changes++;

        var outcome = await ((IIterationSettingsReplication)store).ApplyAsync(
            new IterationSettingsCopyDto("""{"updatedAt":"2026-10-20T08:00:00Z","length":"two-weeks","twoWeekStart":"2026-10-05"}""",
                new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero)),
            TestContext.Current.CancellationToken);

        Assert.Equal(IterationSettingsReplicaOutcome.Taken, outcome);
        Assert.Equal((IterationLength.TwoWeeks, (DateOnly?)new DateOnly(2026, 10, 5)), (settings.Length, settings.TwoWeekStart));
        Assert.Equal(1, changes);
    }

    /// <summary>ADR 0036 §4: a length a newer build wrote reads as Week here but rides along —
    /// moving only the two-week start keeps it verbatim rather than replacing it with
    /// <c>week</c> and pushing that to every desktop.</summary>
    [Fact]
    public void Moving_only_the_start_keeps_a_length_this_build_does_not_know()
    {
        WriteSettings("""{ "updatedAt": "2026-10-20T08:00:00Z", "length": "quarter" }""");
        var store = Store();
        var settings = new IterationSettings(store);
        Assert.Equal(IterationLength.Week, settings.Length);

        settings.SetTwoWeekStart(new DateOnly(2026, 10, 5));

        Assert.Equal(new IterationChoice("quarter", new DateOnly(2026, 10, 5)), store.Choice);
    }

    /// <summary>ADR 0036 §5: on the first start of this build, a length the person chose on
    /// the old one is what the setting reads.</summary>
    [Fact]
    public void A_length_chosen_on_the_old_build_still_reads_after_the_upgrade()
    {
        var shell = new ShellNavigationStore(ShellPath);
        shell.SetGoalsIteration("two-weeks", new DateOnly(2026, 10, 5));
        shell.Flush();

        var settings = new IterationSettings(Store(new ShellNavigationStore(ShellPath)));

        Assert.Equal((IterationLength.TwoWeeks, (DateOnly?)new DateOnly(2026, 10, 5)), (settings.Length, settings.TwoWeekStart));
    }

    [Fact]
    public async Task The_pace_is_the_global_pace_of_the_roadmap()
    {
        var velocity = new FixedVelocity(18m);
        var pace = new PlanningVelocityStandUpPace(() => velocity);

        Assert.Equal(18m, await pace.PointsPerWeekAsync(TestContext.Current.CancellationToken));
        Assert.Equal([null], velocity.Asked);
    }

    [Fact]
    public async Task Without_the_roadmap_there_is_no_pace()
    {
        var pace = new PlanningVelocityStandUpPace(() => null);

        Assert.Null(await pace.PointsPerWeekAsync(TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        ShellNavigationStore.WaitForPendingWrites(ShellPath);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FixedVelocity(decimal pace) : IPlanningVelocity
    {
        public List<string?> Asked { get; } = [];

        public Task<PacesInUseDto> ReadPacesInUseAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<decimal> GetStoryPointsPerWeekAsync(string? repository = null, CancellationToken cancellationToken = default)
        {
            Asked.Add(repository);
            return Task.FromResult(pace);
        }
    }
}
