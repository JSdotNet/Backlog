using Backlog.Infrastructure.Sqlite.Progress;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.DomainModels;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The stored sprint series: one row per series in <c>backlog.db</c>, the whole series
/// as one document. What goes in comes back out unchanged, and a row that will not parse
/// is skipped rather than failing the read.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteSprintSeriesRepositoryTests : IDisposable
{
    private static readonly DateOnly Monday = new(2026, 10, 12);

    private readonly string _dir;
    private readonly SqliteSprintSeriesRepository _repository;

    public SqliteSprintSeriesRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "progress-tests-" + Guid.NewGuid().ToString("N"));
        _repository = new SqliteSprintSeriesRepository(_dir);
    }

    private static SprintSeries Series(string repository, string? name = null) =>
        SprintSeries.Create(
            Guid.NewGuid(),
            [repository, "docs-" + repository],
            name,
            SprintLength.OneWeek,
            DayOfWeek.Monday,
            "Core {n}",
            new BriefVisibility(Daily: true, Weekly: false),
            [GoalRule.Create(GoalRuleKind.InPlans, ["+progress-sprints"]).Value, GoalRule.Create(GoalRuleKind.MarkedAsGoal, null).Value],
            80).Value;

    [Fact]
    public async Task A_first_run_has_no_series()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.Empty(await _repository.ListAsync(ct));
        Assert.Null(await _repository.GetAsync(Guid.NewGuid(), ct));
    }

    [Fact]
    public async Task A_series_with_its_sprints_and_items_round_trips_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = Series("backlog", "Core");
        var carried = Guid.NewGuid();
        var dropped = Guid.NewGuid();
        series.StartSprint(Monday, "Ship the model");
        series.AddTask(1, carried, Monday);
        series.LinkPlan(1, "+progress-sprints", Monday.AddDays(1));
        series.SetNote(1, dropped, "Not this time");
        series.CloseSprint(
            [carried, dropped],
            [carried, dropped],
            new Dictionary<Guid, CloseOutcome> { [carried] = CloseOutcome.Carry, [dropped] = CloseOutcome.Drop },
            "Ship the view");

        await _repository.SaveAsync(series, ct);
        var loaded = await _repository.GetAsync(series.Id, ct);

        Assert.NotNull(loaded);
        Assert.Equal("Core", loaded.Name);
        Assert.Equal(["backlog", "docs-backlog"], loaded.Repositories);
        Assert.Equal(SprintLength.OneWeek, loaded.Length);
        Assert.Equal("Core {n}", loaded.NamePattern.Value);
        Assert.Equal(new BriefVisibility(true, false), loaded.Briefs);
        Assert.Equal(80, loaded.Threshold.Share);
        Assert.Equal(series.GoalRules, loaded.GoalRules);

        var first = loaded.SprintNumber(1)!;
        Assert.False(first.IsOpen);
        Assert.Equal(Monday, first.Window.Start);
        Assert.Equal(Monday.AddDays(6), first.Window.End);
        Assert.Equal("Ship the model", first.Goal);
        Assert.Equal(new PlanLink[] { PlanLink.Restore("progress-sprints", Monday.AddDays(1)) }, first.PlanLinks);
        Assert.Equal(Monday.AddDays(1), first.PlanLinks[0].LinkedOn);
        Assert.Equal(CloseOutcome.Carry, first.ItemFor(carried)!.Outcome);
        Assert.Equal("Not this time", first.ItemFor(dropped)!.Note);
        Assert.Equal(CloseOutcome.Drop, first.ItemFor(dropped)!.Outcome);
        Assert.True(first.ItemFor(dropped)!.KeptAtClose);

        var second = loaded.OpenSprint!;
        Assert.Equal(2, second.Number);
        Assert.Equal("Ship the view", second.Goal);
        Assert.True(second.ItemFor(carried)!.AddedDirectly);
        Assert.Equal(Monday.AddDays(7), second.ItemFor(carried)!.AddedOn);
    }

    [Fact]
    public async Task Every_series_is_its_own_row_listed_in_the_order_it_was_created()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Series("backlog");
        var second = Series("tools");

        await _repository.SaveAsync(first, ct);
        await _repository.SaveAsync(second, ct);
        first.ShowInBriefs(daily: false, weekly: true);
        await _repository.SaveAsync(first, ct);

        var all = await _repository.ListAsync(ct);

        Assert.Equal([first.Id, second.Id], all.Select(series => series.Id));
        Assert.True(all[0].Briefs.Weekly);
        Assert.Equal(2, await CountRowsAsync(ct));
    }

    [Fact]
    public async Task A_row_that_will_not_parse_is_skipped_and_left_as_it_is()
    {
        var ct = TestContext.Current.CancellationToken;
        var good = Series("backlog");
        await _repository.SaveAsync(good, ct);
        await ExecuteAsync(
            "INSERT INTO progress_series (id, document, created_at, updated_at) VALUES ('broken', '{not json', '2026-10-01', '2026-10-01');",
            ct);

        var all = await _repository.ListAsync(ct);

        Assert.Equal([good.Id], all.Select(series => series.Id));
        Assert.Equal(2, await CountRowsAsync(ct));
    }

    [Fact]
    public async Task A_switched_off_series_comes_back_off()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = Series("backlog");
        series.Switch(on: false);

        await _repository.SaveAsync(series, ct);
        var loaded = await _repository.GetAsync(series.Id, ct);

        Assert.False(loaded!.IsOn);
    }

    [Fact]
    public async Task A_document_written_before_the_switch_existed_reads_as_on()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = Series("backlog");
        await _repository.SaveAsync(series, ct);
        var id = series.Id.ToString();
        var stored = await ScalarAsync("SELECT document FROM progress_series;", ct);
        var withoutSwitch = stored.Replace(",\"isOn\":true", string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("isOn", withoutSwitch, StringComparison.Ordinal);
        await ExecuteAsync($"UPDATE progress_series SET document = '{withoutSwitch.Replace("'", "''", StringComparison.Ordinal)}' WHERE id = '{id}';", ct);

        var loaded = await _repository.GetAsync(series.Id, ct);

        Assert.True(loaded!.IsOn);
    }

    [Fact]
    public async Task The_document_writes_dates_as_days_and_enums_by_name()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = Series("backlog");
        series.StartSprint(Monday, "Readable");
        await _repository.SaveAsync(series, ct);

        var document = await ScalarAsync("SELECT document FROM progress_series;", ct);

        Assert.Contains("\"start\":\"2026-10-12\"", document, StringComparison.Ordinal);
        Assert.Contains("\"length\":\"OneWeek\"", document, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"InPlans\"", document, StringComparison.Ordinal);
        Assert.Contains("\"version\":1", document, StringComparison.Ordinal);
    }

    private async Task<long> CountRowsAsync(CancellationToken ct) =>
        long.Parse(await ScalarAsync("SELECT COUNT(*) FROM progress_series;", ct), System.Globalization.CultureInfo.InvariantCulture);

    private async Task<string> ScalarAsync(string sql, CancellationToken ct)
    {
        await using var connection = new SqliteConnection($"Data Source={_repository.DatabasePath}");
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private async Task ExecuteAsync(string sql, CancellationToken ct)
    {
        await using var connection = new SqliteConnection($"Data Source={_repository.DatabasePath}");
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_dir)) return;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
