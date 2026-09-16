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
            [Annotations.Record(Annotations.Change(id, "From the laptop.", Noon), DeviceB, 1)],
            pushWatermark: DateTimeOffset.MinValue);

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
            [Annotations.Record(Annotations.Change(local.Id, "Mine.", Noon), DeviceA, 5)],
            pushWatermark: Noon);

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
            [Annotations.Record(Annotations.Change(local.Id, "Theirs, later.", Noon.AddMinutes(5), resolved: true), DeviceB, 5)],
            pushWatermark: Noon);

        var written = Assert.Single(store.Applied);
        Assert.Equal("Theirs, later.", written.Body);
        Assert.True(written.Resolved);
    }

    [Fact]
    public void A_local_edit_still_waiting_to_be_pushed_is_kept()
    {
        var store = new InMemoryDevbookAnnotationStore();
        // Edited at 12:05 on this machine; the watermark says only what was
        // pushed up to noon has left. The older version arriving from the
        // replica must not overwrite the edit that will win there.
        var local = store.Seed(Annotations.Local("Edited here, unsent.", Noon.AddMinutes(5)));
        var merge = new AnnotationReplicaMerge(store);

        var outcome = merge.Apply(
            [Annotations.Record(Annotations.Change(local.Id, "Older, from elsewhere.", Noon), DeviceB, 5)],
            pushWatermark: Noon);

        Assert.Equal(0, outcome.Applied);
    }

    [Fact]
    public void A_tombstone_from_elsewhere_deletes_the_local_remark()
    {
        var store = new InMemoryDevbookAnnotationStore();
        var local = store.Seed(Annotations.Local("Mine.", Noon));
        var merge = new AnnotationReplicaMerge(store);

        merge.Apply(
            [Annotations.Record(Annotations.Change(local.Id, "Mine.", Noon.AddMinutes(1), deletedAt: Noon.AddMinutes(1)), DeviceB, 5)],
            pushWatermark: Noon);

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
            ],
            pushWatermark: DateTimeOffset.MinValue);

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
}
