using Backlog.Infrastructure.Sqlite.Progress;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The stored impediments: one row per impediment in <c>progress_impediments</c>, the
/// whole impediment as one document, which is also the <c>impediment</c> document that
/// travels. What goes in comes back out unchanged, a copy from another device is kept
/// verbatim at its stamp, and a row that will not parse is skipped rather than failing
/// the read.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteImpedimentRepositoryTests : IDisposable
{
    private static readonly DateOnly Wed14 = new(2026, 10, 14);

    private readonly string _dir;
    private readonly string _other;
    private readonly SqliteImpedimentRepository _repository;

    public SqliteImpedimentRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "impediment-tests-" + Guid.NewGuid().ToString("N"));
        _other = Path.Combine(Path.GetTempPath(), "impediment-tests-" + Guid.NewGuid().ToString("N"));
        _repository = new SqliteImpedimentRepository(_dir);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static Impediment Escalated(string title = "Bank sample files missing", DateTimeOffset? createdAt = null)
    {
        var impediment = Impediment.Raise(Guid.NewGuid(), title, Wed14, createdAt).Value;
        Assert.True(impediment.MoveTo(ImpedimentState.Reported, Wed14.AddDays(1), "bank support").IsSuccess);
        Assert.True(impediment.MoveTo(ImpedimentState.Escalated, Wed14.AddDays(2), "account manager").IsSuccess);
        impediment.SetCheckAgain(new DateOnly(2026, 10, 20));
        impediment.EditNote("Asked for the sample twice.\nNo answer yet.");
        impediment.HoldBack(HeldBackItem.ForTask(Guid.NewGuid()).Value);
        impediment.HoldBack(HeldBackItem.ForPullRequest("acme/payments", 412).Value);
        impediment.HoldBack(HeldBackItem.ForTask(Guid.NewGuid()).Value);
        impediment.HoldBack(HeldBackItem.ForPlan("goals-standup").Value);
        return impediment;
    }

    /// <summary>An impediment as a version 1 build writes it: the tasks in <c>holdsBack</c>
    /// and nothing else held back.</summary>
    private static string Version1(Guid id, string title, string history, params Guid[] tasks) =>
        $$"""{"version":1,"id":"{{id}}","title":"{{title}}","history":[{{history}}],"holdsBack":[{{string.Join(",", tasks.Select(task => $"\"{task}\""))}}]}""";

    private static string Stored(Impediment impediment) =>
        System.Text.Json.JsonSerializer.Serialize(ImpedimentDocument.From(impediment), ImpedimentDocument.JsonOptions);

    private static void AssertSame(Impediment expected, Impediment? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.ReportedTo, actual.ReportedTo);
        Assert.Equal(expected.HandledBy, actual.HandledBy);
        Assert.Equal(expected.CheckAgain, actual.CheckAgain);
        Assert.Equal(expected.Note, actual.Note);
        Assert.Equal(expected.History, actual.History);
        Assert.Equal(expected.HoldsBack, actual.HoldsBack);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
    }

    /// <summary>The phone reads the document this store writes with
    /// <c>ProgressDocuments.ReadImpediment</c> (local ADR 0035 §8), and what it reads is the
    /// impediment the desktop holds, history and all.</summary>
    [Fact]
    public async Task The_stored_document_reads_on_the_phone_as_the_impediment_the_desktop_holds()
    {
        var impediment = Escalated(createdAt: new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero));
        await _repository.SaveAsync(impediment, Cancellation);

        var stored = Assert.Single(await _repository.ReadAllAsync(Cancellation));
        var read = Modules.Progress.Abstractions.ProgressDocuments.ReadImpediment(stored.Content, impediment.Id);

        Assert.NotNull(read);
        Assert.Equivalent(Modules.Progress.Services.ImpedimentMapper.ToDto(impediment), read.Impediment, strict: true);
        Assert.Equal(impediment.CreatedAt, read.CreatedAt);
    }

    [Fact]
    public async Task A_first_run_has_no_impediment()
    {
        Assert.Empty(await _repository.ListAsync(Cancellation));
        Assert.Null(await _repository.GetAsync(Guid.NewGuid(), Cancellation));
        Assert.Empty(await _repository.ReadAllAsync(Cancellation));
    }

    [Fact]
    public async Task An_impediment_with_its_history_and_tasks_round_trips_unchanged()
    {
        var impediment = Escalated(createdAt: new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero));

        await _repository.SaveAsync(impediment, Cancellation);

        AssertSame(impediment, await _repository.GetAsync(impediment.Id, Cancellation));
        AssertSame(impediment, Assert.Single(await _repository.ListAsync(Cancellation)));
    }

    [Fact]
    public async Task A_resolved_impediment_reads_back_resolved_on_its_day()
    {
        var impediment = Impediment.Raise(Guid.NewGuid(), "Credit note numbering unclear", Wed14).Value;
        Assert.True(impediment.MoveTo(ImpedimentState.Resolved, Wed14.AddDays(1)).IsSuccess);

        await _repository.SaveAsync(impediment, Cancellation);

        var read = await _repository.GetAsync(impediment.Id, Cancellation);
        Assert.Equal(ImpedimentState.Resolved, read!.State);
        Assert.Equal(Wed14.AddDays(1), read.ResolvedOn);
    }

    [Fact]
    public async Task Saving_again_replaces_the_row_and_moves_its_stamp_forward()
    {
        var impediment = Escalated();
        await _repository.SaveAsync(impediment, Cancellation);
        var first = (await _repository.ReadAsync(impediment.Id, Cancellation))!.UpdatedAt;

        Assert.True(impediment.MoveTo(ImpedimentState.BeingHandled, Wed14.AddDays(3), "bank").IsSuccess);
        await _repository.SaveAsync(impediment, Cancellation);

        Assert.Equal(1, await CountRowsAsync());
        Assert.Equal(ImpedimentState.BeingHandled, (await _repository.GetAsync(impediment.Id, Cancellation))!.State);
        Assert.True((await _repository.ReadAsync(impediment.Id, Cancellation))!.UpdatedAt > first);
    }

    [Fact]
    public async Task The_stored_document_names_the_state_by_name_and_days_as_dates()
    {
        await _repository.SaveAsync(Escalated(), Cancellation);

        var document = await ScalarAsync("SELECT document FROM progress_impediments;");

        Assert.Contains("\"state\":\"Escalated\"", document, StringComparison.Ordinal);
        Assert.Contains("\"on\":\"2026-10-16\"", document, StringComparison.Ordinal);
        Assert.Contains("\"checkAgain\":\"2026-10-20\"", document, StringComparison.Ordinal);
    }

    /// <summary>Version 2 (local ADR 0035 §9): every item in link order under
    /// <c>heldBack</c>, and the task ids still under <c>holdsBack</c>, so a version 1 build —
    /// which ignores a member it does not know — still reads the tasks.</summary>
    [Fact]
    public async Task The_stored_document_is_version_2_and_a_version_1_reader_still_gets_the_tasks()
    {
        var impediment = Escalated();
        await _repository.SaveAsync(impediment, Cancellation);

        var document = await ScalarAsync("SELECT document FROM progress_impediments;");
        var asVersion1 = System.Text.Json.JsonSerializer.Deserialize<Version1Shape>(document, ImpedimentDocument.JsonOptions)!;

        Assert.Equal(2, asVersion1.Version);
        Assert.Equal(impediment.HoldsBack.Where(item => item.Kind == HeldBackKind.Task).Select(item => item.TaskId!.Value), asVersion1.HoldsBack);
        Assert.Contains("\"heldBack\":[{\"kind\":\"Task\"", document, StringComparison.Ordinal);
        Assert.Contains("{\"kind\":\"PullRequest\",\"repository\":\"acme/payments\",\"number\":412}", document, StringComparison.Ordinal);
        Assert.Contains("{\"kind\":\"Plan\",\"plan\":\"goals-standup\"}", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_version_1_document_reads_its_tasks()
    {
        var id = Guid.NewGuid();
        var task = Guid.NewGuid();
        var copy = new ImpedimentReplicaCopyDto(id, "Bank", Version1(id, "Bank sample files missing", """{"state":"New","on":"2026-10-14"}""", task), DateTimeOffset.UtcNow);

        Assert.True(await _repository.TryWriteAsync(copy, Cancellation));

        Assert.Equal([HeldBackItem.ForTask(task).Value], (await _repository.GetAsync(id, Cancellation))!.HoldsBack);
    }

    [Fact]
    public async Task A_held_back_item_without_its_identity_or_of_an_unknown_kind_is_left_out_and_the_impediment_still_reads()
    {
        var id = Guid.NewGuid();
        var content = $$"""
            {"version":2,"id":"{{id}}","title":"Bank sample files missing","history":[{"state":"New","on":"2026-10-14"}],
             "heldBack":[{"kind":"Session","sessionId":"abc"},{"kind":"PullRequest","number":412},{"kind":"Plan","plan":"+goals-standup"}]}
            """;

        Assert.True(await _repository.TryWriteAsync(new ImpedimentReplicaCopyDto(id, "Bank", content, DateTimeOffset.UtcNow), Cancellation));

        Assert.Equal([HeldBackItem.ForPlan("goals-standup").Value], (await _repository.GetAsync(id, Cancellation))!.HoldsBack);
    }

    /// <summary>The carry-over rule (local ADR 0035 §9, Verification 11): a version 1 write
    /// over a version 2 copy takes the inbound fields and tasks, and keeps the local pull
    /// requests and plans where they were linked.</summary>
    [Fact]
    public void A_version_1_copy_over_a_version_2_one_keeps_the_local_pull_requests_and_plans()
    {
        var kept = Guid.NewGuid();
        var dropped = Guid.NewGuid();
        var added = Guid.NewGuid();
        var local = Escalated();
        foreach (var item in local.HoldsBack.Where(item => item.Kind == HeldBackKind.Task).ToList()) local.Release(item);
        local.HoldBack(HeldBackItem.ForTask(dropped).Value);
        local.HoldBack(HeldBackItem.ForTask(kept).Value);
        var inbound = Version1(
            local.Id,
            "Bank sample files still missing",
            """{"state":"New","on":"2026-10-14"},{"state":"Resolved","on":"2026-10-19"}""",
            added,
            kept);

        var merged = ImpedimentDocument.CarryOver(Stored(local), inbound);
        var read = System.Text.Json.JsonSerializer.Deserialize<ImpedimentDocument>(merged, ImpedimentDocument.JsonOptions)!.ToImpediment();

        Assert.Equal("Bank sample files still missing", read.Title);
        Assert.Equal(ImpedimentState.Resolved, read.State);
        Assert.Equal(
            [
                HeldBackItem.ForPullRequest("acme/payments", 412).Value,
                HeldBackItem.ForPlan("goals-standup").Value,
                HeldBackItem.ForTask(kept).Value,
                HeldBackItem.ForTask(added).Value,
            ],
            read.HoldsBack);
    }

    [Fact]
    public void A_version_2_copy_or_one_over_nothing_but_tasks_is_taken_verbatim()
    {
        var local = Escalated();
        var inbound = Stored(Escalated());
        var tasksOnly = Impediment.Raise(local.Id, "Bank sample files missing", Wed14).Value;
        tasksOnly.HoldBack(HeldBackItem.ForTask(Guid.NewGuid()).Value);
        var version1 = Version1(local.Id, "Bank", """{"state":"New","on":"2026-10-14"}""", Guid.NewGuid());

        Assert.Equal(inbound, ImpedimentDocument.CarryOver(Stored(local), inbound));
        Assert.Equal(version1, ImpedimentDocument.CarryOver(Stored(tasksOnly), version1));
        Assert.Equal(version1, ImpedimentDocument.CarryOver(null, version1));
    }

    [Fact]
    public async Task A_version_1_copy_is_stored_with_the_carried_over_items_at_the_stamp_it_arrived_with()
    {
        var local = Escalated();
        await _repository.SaveAsync(local, Cancellation);
        var task = Guid.NewGuid();
        var copy = new ImpedimentReplicaCopyDto(
            local.Id,
            local.Title,
            Version1(local.Id, local.Title, """{"state":"New","on":"2026-10-14"}""", task),
            DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.True(await _repository.TryWriteAsync(copy, Cancellation));

        Assert.Equal(copy.UpdatedAt, (await _repository.ReadAsync(local.Id, Cancellation))!.UpdatedAt);
        Assert.Equal(
            [HeldBackItem.ForPullRequest("acme/payments", 412).Value, HeldBackItem.ForPlan("goals-standup").Value, HeldBackItem.ForTask(task).Value],
            (await _repository.GetAsync(local.Id, Cancellation))!.HoldsBack);
    }

    [Fact]
    public async Task Impediments_list_in_the_order_they_were_raised()
    {
        var later = Escalated("Later", new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero));
        var earlier = Escalated("Earlier", new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero));

        await _repository.SaveAsync(later, Cancellation);
        await _repository.SaveAsync(earlier, Cancellation);

        Assert.Equal(["Earlier", "Later"], (await _repository.ListAsync(Cancellation)).Select(impediment => impediment.Title));
    }

    [Fact]
    public async Task A_row_that_will_not_parse_is_skipped()
    {
        await _repository.SaveAsync(Escalated(), Cancellation);
        await ExecuteAsync("INSERT INTO progress_impediments (id, document, created_at, updated_at) VALUES ('broken', '{not json', '2026-10-01', '2026-10-01');");

        Assert.Single(await _repository.ListAsync(Cancellation));
    }

    [Fact]
    public async Task A_copy_from_another_device_is_kept_verbatim_at_its_stamp()
    {
        var there = new SqliteImpedimentRepository(_other);
        var impediment = Escalated();
        await there.SaveAsync(impediment, Cancellation);
        var copy = (await there.ReadAsync(impediment.Id, Cancellation))!;

        Assert.True(await _repository.TryWriteAsync(copy, Cancellation));

        Assert.Equal(copy, await _repository.ReadAsync(impediment.Id, Cancellation));
        AssertSame(impediment, await _repository.GetAsync(impediment.Id, Cancellation));
        Assert.Equal(impediment.Title, copy.Title);
    }

    [Fact]
    public async Task A_copy_that_does_not_read_as_that_impediment_is_refused()
    {
        var there = new SqliteImpedimentRepository(_other);
        var impediment = Escalated();
        await there.SaveAsync(impediment, Cancellation);
        var copy = (await there.ReadAsync(impediment.Id, Cancellation))!;

        Assert.False(await _repository.TryWriteAsync(copy with { Id = Guid.NewGuid() }, Cancellation));
        Assert.False(await _repository.TryWriteAsync(copy with { Content = "{not json" }, Cancellation));
        Assert.False(await _repository.TryWriteAsync(copy with { Content = "{\"id\":\"" + copy.Id + "\",\"title\":\"x\",\"history\":[]}" }, Cancellation));
        Assert.Empty(await _repository.ReadAllAsync(Cancellation));
    }

    [Fact]
    public async Task A_save_after_a_copy_stamped_ahead_still_moves_the_stamp_forward()
    {
        var impediment = Escalated();
        var ahead = new ImpedimentReplicaCopyDto(
            impediment.Id,
            impediment.Title,
            System.Text.Json.JsonSerializer.Serialize(ImpedimentDocument.From(impediment), ImpedimentDocument.JsonOptions),
            DateTimeOffset.UtcNow.AddHours(1));
        Assert.True(await _repository.TryWriteAsync(ahead, Cancellation));

        await _repository.SaveAsync(impediment, Cancellation);

        Assert.True((await _repository.ReadAsync(impediment.Id, Cancellation))!.UpdatedAt > ahead.UpdatedAt);
    }

    /// <summary>The migration: the table is created on first open under a stamp of its own,
    /// beside the goals', and a file the goals already set up gains it.</summary>
    [Fact]
    public async Task The_table_is_set_up_beside_the_goals_under_its_own_stamp()
    {
        await new SqliteGoalRepository(_dir).ListAsync(Cancellation);
        await _repository.ListAsync(Cancellation);

        Assert.Equal("1", await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'progress_impediments';"));
        Assert.Equal("1", await ScalarAsync("SELECT COUNT(*) FROM schema_stamps WHERE owner = 'progress_impediments';"));
        Assert.Equal("1", await ScalarAsync("SELECT COUNT(*) FROM schema_stamps WHERE owner = 'progress_goals';"));
    }

    [Fact]
    public async Task The_rooted_repository_follows_its_folder()
    {
        var root = _dir;
        var rooted = new RootedSqliteImpedimentRepository(() => root);
        var impediment = Escalated();
        await rooted.SaveAsync(impediment, Cancellation);

        root = _other;

        Assert.Empty(await rooted.ListAsync(Cancellation));
        Assert.NotNull(await new SqliteImpedimentRepository(_dir).GetAsync(impediment.Id, Cancellation));
    }

    /// <summary>What a version 1 build reads of an impediment document's held-back tasks.</summary>
    private sealed record Version1Shape(int Version, List<Guid> HoldsBack);

    private async Task<long> CountRowsAsync() =>
        long.Parse(await ScalarAsync("SELECT COUNT(*) FROM progress_impediments;"), System.Globalization.CultureInfo.InvariantCulture);

    private async Task<string> ScalarAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={_repository.DatabasePath}");
        await connection.OpenAsync(Cancellation);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(Cancellation), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={_repository.DatabasePath}");
        await connection.OpenAsync(Cancellation);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Cancellation);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (var dir in new[] { _dir, _other })
        {
            if (!Directory.Exists(dir)) continue;
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
