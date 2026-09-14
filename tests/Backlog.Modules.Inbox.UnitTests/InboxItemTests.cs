using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.DomainModels;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// The aggregate's invariants, pinned on their own: what never changes, what
/// happens exactly once, which states a decision can be taken from, and which
/// of those decisions the replica needs to hear about.
/// </summary>
public sealed class InboxItemTests
{
    private static readonly DateTimeOffset Later = Items.Noon.AddHours(1);

    [Fact]
    public void The_capture_instant_and_the_source_url_have_no_setter()
    {
        // The invariant is that nothing can change them, and the strongest form
        // of "nothing can" is that the language offers no way to try.
        Assert.Null(typeof(InboxItem).GetProperty(nameof(InboxItem.CapturedAt))!.SetMethod);
        Assert.Null(typeof(InboxItem).GetProperty(nameof(InboxItem.SourceUrl))!.SetMethod);
    }

    [Fact]
    public void A_replica_capture_keeps_the_captures_id()
    {
        var id = Guid.CreateVersion7();

        var item = Items.FromPhone(id: id);

        Assert.Equal(id, item.Id);
        Assert.True(item.ReplicaBacked);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
    }

    [Fact]
    public void Routing_happens_exactly_once()
    {
        var item = Items.Manual();
        var first = Guid.CreateVersion7();

        item.RouteToBacklog([first], ["jsdotnet/backlog"], Later);

        Assert.Equal(InboxStatus.Triaged, item.Status);
        Assert.True(item.IsRouted);
        Assert.Equal([first], item.Routing!.TaskIds);
        Assert.Equal(["jsdotnet/backlog"], item.Routing.RepoIds);
        Assert.Equal(RoutingDomain.Tasks, item.Routing.Domain);
        Assert.Equal(Later, item.Routing.RoutedAt);

        var refused = Assert.Throws<InvalidInboxTransitionException>(
            () => item.RouteToBacklog([Guid.CreateVersion7()], [], Later.AddMinutes(1)));

        Assert.Equal(InboxStatus.Triaged, refused.From);
        Assert.Equal([first], item.Routing.TaskIds);
    }

    [Theory]
    [InlineData(InboxStatus.Unprocessed)]
    [InlineData(InboxStatus.Deferred)]
    public void An_item_that_is_still_open_can_be_archived(InboxStatus from)
    {
        var item = Items.Manual();
        if (from is InboxStatus.Deferred) item.Defer(until: null, Items.Noon);

        item.Archive(Later);

        Assert.Equal(InboxStatus.Archived, item.Status);
        Assert.Null(item.DeferredUntil);
        Assert.Equal(Later, item.UpdatedAt);
    }

    /// <summary>Triaged without a routing is not a state this scope produces, but
    /// it is a state <c>flow.md</c> names and a stored row may hold; archiving
    /// from it is the dismissal the lifecycle allows.</summary>
    [Fact]
    public void A_triaged_item_without_a_routing_can_be_archived()
    {
        var item = Items.Manual();
        item.LoadState(InboxStatus.Triaged, null, routing: null, replicaAckPending: false, Items.Noon);

        item.Archive(Later);

        Assert.Equal(InboxStatus.Archived, item.Status);
    }

    [Fact]
    public void A_routed_item_cannot_be_archived()
    {
        var item = Items.Manual();
        item.RouteToBacklog([Guid.CreateVersion7()], [], Items.Noon);

        Assert.Throws<InvalidInboxTransitionException>(() => item.Archive(Later));

        Assert.Equal(InboxStatus.Triaged, item.Status);
    }

    [Fact]
    public void An_archived_item_cannot_be_routed()
    {
        var item = Items.Manual();
        item.Archive(Items.Noon);

        Assert.Throws<InvalidInboxTransitionException>(
            () => item.RouteToBacklog([Guid.CreateVersion7()], [], Later));

        Assert.False(item.IsRouted);
    }

    [Fact]
    public void A_person_is_refused_as_a_tag()
    {
        var item = Items.Manual();

        Assert.Throws<ArgumentException>(
            () => item.SetTags([new InboxTag("deploy", false), new InboxTag("@bob", false)]));

        Assert.Empty(item.Tags);
    }

    /// <summary>The person check runs on the bare name, after the sigil is
    /// stripped: <c>#@bob</c> is <c>@bob</c> wearing a hash, and letting it
    /// through would store a person as a tag and refuse the row on its next
    /// read.</summary>
    [Theory]
    [InlineData("#@bob")]
    [InlineData(" #@bob ")]
    [InlineData("##@bob")]
    public void A_person_behind_a_hash_is_still_refused_as_a_tag(string name)
    {
        var item = Items.Manual();

        Assert.Throws<ArgumentException>(() => item.SetTags([new InboxTag(name, false)]));

        Assert.Empty(item.Tags);
    }

    /// <summary>Storage's way in: whatever a row holds is loaded as it is, with
    /// no rule applied and no stamp moved — a rule added after the row was
    /// written must not make the row unreadable.</summary>
    [Fact]
    public void Loading_tags_applies_no_rule_and_moves_no_stamp()
    {
        var item = Items.Manual();
        item.LoadState(InboxStatus.Unprocessed, null, routing: null, replicaAckPending: false, Items.Noon);

        item.LoadTags([new InboxTag("@bob", false), new InboxTag("deploy", true)]);

        Assert.Equal(["@bob", "deploy"], item.Tags.Select(tag => tag.Name));
        Assert.Equal(Items.Noon, item.UpdatedAt);
    }

    [Fact]
    public void Tags_are_stored_bare_and_deduplicated_by_name()
    {
        var item = Items.Manual();

        item.SetTags([new InboxTag("#Deploy", false), new InboxTag("deploy", true), new InboxTag(" ", false)]);

        var only = Assert.Single(item.Tags);
        Assert.Equal("Deploy", only.Name);
        Assert.False(only.AutoGenerated);
    }

    /// <summary>A title is one line wherever it is drawn or written — the
    /// entry text the adapter composes reads line two as metadata — so the
    /// aggregate stores it as one, with every whitespace run a single space.</summary>
    [Fact]
    public void A_title_is_stored_as_one_line()
    {
        var item = Items.Manual("Call\nthe   dentist\r\n\ttomorrow ");

        Assert.Equal("Call the dentist tomorrow", item.Title);
    }

    [Fact]
    public void Repositories_are_distinct_without_regard_to_case()
    {
        var item = Items.Manual();

        item.SetRepoIds(["JSdotNet/Backlog", "jsdotnet/backlog", "", "jsdotnet/other"]);

        Assert.Equal(["JSdotNet/Backlog", "jsdotnet/other"], item.RepoIds);
    }

    [Fact]
    public void Filing_is_allowed_in_every_state_including_archived()
    {
        var list = Guid.CreateVersion7();
        var item = Items.Manual();
        item.Archive(Items.Noon);

        item.MoveToList(list);

        Assert.Equal(list, item.ListId);
        Assert.Equal(InboxStatus.Archived, item.Status);
    }

    // --- What the replica needs to hear -------------------------------------

    [Fact]
    public void Archiving_a_replica_capture_leaves_an_acknowledgement_pending()
    {
        var item = Items.FromPhone();

        item.Archive(Later);

        Assert.True(item.ReplicaAckPending);

        item.MarkReplicaAcknowledged();

        Assert.False(item.ReplicaAckPending);
    }

    [Fact]
    public void Routing_a_replica_capture_leaves_an_acknowledgement_pending()
    {
        var item = Items.FromPhone();

        item.RouteToBacklog([Guid.CreateVersion7()], [], Later);

        Assert.True(item.ReplicaAckPending);
    }

    [Fact]
    public void A_manual_capture_never_owes_the_replica_anything()
    {
        var archived = Items.Manual();
        archived.Archive(Later);

        var routed = Items.Manual();
        routed.RouteToBacklog([Guid.CreateVersion7()], [], Later);

        Assert.False(archived.ReplicaAckPending);
        Assert.False(routed.ReplicaAckPending);
    }

    [Fact]
    public void An_unknown_kind_slug_survives_as_its_own_word()
    {
        var item = Items.Manual();

        item.SetKind(InboxEnumMap.ParseKind("hologram"), "hologram");

        Assert.Equal(ContentKind.Text, item.Kind);
        Assert.Equal("hologram", item.KindSlug);
    }
}
