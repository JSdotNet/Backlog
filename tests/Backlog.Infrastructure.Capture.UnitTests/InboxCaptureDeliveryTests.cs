using Backlog.Infrastructure.Capture.Inbox;
using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Ports;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;

namespace Backlog.Infrastructure.Capture.UnitTests;

/// <summary>
/// The one place a capture becomes an inbox capture: the id travels as it is,
/// the kind becomes the channel slug the Inbox badge reads, and the intake's
/// four answers fold into the run's three.
/// </summary>
public sealed class InboxCaptureDeliveryTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 9, 10, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_capture_is_handed_to_the_intake_as_an_inbox_capture()
    {
        var intake = new RecordingIntake();
        var id = Guid.NewGuid();
        var item = new CaptureItem(id, CaptureSourceKind.YouTube, "A video", "https://www.youtube.com/watch?v=abc", "The description.", CapturedAt);

        await new InboxCaptureDelivery(intake).DeliverAsync(item, TestContext.Current.CancellationToken);

        var capture = Assert.Single(intake.Received);
        Assert.Equal(id, capture.Id);
        Assert.Equal("A video", capture.Title);
        Assert.Equal("youtube", capture.Channel);
        Assert.Equal(CapturedAt, capture.CapturedAt);
        Assert.Equal(CapturedAt, capture.UpdatedAt);
        Assert.Null(capture.WithdrawnAt);
        Assert.Equal("https://www.youtube.com/watch?v=abc", capture.SourceUrl);
        Assert.Equal("The description.", capture.BodyMd);

        // The replica has never seen this id; routing or archiving the item
        // must not push it a tombstone for a document that was never there.
        Assert.False(capture.ReplicaBacked);
    }

    [Theory]
    [InlineData(InboxIntakeOutcome.Received, CaptureDeliveryOutcome.Delivered)]
    [InlineData(InboxIntakeOutcome.AlreadyKnown, CaptureDeliveryOutcome.AlreadyKnown)]
    [InlineData(InboxIntakeOutcome.Withdrawn, CaptureDeliveryOutcome.AlreadyKnown)]
    [InlineData(InboxIntakeOutcome.Ignored, CaptureDeliveryOutcome.Ignored)]
    public async Task The_intakes_answer_becomes_the_runs(InboxIntakeOutcome intake, CaptureDeliveryOutcome expected)
    {
        var delivery = new InboxCaptureDelivery(new RecordingIntake { Answer = intake });
        var item = new CaptureItem(Guid.NewGuid(), CaptureSourceKind.Website, "A post", null, null, CapturedAt);

        Assert.Equal(expected, await delivery.DeliverAsync(item, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_imports_facts_travel_to_the_intake_and_its_list_is_filed_by_id()
    {
        var intake = new RecordingIntake();
        var errands = new InboxListDto(Guid.NewGuid(), "Errands", GroupId: null, Order: 0);
        var lists = new FixedLists(errands);
        var delivery = new InboxCaptureDelivery(intake, lists);
        var item = new CaptureItem(
            Guid.NewGuid(), CaptureSourceKind.Import, "Buy oat milk", null, null, CapturedAt,
            new CaptureFacts("text", ["home"], "maria", " errands "));

        var outcome = await delivery.DeliverAsync(item, TestContext.Current.CancellationToken);
        await delivery.DeliverAsync(item with { Id = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(CaptureDeliveryOutcome.Delivered, outcome);

        var capture = intake.Received[0];
        Assert.Equal("import", capture.Channel);
        Assert.Equal("text", capture.Kind);
        Assert.Equal(["home"], capture.Tags);
        Assert.Equal("maria", capture.Person);
        Assert.Equal(errands.Id, capture.ListId);

        // The lists are asked for once per run, not once per item.
        Assert.Equal(1, lists.Asked);
    }

    /// <summary>An import never creates a list (local ADR 0017): the item
    /// lands unfiled, and the answer says so for the run's line.</summary>
    [Fact]
    public async Task A_list_that_does_not_exist_lands_the_item_unfiled_and_says_so()
    {
        var intake = new RecordingIntake();
        var delivery = new InboxCaptureDelivery(intake, new FixedLists(new InboxListDto(Guid.NewGuid(), "Errands", null, 0)));
        var item = new CaptureItem(
            Guid.NewGuid(), CaptureSourceKind.Import, "Read later", null, null, CapturedAt,
            new CaptureFacts(null, [], null, "Someday/Maybe"));

        var outcome = await delivery.DeliverAsync(item, TestContext.Current.CancellationToken);

        Assert.Equal(CaptureDeliveryOutcome.DeliveredUnfiled, outcome);
        Assert.Null(Assert.Single(intake.Received).ListId);
    }

    [Fact]
    public async Task A_feed_capture_carries_no_facts()
    {
        var intake = new RecordingIntake();
        var item = new CaptureItem(Guid.NewGuid(), CaptureSourceKind.Website, "A post", null, null, CapturedAt);

        await new InboxCaptureDelivery(intake).DeliverAsync(item, TestContext.Current.CancellationToken);

        var capture = Assert.Single(intake.Received);
        Assert.Null(capture.Kind);
        Assert.Null(capture.ListId);
        Assert.Null(capture.Tags);
        Assert.Null(capture.Person);
    }

    private sealed class FixedLists(params InboxListDto[] lists) : IInboxListLookup
    {
        public int Asked { get; private set; }

        public Task<IReadOnlyList<InboxListDto>> ListsAsync(CancellationToken cancellationToken = default)
        {
            Asked++;
            return Task.FromResult<IReadOnlyList<InboxListDto>>(lists);
        }
    }

    private sealed class RecordingIntake : IInboxIntake
    {
        public List<InboxCaptureDto> Received { get; } = [];

        public InboxIntakeOutcome Answer { get; init; } = InboxIntakeOutcome.Received;

        public Task<InboxIntakeOutcome> ReceiveAsync(InboxCaptureDto capture, CancellationToken cancellationToken = default)
        {
            Received.Add(capture);
            return Task.FromResult(Answer);
        }
    }
}
