using Backlog.Modules.Progress.DomainModels;
using Backlog.Modules.Progress.Services;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// The one-repository-one-series rule for series that arrive by sync
/// (<c>.devbook/domain/progress/domain.invariants.md#invariant-a-synced-repository-keeps-to-the-series-set-up-first</c>):
/// two desktops that each set up a series for one repository offline both keep theirs,
/// and <see cref="SeriesAssignment"/> decides from the series alone which keeps it.
/// </summary>
public sealed class SeriesClaimTests
{
    private static readonly DateOnly Monday = Make.Monday;
    private static readonly DateTimeOffset Morning = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_series_set_up_first_keeps_the_repository()
    {
        var core = Series("Core", Morning, "backlog");
        var main = Series("Main", Morning.AddHours(1), "BACKLOG", "docs");
        main.StartSprint(Monday, "Recorded on B");

        // The order the desktop lists them in decides nothing.
        var switchedOff = SeriesAssignment.ResolveClaims([main, core]);

        Assert.Same(main, Assert.Single(switchedOff));
        Assert.True(core.IsOn);
        Assert.Null(core.OffReason);
        Assert.False(main.IsOn);
        Assert.Equal("Switched off: BACKLOG already works to \"Core\", which was set up first.", main.OffReason);
        Assert.Equal(["BACKLOG", "docs"], main.Repositories);
        Assert.Equal("Recorded on B", Assert.Single(main.Sprints).Goal);
        Assert.Same(core, SeriesAssignment.HolderOf([main, core], "backlog"));
        Assert.Empty(SeriesAssignment.ResolveClaims([main, core]));
    }

    [Fact]
    public void Two_series_set_up_at_the_same_moment_are_ordered_by_id()
    {
        var first = Series("First", Morning, "backlog", id: new Guid("00000000-0000-0000-0000-000000000001"));
        var second = Series("Second", Morning, "backlog", id: new Guid("00000000-0000-0000-0000-000000000002"));

        SeriesAssignment.ResolveClaims([second, first]);

        Assert.True(first.IsOn);
        Assert.False(second.IsOn);
    }

    [Fact]
    public void A_series_stored_before_the_moment_was_kept_counts_as_set_up_first()
    {
        var legacy = Series("Legacy", null, "backlog");
        var fresh = Series("Fresh", Morning, "backlog");

        SeriesAssignment.ResolveClaims([fresh, legacy]);

        Assert.True(legacy.IsOn);
        Assert.False(fresh.IsOn);
    }

    [Fact]
    public void A_switched_on_series_keeps_the_repository_from_a_switched_off_one()
    {
        var core = Series("Core", Morning, "backlog");
        core.Switch(on: false);
        var main = Series("Main", Morning.AddHours(1), "backlog");

        Assert.Empty(SeriesAssignment.ResolveClaims([core, main]));
        Assert.True(main.IsOn);
        Assert.Same(main, SeriesAssignment.HolderOf([core, main], "backlog"));
    }

    [Fact]
    public void Switching_on_a_series_whose_repository_another_has_is_refused()
    {
        var core = Series("Core", Morning, "backlog");
        var main = Series("Main", Morning.AddHours(1), "backlog");
        SeriesAssignment.ResolveClaims([core, main]);

        var on = SeriesAssignment.SwitchOn([core, main], main, on: true);

        Assert.Equal("progress.repository_in_other_series", on.Error.Code);
        Assert.False(main.IsOn);
        Assert.NotNull(main.OffReason);
    }

    [Fact]
    public void Switching_a_series_on_clears_its_off_reason()
    {
        var core = Series("Core", Morning, "backlog");
        var main = Series("Main", Morning.AddHours(1), "backlog", "docs");
        SeriesAssignment.ResolveClaims([core, main]);
        Assert.True(main.RemoveRepository("backlog").IsSuccess);

        var on = SeriesAssignment.SwitchOn([core, main], main, on: true);

        Assert.True(on.IsSuccess);
        Assert.True(main.IsOn);
        Assert.Null(main.OffReason);
    }

    private static SprintSeries Series(string name, DateTimeOffset? createdAt, params string[] repositories) =>
        Series(name, createdAt, repositories, null);

    private static SprintSeries Series(string name, DateTimeOffset? createdAt, string repository, Guid id) =>
        Series(name, createdAt, [repository], id);

    private static SprintSeries Series(string name, DateTimeOffset? createdAt, string[] repositories, Guid? id) =>
        SprintSeries.Create(id ?? Guid.NewGuid(), repositories, name, createdAt: createdAt).Value;
}
