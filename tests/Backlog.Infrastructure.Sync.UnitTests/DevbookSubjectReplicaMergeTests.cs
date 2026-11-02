using Backlog.Infrastructure.Sync.DevbookSubjects;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using SubjectNames = Backlog.Modules.Devbook.Abstractions.DevbookSubjects;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The rules two desktops have to agree on for a subject's state: which of two
/// records in one page is later, whether an arriving version replaces the local
/// one, and that the wire and the store say the same thing.
/// <see cref="AnnotationReplicaMergeTests"/>' questions without the tombstone,
/// plus the one a derived id asks: a document filed under any id but its
/// subject's would be a second state for one subject.
/// </summary>
public class DevbookSubjectReplicaMergeTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid DeviceA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000");
    private static readonly Guid DeviceB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000000");

    [Fact]
    public void The_replica_stamp_decides_first_then_the_device_stamp_then_the_device_id()
    {
        var earlier = Subjects.Record(Subjects.Change("identified", Noon), DeviceA, 10);
        var later = Subjects.Record(Subjects.Change("planned", Noon.AddMinutes(-5)), DeviceB, 11);
        Assert.True(DevbookSubjectReplicaMerge.Wins(later, earlier));
        Assert.False(DevbookSubjectReplicaMerge.Wins(earlier, later));

        var olderDevice = Subjects.Record(Subjects.Change("identified", Noon), DeviceA, 10);
        var newerDevice = Subjects.Record(Subjects.Change("planned", Noon.AddMinutes(1)), DeviceB, 10);
        Assert.True(DevbookSubjectReplicaMerge.Wins(newerDevice, olderDevice));

        var a = Subjects.Record(Subjects.Change("identified", Noon), DeviceA, 10);
        var b = Subjects.Record(Subjects.Change("planned", Noon), DeviceB, 10);
        Assert.True(DevbookSubjectReplicaMerge.Wins(b, a));
        Assert.False(DevbookSubjectReplicaMerge.Wins(a, b));
    }

    [Fact]
    public void A_state_this_device_has_never_seen_is_written()
    {
        var store = new InMemoryDevbookSubjectStateStore();
        var merge = new DevbookSubjectReplicaMerge(store);

        var outcome = merge.Apply([Subjects.Record(Subjects.Change("planned", Noon), DeviceB, 1)]);

        Assert.Equal(1, outcome.Applied);

        var written = Assert.Single(store.Applied);
        Assert.Equal(Subjects.DebtId, written.Id);
        Assert.Equal(Subjects.Repository, written.RepositoryAlias);
        Assert.Equal(Subjects.Debt, written.Subject);
        Assert.Equal("planned", written.State);
        Assert.Equal("DEV-LAPTOP", written.By);
        Assert.Equal(Noon, written.At);
        Assert.Equal(Noon, written.UpdatedAt);
        Assert.Equal(written, store.Get(Subjects.Repository, Subjects.Debt));
    }

    [Fact]
    public void This_devices_own_echo_changes_nothing()
    {
        var store = new InMemoryDevbookSubjectStateStore();
        store.Seed(Subjects.Local("planned", Noon));
        var merge = new DevbookSubjectReplicaMerge(store);

        var outcome = merge.Apply([Subjects.Record(Subjects.Change("planned", Noon), DeviceA, 5)]);

        Assert.Equal(0, outcome.Applied);
        Assert.Empty(store.Applied);
    }

    [Fact]
    public void A_later_state_from_elsewhere_replaces_the_local_one_whole()
    {
        var store = new InMemoryDevbookSubjectStateStore();
        store.Seed(Subjects.Local("identified", Noon));
        var merge = new DevbookSubjectReplicaMerge(store);

        merge.Apply([Subjects.Record(Subjects.Change("planned", Noon.AddMinutes(5)), DeviceB, 5)]);

        var written = Assert.Single(store.Applied);
        Assert.Equal("planned", written.State);
        Assert.Equal("DEV-LAPTOP", written.By);
    }

    [Fact]
    public void An_older_state_never_replaces_a_later_local_one()
    {
        var store = new InMemoryDevbookSubjectStateStore();
        store.Seed(Subjects.Local("planned", Noon.AddMinutes(5)));
        var merge = new DevbookSubjectReplicaMerge(store);

        var outcome = merge.Apply([Subjects.Record(Subjects.Change("identified", Noon), DeviceB, 5)]);

        Assert.Equal(0, outcome.Applied);
        Assert.Empty(store.Applied);
    }

    /// <summary>
    /// A person sets a state here while a pull is being merged — after the merge
    /// could have read the local copy, before it writes. The newer-than check is
    /// the store's, made with the write, so the older copy arriving is refused and
    /// the person's state stays; a merge that decided on a copy it read first would
    /// have put the older copy over it, and nothing would ever push it again.
    /// </summary>
    [Fact]
    public void A_state_set_here_while_the_page_is_merged_is_never_overwritten_by_an_older_arrival()
    {
        var store = new InMemoryDevbookSubjectStateStore();
        store.Seed(Subjects.Local("identified", Noon));
        store.BeforeNextApply = () => store.Seed(Subjects.Local("resolved", Noon.AddMinutes(10)));
        var merge = new DevbookSubjectReplicaMerge(store);

        var outcome = merge.Apply([Subjects.Record(Subjects.Change("planned", Noon.AddMinutes(5)), DeviceB, 5)]);

        Assert.Equal(0, outcome.Applied);
        Assert.Empty(store.Applied);
        Assert.Equal("resolved", store.Get(Subjects.Repository, Subjects.Debt)!.State);
    }

    [Fact]
    public void Two_records_for_one_subject_in_a_page_cost_one_write()
    {
        var store = new InMemoryDevbookSubjectStateStore();
        var merge = new DevbookSubjectReplicaMerge(store);

        var outcome = merge.Apply(
            [
                Subjects.Record(Subjects.Change("identified", Noon), DeviceB, 1),
                Subjects.Record(Subjects.Change("planned", Noon.AddMinutes(1)), DeviceB, 2),
            ]);

        Assert.Equal(1, outcome.Applied);
        Assert.Equal("planned", Assert.Single(store.Applied).State);
    }

    /// <summary>A proposal carries its approval and acceptance, a decision record
    /// its pull request; both have to come back out of the wire as they went
    /// in.</summary>
    [Fact]
    public void The_wire_shape_round_trips_the_stored_record()
    {
        var proposal = DevbookSubject.Proposal("add-subject-store");
        var approval = new DevbookSubjectDecision("DEV-TOWER", Noon, "sha256:0123abcd");
        var accepted = Subjects.Local("accepted", Noon.AddMinutes(1), proposal) with
        {
            Approval = approval,
            Acceptance = approval with { At = Noon.AddMinutes(1) },
        };
        var decided = Subjects.Local("decided", Noon, DevbookSubject.Decision(".devbook/arc42/adr/0044-subject-state.md", 1604));

        Assert.Equal(accepted, DevbookSubjectReplicaMerge.ToState(DevbookSubjectReplicaMerge.ToChange(accepted)));
        Assert.Equal(decided, DevbookSubjectReplicaMerge.ToState(DevbookSubjectReplicaMerge.ToChange(decided)));

        var wire = DevbookSubjectReplicaMerge.ToChange(decided).Subject;
        Assert.Equal("decision", wire.Kind);
        Assert.Equal(".devbook/arc42/adr/0044-subject-state.md", wire.Key);
        Assert.Equal(1604, wire.PullRequest);
    }

    /// <summary>Two devices converge on one document because both derive its id
    /// from the alias and the subject. A record under any other id was not
    /// written by a device of ours, and taking it would give the subject a second
    /// state that nothing reads.</summary>
    [Fact]
    public void A_record_filed_under_another_id_than_its_subjects_is_not_taken()
    {
        var store = new InMemoryDevbookSubjectStateStore();
        var merge = new DevbookSubjectReplicaMerge(store);

        var stray = Subjects.Change("planned", Noon) with { Id = Guid.CreateVersion7() };

        var outcome = merge.Apply([Subjects.Record(stray, DeviceB, 1)]);

        Assert.Equal(0, outcome.Applied);
        Assert.Empty(store.Applied);
    }

    /// <summary>The service checks what it stores, but a page is still outside
    /// input to this machine: a record its own rule refuses is skipped, never
    /// thrown on, so it cannot stop every other record in the feed.</summary>
    [Theory]
    [InlineData("state outside the row")]
    [InlineData("unknown kind")]
    [InlineData("key that names nothing")]
    public void A_record_this_device_could_not_file_is_skipped(string shape)
    {
        var store = new InMemoryDevbookSubjectStateStore();
        var merge = new DevbookSubjectReplicaMerge(store);
        var change = Subjects.Change("planned", Noon);
        var bad = shape switch
        {
            "state outside the row" => change with { Subject = change.Subject with { State = "someday" } },
            "unknown kind" => change with { Subject = change.Subject with { Kind = "chapter" } },
            "key that names nothing" => change with { Subject = change.Subject with { Key = "./" } },
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "No such shape."),
        };

        var outcome = merge.Apply(
            [
                Subjects.Record(bad, DeviceB, 1),
                Subjects.Record(Subjects.Change("planned", Noon) with { Id = DevbookSubject.IdFor("JSdotNet/Other", Subjects.Debt), Subject = change.Subject with { RepositoryAlias = "JSdotNet/Other" } }, DeviceB, 2),
            ]);

        Assert.Equal(1, outcome.Applied);
        Assert.Equal("JSdotNet/Other", Assert.Single(store.Applied).RepositoryAlias);
    }

    /// <summary>The wire's copy of the contract's rows against the Devbook
    /// module's (<see cref="DevbookSubjectRecordLimits"/> may not see that
    /// module): a state the module grows without the wire refuses every push that
    /// carries it, and this is where that drift fails first.</summary>
    [Fact]
    public void The_wire_knows_every_state_the_module_does()
    {
        foreach (var kind in Enum.GetValues<DevbookSubjectKind>())
        {
            Assert.Equal(DevbookSubject.StatesOf(kind), DevbookSubjectRecordLimits.StatesOf(SubjectNames.KindName(kind)));
        }
    }
}
