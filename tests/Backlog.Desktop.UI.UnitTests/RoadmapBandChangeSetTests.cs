using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.UI;
using Backlog.SharedKernel.Results;
using Backlog.UI.Components.Roadmap;

using Bunit;

using Microsoft.Data.Sqlite;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The band's writes name the person (local ADR 0061 §3), read back from the journal the
/// real store's triggers write into this test's own <c>backlog.db</c>: a drag is one
/// <c>person</c> change set and leaves no unknown-writer one, and the keep-up the band
/// runs when it opens is the system's own pass.
/// </summary>
public sealed class RoadmapBandChangeSetTests : RoadmapBandHarness
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_drag_is_one_person_change_set_and_no_unknown_writer_one()
    {
        Configure("JSdotNet/Backlog");
        Assert.IsType<Ok<RoadmapItemDto>>((await Planning.AddItemAsync(
            "Work", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9), repositoryAliases: ["backlog"], cancellationToken: Ct)).Value);

        using var context = Context();
        var band = Drawn(context);
        var timeline = band.FindComponent<RoadmapTimeline>();
        var bar = timeline.Instance.Bars.Single();
        var before = (await ReadAsync()).Select(row => row.Sequence).DefaultIfEmpty(0).Max();

        await band.InvokeAsync(() => timeline.Instance.OnBarChanged.InvokeAsync(
            new RoadmapChange(bar.Id, bar.RowId, new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 6), RoadmapDrag.Move)));

        var rows = (await ReadAsync()).Where(row => row.Sequence > before).ToList();
        var row = Assert.Single(rows);
        Assert.Equal("roadmap-plan", row.DocumentKind);
        Assert.Equal("person", row.Actor);
        Assert.Equal("moving a roadmap item", row.Label);
    }

    [Fact]
    public async Task Opening_the_band_files_its_keep_up_under_the_system_never_an_unknown_writer()
    {
        Configure("JSdotNet/Backlog");
        Assert.IsType<Ok<RoadmapItemDto>>((await Planning.AddItemAsync(
            "Work", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9), repositoryAliases: ["backlog"], cancellationToken: Ct)).Value);
        var before = (await ReadAsync()).Select(row => row.Sequence).DefaultIfEmpty(0).Max();

        using var context = Context();
        Drawn(context);

        var rows = (await ReadAsync()).Where(row => row.Sequence > before).ToList();
        Assert.DoesNotContain(rows, row => row.Actor == "unknown");
        Assert.All(rows, row => Assert.Equal("system", row.Actor));
    }

    private sealed record JournalRow(long Sequence, string Actor, string? Label, string DocumentKind);

    /// <summary>The journal's revisions with the change set each belongs to, straight
    /// from the database the band's store writes.</summary>
    private async Task<IReadOnlyList<JournalRow>> ReadAsync()
    {
        var path = SqliteTaskRepository.DatabasePathFor(Settings.RootDirectory);
        if (!File.Exists(path)) return [];

        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync(Ct);

        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'revisions';";
        if (Convert.ToInt64(await exists.ExecuteScalarAsync(Ct)) == 0) return [];

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.sequence, c.actor_kind, c.label, r.document_kind
            FROM revisions r JOIN change_sets c ON c.id = r.change_set_id
            ORDER BY r.sequence;
            """;

        var rows = new List<JournalRow>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(new JournalRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3)));
        }

        return rows;
    }
}
