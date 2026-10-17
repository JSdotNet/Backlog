using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// What the stand-up asks of the desktop: the person's iteration length and two-week start,
/// kept in the shell navigation file on the <c>ToolsViewPreferences</c> pattern
/// (<c>.devbook/domain/progress/context.md#iteration-length</c>, <c>#two-week-start</c>), and
/// the global pace Roadmap Planning places by.
/// </summary>
public sealed class StandUpSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-iteration-settings-tests", Guid.NewGuid().ToString("n"));

    private string ShellPath => Path.Combine(_root, "shell-navigation.json");

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
        var store = new ShellNavigationStore(ShellPath);
        var settings = new IterationSettings(store);
        // Moved off the default first, so choosing Week is a change that writes too.
        settings.SetLength(IterationLength.Month, Wednesday21);
        settings.SetLength(length, Wednesday21);
        store.Flush();

        Assert.Contains($"\"goalsIterationLength\": \"{stored}\"", File.ReadAllText(ShellPath), StringComparison.Ordinal);
        Assert.Equal(length, new IterationSettings(new ShellNavigationStore(ShellPath)).Length);
    }

    /// <summary>A file written with a member name still reads, compared without regard to case.</summary>
    [Theory]
    [InlineData("TwoWeeks", IterationLength.TwoWeeks)]
    [InlineData("MONTH", IterationLength.Month)]
    [InlineData("Two-Weeks", IterationLength.TwoWeeks)]
    public void A_member_name_or_another_case_still_reads(string stored, IterationLength expected)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(ShellPath, $$"""{ "goalsIterationLength": "{{stored}}" }""");

        Assert.Equal(expected, new IterationSettings(new ShellNavigationStore(ShellPath)).Length);
    }

    /// <summary>A first choice of 2 weeks writes its length and its start in one save.</summary>
    [Fact]
    public void Choosing_two_weeks_first_saves_once()
    {
        var store = new ShellNavigationStore(ShellPath);
        var saves = 0;
        store.Changed += () => saves++;

        new IterationSettings(store).SetLength(IterationLength.TwoWeeks, Wednesday21);

        Assert.Equal(1, saves);
        Assert.Equal(("two-weeks", (DateOnly?)new DateOnly(2026, 10, 19)), (store.GoalsIterationLength, store.GoalsTwoWeekStart));
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
        var store = new ShellNavigationStore(ShellPath);
        var settings = new IterationSettings(store);
        Assert.Equal((IterationLength.Week, (DateOnly?)null), (settings.Length, settings.TwoWeekStart));

        settings.SetLength(IterationLength.TwoWeeks, Wednesday21);
        store.Flush();

        var reopened = new IterationSettings(new ShellNavigationStore(ShellPath));
        Assert.Equal((IterationLength.TwoWeeks, (DateOnly?)new DateOnly(2026, 10, 19)), (reopened.Length, reopened.TwoWeekStart));
    }

    /// <summary>Scenario "Choosing 2 weeks for the first time": the start defaults to the Monday of this week.</summary>
    [Fact]
    public void Choosing_two_weeks_first_sets_the_start_to_this_weeks_Monday()
    {
        var settings = new IterationSettings(new ShellNavigationStore(ShellPath));
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
        Directory.CreateDirectory(_root);
        File.WriteAllText(ShellPath, $$"""{ "goalsIterationLength": "{{stored}}" }""");

        Assert.Equal(IterationLength.Week, new IterationSettings(new ShellNavigationStore(ShellPath)).Length);
    }

    [Fact]
    public void Without_a_store_the_setting_is_kept_in_memory()
    {
        var settings = new IterationSettings(null);

        settings.SetLength(IterationLength.TwoWeeks, Wednesday21);

        Assert.Equal((IterationLength.TwoWeeks, (DateOnly?)new DateOnly(2026, 10, 19)), (settings.Length, settings.TwoWeekStart));
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
