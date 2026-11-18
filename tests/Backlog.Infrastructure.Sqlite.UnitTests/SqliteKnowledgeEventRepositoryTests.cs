using System.Globalization;

using Backlog.Infrastructure.Sqlite.Events;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The stored knowledge events: one row per event in <c>events_knowledge_events</c>, the whole
/// event as one JSON document (local ADR 0050 §2, the shape ADR 0039 §3 gives an owned
/// document). What goes in comes back out field for field, saving again keeps one row with its
/// first-saved stamp and a later <c>updated_at</c>, and a row that will not parse is skipped
/// rather than failing the read.
/// </summary>
public sealed class SqliteKnowledgeEventRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Amsterdam = TimeSpan.FromHours(1);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "knowledge-event-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _other = Path.Combine(Path.GetTempPath(), "knowledge-event-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(Noon);
    private readonly SqliteKnowledgeEventRepository _repository;

    public SqliteKnowledgeEventRepositoryTests() => _repository = new SqliteKnowledgeEventRepository(_dir, _clock);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>An attended event with every field the aggregate carries set, built through its
    /// public rules rather than restored, so what is stored is what the module writes.</summary>
    private static KnowledgeEvent Attended()
    {
        var knowledgeEvent = KnowledgeEvent.Create(Guid.NewGuid(), "Techorama 2026").Value;

        knowledgeEvent.SetSeries(Guid.NewGuid());
        Assert.True(knowledgeEvent.SetDates(
            EventDate.Create(new DateOnly(2026, 10, 26), new TimeOnly(8, 30), Amsterdam).Value,
            EventDate.Create(new DateOnly(2026, 10, 28), null, null).Value,
            DateSource.EventPage).IsSuccess);
        knowledgeEvent.SetPlace(Place.AtVenue("Kinepolis Utrecht").Value);
        knowledgeEvent.SetLink("https://techorama.nl/2026");
        knowledgeEvent.SetWhatToLearn("Aspire in production; what .NET 11 changes for MAUI");
        knowledgeEvent.SetKeyDates(
        [
            KeyDate.Create(new DateOnly(2026, 6, 30), "Early bird ends").Value,
            KeyDate.Create(new DateOnly(2026, 9, 1), "Registration closes").Value,
        ]);
        knowledgeEvent.SetTicket(TicketState.Bought);
        knowledgeEvent.SetCost(Cost.Create(1295.50m, "eur").Value);
        Assert.True(knowledgeEvent.LinkNote(NoteLink.Create(NoteLinkKind.Brain, "knowledge/aspire.md", "Aspire")).IsSuccess);
        Assert.True(knowledgeEvent.LinkNote(NoteLink.Create(NoteLinkKind.Talk, "inbox-42", null)).IsSuccess);
        Assert.True(knowledgeEvent.LinkTask(EventTaskLink.Create("task-hotel", TaskPurpose.Prep)).IsSuccess);
        Assert.True(knowledgeEvent.LinkHarvestTask("task-harvest").IsSuccess);
        Assert.True(knowledgeEvent.MoveTo(KnowledgeEventState.Attended).IsSuccess);
        Assert.True(knowledgeEvent.SetVerdict("Worth it for the Aspire track alone").IsSuccess);

        return knowledgeEvent;
    }

    private static KnowledgeEvent TitleOnly(string title = "dotNET Friday") =>
        KnowledgeEvent.Create(Guid.NewGuid(), title).Value;

    [Fact]
    public async Task A_first_run_has_no_event()
    {
        Assert.Empty(await _repository.ListAsync(Cancellation));
        Assert.Null(await _repository.GetAsync(Guid.NewGuid(), Cancellation));
    }

    [Fact]
    public async Task A_fully_populated_event_reads_back_field_for_field()
    {
        var stored = Attended();

        await _repository.SaveAsync(stored, Cancellation);
        var read = await _repository.GetAsync(stored.Id, Cancellation);

        Assert.NotNull(read);
        Assert.Equal(stored.Id, read.Id);
        Assert.Equal("Techorama 2026", read.Title);
        Assert.Equal(stored.Series, read.Series);
        Assert.Equal(new DateOnly(2026, 10, 26), read.Start!.Date);
        Assert.Equal(new TimeOnly(8, 30), read.Start.Time);
        Assert.Equal(Amsterdam, read.Start.UtcOffset);
        Assert.Equal(stored.End, read.End);
        Assert.Null(read.End!.Time);
        Assert.Equal(DateSource.EventPage, read.DateSource);
        Assert.Equal("Kinepolis Utrecht", read.Place!.Venue);
        Assert.False(read.Place.IsOnline);
        Assert.Equal("https://techorama.nl/2026", read.Link);
        Assert.Equal(KnowledgeEventState.Attended, read.State);
        Assert.Null(read.SkipReason);
        Assert.Equal("Aspire in production; what .NET 11 changes for MAUI", read.WhatToLearn);
        Assert.Equal(
            [(new DateOnly(2026, 6, 30), "Early bird ends"), (new DateOnly(2026, 9, 1), "Registration closes")],
            read.KeyDates.Select(keyDate => (keyDate.Date, keyDate.Label)));
        Assert.Equal(TicketState.Bought, read.Ticket);
        Assert.Equal(1295.50m, read.Cost!.Amount);
        Assert.Equal("EUR", read.Cost.Currency);
        Assert.Equal(
            [(NoteLinkKind.Brain, "knowledge/aspire.md", "Aspire"), (NoteLinkKind.Talk, "inbox-42", "")],
            read.Notes.Select(note => (note.Kind, note.Reference, note.Title)));
        Assert.Equal(
            [("task-hotel", TaskPurpose.Prep), ("task-harvest", TaskPurpose.Harvest)],
            read.Tasks.Select(task => (task.TaskId, task.Purpose)));
        Assert.Equal("Worth it for the Aspire track alone", read.Verdict!.Text);
    }

    [Fact]
    public async Task A_title_only_event_reads_back_spotted_with_every_other_field_empty()
    {
        var stored = TitleOnly();

        await _repository.SaveAsync(stored, Cancellation);
        var read = await _repository.GetAsync(stored.Id, Cancellation);

        Assert.NotNull(read);
        Assert.Equal("dotNET Friday", read.Title);
        Assert.Equal(KnowledgeEventState.Spotted, read.State);
        Assert.Null(read.Series);
        Assert.Null(read.Start);
        Assert.Null(read.End);
        Assert.Null(read.DateSource);
        Assert.Null(read.Place);
        Assert.Null(read.Link);
        Assert.Null(read.SkipReason);
        Assert.Null(read.WhatToLearn);
        Assert.Empty(read.KeyDates);
        Assert.Null(read.Ticket);
        Assert.Null(read.Cost);
        Assert.Empty(read.Notes);
        Assert.Empty(read.Tasks);
        Assert.Null(read.Verdict);
    }

    [Fact]
    public async Task A_skipped_online_event_keeps_its_reason_and_its_place()
    {
        var stored = TitleOnly("A webinar on nothing in particular");
        stored.SetPlace(Place.Online);
        Assert.True(stored.Skip("Clashes with the sprint review").IsSuccess);

        await _repository.SaveAsync(stored, Cancellation);
        var read = await _repository.GetAsync(stored.Id, Cancellation);

        Assert.Equal(KnowledgeEventState.Skipped, read!.State);
        Assert.Equal("Clashes with the sprint review", read.SkipReason!.Text);
        Assert.Same(Place.Online, read.Place);
    }

    [Fact]
    public async Task Saving_again_keeps_one_row_its_first_saved_stamp_and_a_later_updated_at()
    {
        var knowledgeEvent = TitleOnly();
        await _repository.SaveAsync(knowledgeEvent, Cancellation);

        _clock.SetUtcNow(Noon.AddMinutes(5));
        Assert.True(knowledgeEvent.Rename("dotNET Friday Q4").IsSuccess);
        await _repository.SaveAsync(knowledgeEvent, Cancellation);

        Assert.Equal("1", await ScalarAsync("SELECT COUNT(*) FROM events_knowledge_events;"));
        Assert.Equal(Stamp(Noon), await ScalarAsync("SELECT created_at FROM events_knowledge_events;"));
        Assert.Equal(Stamp(Noon.AddMinutes(5)), await ScalarAsync("SELECT updated_at FROM events_knowledge_events;"));
        Assert.Equal("dotNET Friday Q4", (await _repository.GetAsync(knowledgeEvent.Id, Cancellation))!.Title);
    }

    [Fact]
    public async Task The_list_holds_every_event_in_the_order_they_were_first_saved()
    {
        var first = TitleOnly("First");
        var second = TitleOnly("Second");

        await _repository.SaveAsync(first, Cancellation);
        _clock.SetUtcNow(Noon.AddMinutes(1));
        await _repository.SaveAsync(second, Cancellation);
        _clock.SetUtcNow(Noon.AddMinutes(2));
        await _repository.SaveAsync(first, Cancellation);

        Assert.Equal([first.Id, second.Id], (await _repository.ListAsync(Cancellation)).Select(knowledgeEvent => knowledgeEvent.Id));
    }

    [Fact]
    public async Task A_row_that_is_not_json_is_skipped()
    {
        var kept = TitleOnly();
        await _repository.SaveAsync(kept, Cancellation);
        await InsertRawAsync(BadId, "{not json");

        Assert.Equal([kept.Id], (await _repository.ListAsync(Cancellation)).Select(knowledgeEvent => knowledgeEvent.Id));
        Assert.Null(await _repository.GetAsync(Guid.Parse(BadId), Cancellation));
    }

    private const string BadId = "11111111-1111-1111-1111-111111111111";

    /// <summary>Documents that parse as JSON but do not read as a knowledge event: a later
    /// shape, no title, a state outside the fixed set, a time with no offset, a blank note
    /// reference, and a negative cost.</summary>
    [Theory]
    [InlineData("""{"version":9,"id":"11111111-1111-1111-1111-111111111111","title":"Later shape","state":"Spotted"}""")]
    [InlineData("""{"version":1,"id":"11111111-1111-1111-1111-111111111111","title":"  ","state":"Spotted"}""")]
    [InlineData("""{"version":1,"id":"11111111-1111-1111-1111-111111111111","title":"Odd state","state":42}""")]
    [InlineData("""{"version":1,"id":"11111111-1111-1111-1111-111111111111","title":"No offset","state":"Spotted","start":{"date":"2026-10-26","time":"08:30:00"},"dateSource":"Typed"}""")]
    [InlineData("""{"version":1,"id":"11111111-1111-1111-1111-111111111111","title":"Blank note","state":"Spotted","notes":[{"kind":"Brain","reference":" "}]}""")]
    [InlineData("""{"version":1,"id":"11111111-1111-1111-1111-111111111111","title":"Negative cost","state":"Spotted","cost":{"amount":-1,"currency":"EUR"}}""")]
    public async Task A_row_that_parses_but_does_not_read_as_a_knowledge_event_is_skipped(string document)
    {
        var kept = TitleOnly();
        await _repository.SaveAsync(kept, Cancellation);
        await InsertRawAsync(BadId, document);

        Assert.Equal([kept.Id], (await _repository.ListAsync(Cancellation)).Select(knowledgeEvent => knowledgeEvent.Id));
        Assert.Null(await _repository.GetAsync(Guid.Parse(BadId), Cancellation));
    }

    /// <summary>The table is set up under its own stamp (local ADR 0006), and only its own: the
    /// series table is the series store's to create (inherited ADR 0014).</summary>
    [Fact]
    public async Task The_table_is_set_up_under_its_own_stamp_and_no_other()
    {
        await _repository.ListAsync(Cancellation);

        Assert.Equal("1", await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'events_knowledge_events';"));
        Assert.Equal(SqliteKnowledgeEventRepository.Schema.Fingerprint, await ScalarAsync("SELECT fingerprint FROM schema_stamps WHERE owner = 'events_knowledge_events';"));
        Assert.Equal("0", await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'events_series';"));
    }

    [Fact]
    public async Task The_rooted_repository_follows_its_folder()
    {
        var root = _dir;
        var rooted = new RootedSqliteKnowledgeEventRepository(() => root);
        var knowledgeEvent = TitleOnly();
        await rooted.SaveAsync(knowledgeEvent, Cancellation);

        root = _other;

        Assert.Empty(await rooted.ListAsync(Cancellation));
        Assert.NotNull(await _repository.GetAsync(knowledgeEvent.Id, Cancellation));
    }

    private static string Stamp(DateTimeOffset stamp) => stamp.ToString("O", CultureInfo.InvariantCulture);

    private async Task InsertRawAsync(string id, string document)
    {
        await using var connection = new SqliteConnection(SqliteSchema.ConnectionString(_repository.DatabasePath));
        await connection.OpenAsync(Cancellation);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO events_knowledge_events (id, state, created_at, updated_at, document) VALUES ($id, 'Spotted', '2026-01-01', '2026-01-01', $document);";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$document", document);
        await command.ExecuteNonQueryAsync(Cancellation);
    }

    private async Task<string> ScalarAsync(string sql)
    {
        await using var connection = new SqliteConnection(SqliteSchema.ConnectionString(_repository.DatabasePath));
        await connection.OpenAsync(Cancellation);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(Cancellation), CultureInfo.InvariantCulture)!;
    }

    /// <summary>Lets go of this test's own databases only — clearing every pool would reach the
    /// tests running beside it (see <c>SqlitePoolClearingTests</c>).</summary>
    public void Dispose()
    {
        foreach (var dir in new[] { _dir, _other })
        {
            SqliteDatabaseFile.ReleasePool(SqliteTaskRepository.DatabasePathFor(dir));

            if (!Directory.Exists(dir)) continue;
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
