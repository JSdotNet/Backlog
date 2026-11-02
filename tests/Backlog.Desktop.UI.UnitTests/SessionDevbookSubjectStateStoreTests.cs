using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The fallback subject store a panel uses when its host composed none — the
/// storybook and the panel tests — keeping the same rules in memory as the file
/// store keeps on disk, so the two never disagree about what a state operation
/// does.
/// </summary>
public sealed class SessionDevbookSubjectStateStoreTests
{
    private const string Repository = "JSdotNet/Backlog";
    private const string Host = "DEV-TOWER";
    private const string Fingerprint = "sha256:2b11c532";

    private static readonly DevbookSubject Debt = DevbookSubject.Debt(".devbook/arc42/tdr/0001-archify.md");
    private static readonly DevbookSubject Change = DevbookSubject.Proposal("pay-by-card");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_state_set_is_the_state_read_back_stamped_by_the_store()
    {
        var store = new SessionDevbookSubjectStateStore(_time);

        store.SetState(Repository, Debt, "planned", Host);

        var state = store.Get(Repository, Debt)!;
        Assert.Equal("planned", state.State);
        Assert.Equal(Host, state.By);
        Assert.Equal(_time.GetUtcNow(), state.UpdatedAt);
        Assert.Same(state, store.Find(state.Id));
        Assert.Equal([state], store.List(Repository));
        Assert.Empty(store.List("JSdotNet/devbook"));
    }

    [Fact]
    public void A_refusal_writes_nothing_and_says_nothing()
    {
        var store = new SessionDevbookSubjectStateStore(_time);
        var raised = 0;
        store.Changed += () => raised++;

        var outcome = store.Accept(Repository, Change, Fingerprint, Host);

        Assert.Equal(DevbookSubjectRefusal.NotApproved, outcome.Refused);
        Assert.Null(store.Get(Repository, Change));
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Approve_then_accept_at_the_same_fingerprint_reads_accepted()
    {
        var store = new SessionDevbookSubjectStateStore(_time);
        var raised = 0;
        store.Changed += () => raised++;

        store.Approve(Repository, Change, Fingerprint, Host);
        store.Accept(Repository, Change, Fingerprint, Host);

        Assert.Equal("accepted", store.Get(Repository, Change)!.State);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void A_replicated_state_keeps_its_stamps_and_a_push_selects_after_the_watermark()
    {
        var store = new SessionDevbookSubjectStateStore(_time);
        var elsewhere = _time.GetUtcNow().AddHours(-1);
        var theirs = new DevbookSubjectState(
            DevbookSubject.IdFor(Repository, Change), Repository, Change, "draft", "OTHER-PC", elsewhere, null, null, elsewhere);

        Assert.True(store.Apply(theirs));
        store.SetState(Repository, Debt, "planned", Host);

        Assert.Equal(theirs with { RepositoryAlias = DevbookSubject.AliasKey(Repository) }, store.Find(theirs.Id));
        Assert.Equal([Debt], store.ListChangedSince(elsewhere).Select(state => state.Subject));
    }

    /// <summary>The newer-than check and the write are one step, so a state set
    /// here while a pull was deciding is never overwritten by the older copy the
    /// pull was holding.</summary>
    [Fact]
    public void A_replicated_state_no_later_than_the_one_held_is_not_written()
    {
        var store = new SessionDevbookSubjectStateStore(_time);
        store.SetState(Repository, Debt, "resolved", Host);
        var held = store.Get(Repository, Debt)!;
        var raised = 0;
        store.Changed += () => raised++;

        Assert.False(store.Apply(held with { State = "planned", UpdatedAt = held.UpdatedAt.AddMinutes(-1) }));
        Assert.False(store.Apply(held with { State = "planned" }));

        Assert.Equal("resolved", store.Get(Repository, Debt)!.State);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Two_spellings_of_one_alias_are_one_subject()
    {
        var store = new SessionDevbookSubjectStateStore(_time);

        store.Approve("JSdotNet/Backlog", Change, Fingerprint, Host);
        var accepted = store.Accept("jsdotnet/backlog", Change, Fingerprint, Host);

        Assert.False(accepted.IsRefused);
        Assert.Equal("accepted", Assert.Single(store.List("JSDOTNET/BACKLOG")).State);
    }

    /// <summary>The direction map beside the subjects: a direction set is read
    /// back under any layout's address, and each port announces only its own
    /// writes, so a pane rebuilding its sync scope is not woken by a subject.</summary>
    [Fact]
    public void A_direction_reads_back_and_each_port_announces_only_its_own_writes()
    {
        var store = new SessionDevbookSubjectStateStore(_time);
        IDevbookSyncDirectionStore directions = store;
        var subjectEvents = 0;
        var directionEvents = 0;
        store.Changed += () => subjectEvents++;
        directions.Changed += () => directionEvents++;

        store.SetState(Repository, Debt, "planned", Host);
        directions.Set(Repository, ".devbook/domain/inbox/context.md", "Pull", Host);

        Assert.Equal("pull", directions.Get("jsdotnet/backlog", ".domain/inbox/context.md"));
        Assert.Equal((1, 1), (subjectEvents, directionEvents));

        directions.Import(Repository, [("domain/inbox/context.md", "push"), ("domain/sessions/context.md", "push")]);
        directions.Import(Repository, [("domain/tasks/domain.md#task", "push")]);

        Assert.Equal(["pull", "push"], directions.List(Repository).Select(statement => statement.Direction));
        Assert.True(directions.IsImported(Repository));
        Assert.Equal((1, 2), (subjectEvents, directionEvents));
    }

    /// <summary>The import's write: only what the store does not hold, as given,
    /// announced once — and a second run writes and announces nothing.</summary>
    [Fact]
    public void An_import_writes_only_what_is_absent_and_raises_changed_once()
    {
        var store = new SessionDevbookSubjectStateStore(_time);
        store.SetState(Repository, Debt, "resolved", Host);
        var raised = 0;
        store.Changed += () => raised++;
        var authored = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        IReadOnlyList<DevbookSubjectState> batch =
        [
            new(DevbookSubject.IdFor(Repository, Debt), Repository, Debt, "planned", "Job Schepers", authored, null, null, authored),
            new(DevbookSubject.IdFor(Repository, Change), Repository, Change, "draft", "Job Schepers", authored, null, null, authored),
        ];

        Assert.Equal(1, store.ImportIfAbsent(batch));
        Assert.Equal(1, raised);
        Assert.Equal("resolved", store.Get(Repository, Debt)!.State);
        Assert.Equal(authored, store.Get(Repository, Change)!.UpdatedAt);

        Assert.Equal(0, store.ImportIfAbsent(batch));
        Assert.Equal(1, raised);
    }
}
