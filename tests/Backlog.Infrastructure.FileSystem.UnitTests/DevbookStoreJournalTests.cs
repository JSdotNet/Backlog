using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.Sqlite.Changes;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel.Changes;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The devbook subject states and the remarks in the change journal (local ADR 0061 §2):
/// JSON files no trigger reaches, so each store records the prior document through the
/// journal port in <c>backlog.db</c> before it writes the file, withdraws it when the file
/// write fails, and writes the document back through its own write path when a person
/// restores the change set.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class DevbookStoreJournalTests : IDisposable
{
    private const string Repository = "JSdotNet/Backlog";
    private const string Chapter = ".domain/devbook/features.md";
    private const string Host = "DEV-TOWER";

    private static readonly IReadOnlySet<DocumentKey> NoOverrides = new HashSet<DocumentKey>();
    private static readonly DevbookSubject Debt = DevbookSubject.Debt(".devbook/arc42/tdr/0001-almanac.md");

    private readonly TemporaryDirectory _temp = new("devbook-store-journal-tests");
    private readonly DevbookSubjectStateStore _subjects;
    private readonly DevbookAnnotationStore _remarks;
    private readonly SqliteChangeRestore _restore;
    private readonly SqliteChangeJournal _journal;

    public DevbookStoreJournalTests()
    {
        var journal = new SqliteDocumentJournal(_temp.Path);
        _subjects = new DevbookSubjectStateStore(_temp.Path, journal: journal);
        _remarks = new DevbookAnnotationStore(_temp.Path, journal: journal);

        var tasks = new SqliteTaskRepository(_temp.Path);
        _restore = new SqliteChangeRestore(_temp.Path, tasks, stores: [_subjects, _remarks]);
        _journal = new SqliteChangeJournal(tasks.DatabasePath);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Restoring_a_devbook_set_state_call_returns_the_subject_to_its_prior_state()
    {
        using (ChangeContext.Begin(ChangeActor.Person, "planning"))
        {
            _subjects.SetState(Repository, Debt, "planned", Host);
        }

        var agent = Agent("devbook_set_state", () => _subjects.SetState(Repository, Debt, "resolved", "claude-code"));
        var resolved = _subjects.Get(Repository, Debt)!;
        Assert.Equal("resolved", resolved.State);

        var step = Assert.Single((await _restore.PlanAsync(agent.Id, Token))!.Steps);
        Assert.Equal(new DocumentKey(DevbookSubjectStateStore.DocumentKind, resolved.Id.ToString()), step.Key);
        Assert.Equal(RestoreAction.Revert, step.Action);
        Assert.False(step.Conflict);

        var outcome = await _restore.RestoreAsync(agent.Id, NoOverrides, Token);

        Assert.Equal(1, outcome.Restored);
        var restored = _subjects.Get(Repository, Debt)!;
        Assert.Equal("planned", restored.State);
        Assert.Equal(Host, restored.By);
        Assert.True(restored.UpdatedAt > resolved.UpdatedAt, "A restore is a later write, so it replicates.");

        // Read back from disk, not only from memory.
        Assert.Equal("planned", new DevbookSubjectStateStore(_temp.Path).Get(Repository, Debt)!.State);
    }

    [Fact]
    public async Task Restoring_the_first_state_a_change_set_set_leaves_the_subject_with_none()
    {
        var agent = Agent("devbook_set_state", () => _subjects.SetState(Repository, Debt, "planned", "claude-code"));

        Assert.Equal(RestoreAction.Tombstone, Assert.Single((await _restore.PlanAsync(agent.Id, Token))!.Steps).Action);

        var outcome = await _restore.RestoreAsync(agent.Id, NoOverrides, Token);

        Assert.Equal(1, outcome.Restored);
        Assert.Null(_subjects.Get(Repository, Debt));
        Assert.Null(new DevbookSubjectStateStore(_temp.Path).Get(Repository, Debt));
    }

    [Fact]
    public async Task Restoring_a_posted_note_takes_it_back_off_the_chapter()
    {
        DevbookAnnotation posted = null!;
        var agent = Agent("devbook_post_note", () =>
        {
            posted = _remarks.Add(Repository, Chapter, 2, "claude-code", kind: DevbookAnnotationKind.Question);
            _remarks.Edit(posted.Id, "Who owns this decision?");
        });
        Assert.Single(_remarks.List(Repository, Chapter));

        var step = Assert.Single((await _restore.PlanAsync(agent.Id, Token))!.Steps);
        Assert.Equal(new DocumentKey(DevbookAnnotationStore.DocumentKind, posted.Id.ToString()), step.Key);
        Assert.Equal(RestoreAction.Tombstone, step.Action);

        var outcome = await _restore.RestoreAsync(agent.Id, NoOverrides, Token);

        Assert.Equal(1, outcome.Restored);
        Assert.Empty(_remarks.List(Repository, Chapter));
        Assert.NotNull(_remarks.Find(posted.Id)!.DeletedAt);
        Assert.Empty(new DevbookAnnotationStore(_temp.Path).List(Repository, Chapter));
    }

    [Fact]
    public async Task Restoring_a_resolve_reopens_the_note_with_its_text()
    {
        DevbookAnnotation note;
        using (ChangeContext.Begin(ChangeActor.Person, "noting"))
        {
            note = _remarks.Add(Repository, Chapter, 0, Host);
            _remarks.Edit(note.Id, "Check the numbers.");
        }

        var agent = Agent("devbook_resolve_note", () =>
        {
            _remarks.Edit(note.Id, "Check the numbers.\n\nDone: they hold.");
            _remarks.SetResolved(note.Id, true);
        });

        var outcome = await _restore.RestoreAsync(agent.Id, NoOverrides, Token);

        Assert.Equal(1, outcome.Restored);
        var restored = Assert.Single(_remarks.List(Repository, Chapter));
        Assert.False(restored.Resolved);
        Assert.Equal("Check the numbers.", restored.Body);
    }

    [Fact]
    public async Task A_remark_edited_since_is_a_conflict_kept_as_it_is()
    {
        DevbookAnnotation note;
        using (ChangeContext.Begin(ChangeActor.Person, "noting"))
        {
            note = _remarks.Add(Repository, Chapter, 0, Host);
            _remarks.Edit(note.Id, "First.");
        }

        var agent = Agent("devbook_post_note", () => _remarks.Edit(note.Id, "First. Agent's reply."));
        using (ChangeContext.Begin(ChangeActor.Person, "editing"))
        {
            _remarks.Edit(note.Id, "Mine since.");
        }

        Assert.True(Assert.Single((await _restore.PlanAsync(agent.Id, Token))!.Steps).Conflict);

        var outcome = await _restore.RestoreAsync(agent.Id, NoOverrides, Token);

        Assert.Equal(0, outcome.Restored);
        Assert.Equal("Mine since.", _remarks.Find(note.Id)!.Body);
    }

    [Fact]
    public async Task A_file_write_that_fails_takes_its_revision_back_out_of_the_journal()
    {
        // A file where the store's folder should be: the state file cannot be written.
        await File.WriteAllTextAsync(Path.Combine(_temp.Path, DevbookSubjectStateStore.FolderName), "in the way", Token);

        var agent = Agent("devbook_set_state", () => _subjects.SetState(Repository, Debt, "planned", "claude-code"));

        Assert.DoesNotContain(await _journal.ListAsync(cancellationToken: Token), entry => entry.ChangeSet.Id == agent.Id);
        Assert.Empty(await _journal.ReadAsync(agent.Id, Token));
    }

    [Fact]
    public async Task A_restore_whose_file_write_fails_restores_nothing_and_can_be_tried_again()
    {
        using (ChangeContext.Begin(ChangeActor.Person, "planning"))
        {
            _subjects.SetState(Repository, Debt, "planned", Host);
        }

        var agent = Agent("devbook_set_state", () => _subjects.SetState(Repository, Debt, "resolved", "claude-code"));
        var file = Assert.Single(Directory.GetFiles(Path.Combine(_temp.Path, DevbookSubjectStateStore.FolderName)));
        File.SetAttributes(file, FileAttributes.ReadOnly);

        try
        {
            var outcome = await _restore.RestoreAsync(agent.Id, NoOverrides, Token);

            Assert.Equal(0, outcome.Restored);
            Assert.Null(outcome.RestoreChangeSetId);
            Assert.Null((await _restore.PlanAsync(agent.Id, Token))!.UndoneBy);
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task A_devbook_import_is_one_change_set()
    {
        var states = new[]
        {
            new DevbookSubjectState(Guid.NewGuid(), Repository, DevbookSubject.Debt(".devbook/arc42/tdr/0002-a.md"), "planned", Host, DateTimeOffset.UtcNow, null, null, DateTimeOffset.UtcNow),
            new DevbookSubjectState(Guid.NewGuid(), Repository, DevbookSubject.Debt(".devbook/arc42/tdr/0003-b.md"), "planned", Host, DateTimeOffset.UtcNow, null, null, DateTimeOffset.UtcNow),
        };

        Assert.Equal(2, _subjects.ImportIfAbsent(states));

        var import = Assert.Single(await _journal.ListAsync(cancellationToken: Token));
        Assert.Equal(ChangeActor.System, import.ChangeSet.Actor);
        Assert.Equal(2, import.DocumentCount);
    }

    [Fact]
    public async Task A_write_no_entry_point_named_is_recorded_under_an_unknown_writer()
    {
        _subjects.SetState(Repository, Debt, "planned", Host);

        var unknown = Assert.Single(await _journal.ListAsync(cancellationToken: Token));
        Assert.Equal(ChangeActor.Unknown, unknown.ChangeSet.Actor);
        var document = Assert.Single(await _journal.ReadAsync(unknown.ChangeSet.Id, Token));
        Assert.Equal(DevbookSubjectStateStore.DocumentKind, document.Kind);
        Assert.Null(document.Before);
    }

    private static ChangeSet Agent(string label, Action writes)
    {
        var agent = ChangeSet.Start(ChangeActor.Agent, label) with { Caller = "claude-code", SessionId = "session-1" };
        using (ChangeContext.Enter(agent))
        {
            writes();
        }

        return agent;
    }

    public void Dispose() => _temp.Dispose();
}
