using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.CreatePlan;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// Create plan's <b>Also start a project note</b>
/// (<c>.devbook/domain/inbox/features.md#create-plan-from-an-item</c>): off, the
/// Brain is never asked; on, a note goes into the Brain's projects area titled as
/// the plan, carrying the item's notes and link and the plan's tag, and the plan
/// gains a <c>plan</c> entry whose Knowledge Ref is that note. A Brain that cannot
/// take a note refuses before anything is drafted.
/// </summary>
public sealed class CreatePlanProjectNoteTests
{
    private static readonly DateTimeOffset Decision = Items.Noon.AddHours(1);

    [Fact]
    public async Task Off_by_default_the_brain_is_not_asked_and_the_plan_goes_as_drafted()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var drafter = new FakePlanDrafter();
        var brain = new RecordingBrain();
        var item = Items.Manual("Ship the thing");
        store.Seed(item);

        var result = await new CreatePlanCommandHandler(store, target, new FakeTimeProvider(Decision), drafter, brain: brain)
            .Handle(new CreatePlanCommand(item.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ProjectNotePath);
        Assert.Empty(brain.Requests);
        Assert.Equal(drafter.Answer, target.Imports.Single().Plan);
    }

    [Fact]
    public async Task On_the_note_goes_into_projects_titled_as_the_plan_with_the_items_notes_link_and_tag()
    {
        var store = new InMemoryInboxStore();
        var drafter = new FakePlanDrafter();
        var brain = new RecordingBrain();
        var item = Linked("Ship the thing", "https://example.com/launch");
        store.Seed(item);

        var result = await Create(store, new FakeBacklogTarget(), drafter, brain, item.Id);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(brain.Requests);
        Assert.True(request.IntoProjects);
        Assert.Null(request.Folder);
        Assert.Equal("Ship the thing", request.Title);
        Assert.Equal(item.BodyMd, request.BodyMd);
        Assert.Equal("https://example.com/launch", request.SourceUrl);
        Assert.Equal(drafter.Requests.Single().PlanTag, request.RoadmapTag);
        Assert.Equal("projects/ship-the-thing.md", result.Value.ProjectNotePath);
    }

    [Fact]
    public async Task On_the_plan_gains_a_plan_entry_whose_knowledge_ref_is_the_note()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var drafter = new FakePlanDrafter { Answer = "# Step one\n`prompt` `!draft` `+{tag}`\n\nDo it.\n" };
        var item = Items.Manual("Ship the thing");
        item.SetRepoIds(["JSdotNet/Backlog"]);
        store.Seed(item);

        var result = await Create(store, target, drafter, new RecordingBrain(), item.Id);

        Assert.True(result.IsSuccess);
        var tag = drafter.Requests.Single().PlanTag;
        Assert.Equal(
            $"# Ship the thing\n`plan` `+{tag}` `repo:JSdotNet/Backlog` `devbook:#brain:projects/ship-the-thing.md`\n\n"
            + $"# Step one\n`prompt` `!draft` `+{tag}`\n\nDo it.\n",
            target.Imports.Single().Plan);
        Assert.True(item.IsRouted);
    }

    [Fact]
    public async Task A_brain_that_cannot_take_a_note_refuses_in_its_own_words_before_the_drafter_is_asked()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var drafter = new FakePlanDrafter();
        var brain = new RecordingBrain { Unavailable = "Choose a Brain folder in Settings." };
        var item = Items.Manual();
        store.Seed(item);

        var result = await Create(store, target, drafter, brain, item.Id);

        Assert.Equal(InboxErrors.BrainUnavailableCode, result.Error.Code);
        Assert.Equal("Choose a Brain folder in Settings.", result.Error.Message);
        Assert.Empty(drafter.Requests);
        Assert.Empty(brain.Requests);
        Assert.Empty(target.Imports);
        Assert.False(item.IsRouted);
    }

    [Fact]
    public async Task A_host_without_a_brain_refuses_the_note()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        store.Seed(item);

        var result = await Create(store, new FakeBacklogTarget(), new FakePlanDrafter(), brain: null, item.Id);

        Assert.Equal(InboxErrors.BrainUnavailableCode, result.Error.Code);
        Assert.Equal(InboxErrors.NoBrainHere, result.Error.Message);
    }

    [Fact]
    public async Task A_draft_that_is_not_a_plan_writes_no_note()
    {
        var store = new InMemoryInboxStore();
        var brain = new RecordingBrain();
        var item = Items.Manual();
        store.Seed(item);

        var result = await Create(store, new FakeBacklogTarget(), new FakePlanDrafter { Answer = "Three steps, roughly." }, brain, item.Id);

        Assert.Equal("inbox.plan.empty", result.Error.Code);
        Assert.Empty(brain.Requests);
    }

    [Fact]
    public async Task A_note_the_brain_refuses_leaves_tasks_unasked()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var brain = new RecordingBrain { RefuseWith = "This Brain has no projects area to start a project note in." };
        var item = Items.Manual();
        store.Seed(item);

        var result = await Create(store, target, new FakePlanDrafter(), brain, item.Id);

        Assert.Equal(InboxErrors.BrainUnavailableCode, result.Error.Code);
        Assert.Empty(target.Imports);
        Assert.False(item.IsRouted);
    }

    private static InboxItem Linked(string title, string url) =>
        InboxItem.Capture(
            title,
            new InboxSource(InboxEnumMap.ManualChannel, Person: null),
            sourceUrl: url,
            ContentKind.Text,
            capturedAt: Items.Noon,
            receivedAt: Items.Noon,
            replicaBacked: false);

    private static Task<Result<InboxRoutedDto>> Create(
        InMemoryInboxStore store,
        FakeBacklogTarget target,
        IInboxPlanDrafter drafter,
        IInboxBrainTarget? brain,
        Guid id) =>
        new CreatePlanCommandHandler(store, target, new FakeTimeProvider(Decision), drafter, brain: brain)
            .Handle(new CreatePlanCommand(id, StartProjectNote: true), TestContext.Current.CancellationToken);

    private sealed class RecordingBrain : IInboxBrainTarget
    {
        public List<InboxBrainNoteRequestDto> Requests { get; } = [];

        public string? Unavailable { get; init; }

        public string? RefuseWith { get; init; }

        public string? UnavailableReason => Unavailable;

        public Task<Result<InboxBrainNoteWrittenDto>> WriteNoteAsync(InboxBrainNoteRequestDto request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return Task.FromResult(RefuseWith is { } refusal
                ? Result.Failure<InboxBrainNoteWrittenDto>(InboxErrors.BrainUnavailable(refusal))
                : Result.Success(new InboxBrainNoteWrittenDto(
                    "projects/" + string.Join('-', request.Title.ToLowerInvariant().Split(' ')) + ".md",
                    "Projects")));
        }
    }
}
