using Backlog.Infrastructure.FileSystem.Dashboard;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// What the dashboard's drift part is fed: the Devbook context's per-chapter sync
/// verdicts folded into one row per unit, the unit's root chapter speaking for it.
/// </summary>
public class SyncVerdictDriftUnitSourceTests
{
    private const string Repository = "JSdotNet/Backlog";

    private const string Unit = ".devbook/domain/sessions/domain.md#delivery-run";

    private static readonly DashboardRepository Backlog = new("backlog", Repository);

    private static readonly DateTimeOffset Earlier = new(2026, 10, 1, 4, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Later = new(2026, 10, 5, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_unit_is_one_row_carrying_its_root_chapters_unit_verdict_and_direction()
    {
        var source = new SyncVerdictDriftUnitSource(new Store(
            Verdict(".domain/sessions/domain.md", "delivery-run", Later, unitVerdict: "code-ahead"),
            Verdict(".domain/sessions/domain.md", "delivery-run-recording", Later, unitVerdict: "code-ahead")));

        var unit = Assert.Single(source.Units(Backlog));

        Assert.Equal("backlog", unit.RepositoryAlias);
        Assert.Equal(Unit, unit.Unit);
        Assert.Equal("aggregate", unit.Kind);
        Assert.Equal("push", unit.Direction);
        Assert.Equal(".devbook/domain/sessions/context.md", unit.DirectionFrom);
        Assert.Equal("code-ahead", unit.Verdict);
        Assert.Equal("pr", unit.Action);
        Assert.Equal(Later, unit.RecordedAt);
    }

    [Fact]
    public void Without_its_root_a_unit_takes_its_latest_chapter()
    {
        var source = new SyncVerdictDriftUnitSource(new Store(
            Verdict(".domain/sessions/domain.md", "run-started", Earlier, unitVerdict: "aligned"),
            Verdict(".domain/sessions/domain.md", "delivery-run-recording", Later, unitVerdict: "spec-ahead")));

        var unit = Assert.Single(source.Units(Backlog));

        Assert.Equal("spec-ahead", unit.Verdict);
        Assert.Equal(Later, unit.RecordedAt);
    }

    [Fact]
    public void A_host_with_no_verdict_store_says_it_keeps_none()
    {
        var source = new SyncVerdictDriftUnitSource(verdicts: null);

        Assert.False(source.IsAvailable);
        Assert.Empty(source.Units(Backlog));
    }

    private static DevbookSyncVerdict Verdict(string path, string anchor, DateTimeOffset recordedAt, string unitVerdict) =>
        new(
            Repository,
            path,
            anchor,
            "aligned",
            Evidence: null,
            Unit: Unit,
            UnitKind: "aggregate",
            UnitVerdict: unitVerdict,
            Action: "pr",
            Link: "https://github.com/JSdotNet/Backlog/pull/950",
            RecordedAt: recordedAt,
            RunId: "run-1",
            Sync: "push",
            SyncFrom: ".devbook/domain/sessions/context.md");

    private sealed class Store(params DevbookSyncVerdict[] verdicts) : IDevbookSyncVerdicts
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<DevbookSyncVerdict> For(string repository, string chapterPath, IReadOnlyList<DevbookFolderSetting>? folders = null) => [];

        public IReadOnlyList<DevbookSyncVerdict> ForRepository(string repository) =>
            [.. verdicts.Where(verdict => string.Equals(verdict.Repository, repository, StringComparison.OrdinalIgnoreCase))];

        public Task RecordAsync(IReadOnlyList<DevbookSyncVerdict> recorded, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
