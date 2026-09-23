using Backlog.Infrastructure.Sync.Annotations;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The rules two desktops have to agree on for a remark: which of two records
/// in one page is later, and whether an arriving version replaces the local
/// one. <see cref="TaskReplicaMergeTests"/>' questions over the annotation
/// shape, because the answers are the same and have to stay so.
/// </summary>
public class AnnotationReplicaMergeTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid DeviceA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000");
    private static readonly Guid DeviceB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000000");

    [Fact]
    public void The_replica_stamp_decides_first_then_the_device_stamp_then_the_device_id()
    {
        var id = Guid.NewGuid();

        var earlier = Annotations.Record(Annotations.Change(id, "one", Noon), DeviceA, 10);
        var later = Annotations.Record(Annotations.Change(id, "two", Noon.AddMinutes(-5)), DeviceB, 11);
        Assert.True(AnnotationReplicaMerge.Wins(later, earlier));
        Assert.False(AnnotationReplicaMerge.Wins(earlier, later));

        var olderDevice = Annotations.Record(Annotations.Change(id, "one", Noon), DeviceA, 10);
        var newerDevice = Annotations.Record(Annotations.Change(id, "two", Noon.AddMinutes(1)), DeviceB, 10);
        Assert.True(AnnotationReplicaMerge.Wins(newerDevice, olderDevice));

        var a = Annotations.Record(Annotations.Change(id, "one", Noon), DeviceA, 10);
        var b = Annotations.Record(Annotations.Change(id, "two", Noon), DeviceB, 10);
        Assert.True(AnnotationReplicaMerge.Wins(b, a));
        Assert.False(AnnotationReplicaMerge.Wins(a, b));
    }

    [Fact]
    public void A_remark_this_device_has_never_seen_is_written()
    {
        var store = new InMemoryDevbookAnnotationStore();
        var merge = new AnnotationReplicaMerge(store);
        var id = Guid.NewGuid();

        var outcome = merge.Apply(
            [Annotations.Record(Annotations.Change(id, "From the laptop.", Noon), DeviceB, 1)]);

        Assert.Equal(1, outcome.Applied);

        var written = Assert.Single(store.Applied);
        Assert.Equal(id, written.Id);
        Assert.Equal("From the laptop.", written.Body);
        Assert.Equal("DEV-LAPTOP", written.Author);
        Assert.Equal(Annotations.Repository, written.RepositoryAlias);
        Assert.Equal(Annotations.Chapter, written.ChapterPath);
        Assert.Equal(Noon, written.UpdatedAt);
    }

    [Fact]
    public void This_devices_own_echo_changes_nothing()
    {
        var store = new InMemoryDevbookAnnotationStore();
        var local = store.Seed(Annotations.Local("Mine.", Noon));
        var merge = new AnnotationReplicaMerge(store);

        var outcome = merge.Apply(
            [Annotations.Record(Annotations.Change(local.Id, "Mine.", Noon), DeviceA, 5)]);

        Assert.Equal(0, outcome.Applied);
        Assert.Empty(store.Applied);
    }

    [Fact]
    public void A_newer_version_from_elsewhere_replaces_the_local_one()
    {
        var store = new InMemoryDevbookAnnotationStore();
        var local = store.Seed(Annotations.Local("Mine.", Noon));
        var merge = new AnnotationReplicaMerge(store);

        merge.Apply(
            [Annotations.Record(Annotations.Change(local.Id, "Theirs, later.", Noon.AddMinutes(5), resolved: true), DeviceB, 5)]);

        var written = Assert.Single(store.Applied);
        Assert.Equal("Theirs, later.", written.Body);
        Assert.True(written.Resolved);
    }

    [Fact]
    public void A_local_edit_still_waiting_to_be_pushed_is_kept()
    {
        var store = new InMemoryDevbookAnnotationStore();
        // Edited at 12:05 on this machine and not yet pushed. The older version
        // arriving from the replica must not overwrite the edit that will win
        // there — and would not whatever the push watermark said.
        var local = store.Seed(Annotations.Local("Edited here, unsent.", Noon.AddMinutes(5)));
        var merge = new AnnotationReplicaMerge(store);

        var outcome = merge.Apply(
            [Annotations.Record(Annotations.Change(local.Id, "Older, from elsewhere.", Noon), DeviceB, 5)]);

        Assert.Equal(0, outcome.Applied);
    }

    [Fact]
    public void An_older_version_never_replaces_a_newer_local_one_even_one_already_pushed()
    {
        var store = new InMemoryDevbookAnnotationStore();
        // Edited at 12:05 and already pushed — the case the old rule got wrong.
        var local = store.Seed(Annotations.Local("Newer, already sent.", Noon.AddMinutes(5)));
        var merge = new AnnotationReplicaMerge(store);

        var outcome = merge.Apply(
            [Annotations.Record(Annotations.Change(local.Id, "Older, from elsewhere.", Noon), DeviceB, 5)]);

        Assert.Equal(0, outcome.Applied);
        Assert.Empty(store.Applied);
    }

    [Fact]
    public void A_tombstone_from_elsewhere_deletes_the_local_remark()
    {
        var store = new InMemoryDevbookAnnotationStore();
        var local = store.Seed(Annotations.Local("Mine.", Noon));
        var merge = new AnnotationReplicaMerge(store);

        merge.Apply(
            [Annotations.Record(Annotations.Change(local.Id, "Mine.", Noon.AddMinutes(1), deletedAt: Noon.AddMinutes(1)), DeviceB, 5)]);

        var written = Assert.Single(store.Applied);
        Assert.Equal(Noon.AddMinutes(1), written.DeletedAt);
        Assert.Empty(store.List(Annotations.Repository, Annotations.Chapter));
    }

    [Fact]
    public void Two_records_for_one_remark_in_a_page_cost_one_write()
    {
        var store = new InMemoryDevbookAnnotationStore();
        var merge = new AnnotationReplicaMerge(store);
        var id = Guid.NewGuid();

        var outcome = merge.Apply(
            [
                Annotations.Record(Annotations.Change(id, "First.", Noon), DeviceB, 1),
                Annotations.Record(Annotations.Change(id, "Second.", Noon.AddMinutes(1)), DeviceB, 2),
            ]);

        Assert.Equal(1, outcome.Applied);
        Assert.Equal("Second.", Assert.Single(store.Applied).Body);
    }

    [Fact]
    public void The_wire_shape_round_trips_the_stored_record()
    {
        var local = Annotations.Local("Round trip.", Noon, resolved: true);

        var back = AnnotationReplicaMerge.ToAnnotation(AnnotationReplicaMerge.ToChange(local));

        Assert.Equal(local, back);
    }

    [Fact]
    public void The_anchor_digest_crosses_the_wire()
    {
        // The whole point of replicating it: the other device holds its own copy
        // of the chapter, and the index alone cannot tell it whether that copy
        // has moved the block. Dropping the digest here would leave every
        // remark anchored by index the moment it left the machine that made it.
        var local = Annotations.Local("Anchored.", Noon) with { BlockHash = "1f2e3d4c" };

        var change = AnnotationReplicaMerge.ToChange(local);

        Assert.Equal("1f2e3d4c", change.Annotation.BlockHash);
        Assert.Equal("1f2e3d4c", AnnotationReplicaMerge.ToAnnotation(change).BlockHash);
    }

    [Fact]
    public void A_payload_from_a_device_that_does_not_send_a_digest_is_anchored_by_index()
    {
        // An older build's push. The member is simply absent, which is null
        // here, and null has always meant "trust the index".
        var change = Annotations.Change(Guid.NewGuid(), "From an older build.", Noon);

        var annotation = AnnotationReplicaMerge.ToAnnotation(change);

        Assert.Null(annotation.BlockHash);
        Assert.False(annotation.IsAnchored);
    }
}
