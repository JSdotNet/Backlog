using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Roadmap.UI;
using Backlog.UI.Components.Roadmap;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The plan's queue icon on a roadmap item and the waiting chip in its dialog (local ADR
/// 0037 §2): beside a copy of the stored tag, a plan request for the item's tag in its
/// first repository with a clone folder, disabled with the reason when none has one,
/// the chip through the open states with Cancel while Requested, and no icon or chip
/// with the switch off — the copy stays.
/// </summary>
public sealed class RoadmapPlanSessionTests : RoadmapBandHarness
{
    private const string Queue = "[data-testid='roadmap-editor-queue-session']";

    private readonly FakePlanSessionRequests _queue = new();
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);

    private IRenderedComponent<RoadmapBand> Band(BunitContext context)
    {
        context.Services.AddSingleton<IPlanSessionRequests>(_queue);
        context.Services.AddSingleton<TimeProvider>(_clock);

        var band = context.Render<RoadmapBand>();
        band.WaitForElement("[data-testid='roadmap-timeline']");
        return band;
    }

    private async Task<(Guid Id, string Tag)> PlannedAsync(params string[] repositories)
    {
        Configure("JSdotNet/Backlog", "JSdotNet/Fincent");

        var added = await Planning.AddItemAsync(
            "Session dispatch",
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 9),
            repositoryAliases: repositories,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(added.IsSuccess);

        return (added.Value.Id, added.Value.Tag);
    }

    /// <summary>The tag with its plan sigil, <c>+&lt;slug&gt;</c>: what the plan row copies and names.</summary>
    private static string Sigiled(string tag) => $"+{tag.Trim().TrimStart('+', '#')}";

    private static async Task OpenAsync(IRenderedComponent<RoadmapBand> band, Guid id)
    {
        var timeline = band.FindComponent<RoadmapTimeline>();
        await band.InvokeAsync(() => timeline.Instance.OnBarSelected.InvokeAsync(id.ToString()));
        band.WaitForElement("[data-testid='roadmap-editor']");
    }

    [Fact]
    public async Task The_queue_icon_sits_beside_the_plan_copy_and_names_the_plan_and_repository()
    {
        var (id, tag) = await PlannedAsync("fincent", "backlog");
        RepositorySettings.SetCloneDirectory("backlog", @"D:\Repos\Backlog");

        using var context = Context();
        var band = Band(context);
        await OpenAsync(band, id);

        var plan = band.Find("[data-testid='roadmap-editor-plan']");
        var copy = plan.QuerySelector("[data-testid='roadmap-editor-copy-plan']");
        var queue = plan.QuerySelector(Queue);
        Assert.NotNull(copy);
        Assert.NotNull(queue);
        Assert.Same(copy, queue.ParentElement!.PreviousElementSibling);
        Assert.NotNull(queue.QuerySelector("svg.list-end-icon"));

        var label = $"Queue a session for the {Sigiled(tag)} plan in JSdotNet/Backlog";
        Assert.Equal(label, queue.GetAttribute("aria-label"));
        Assert.Equal(label, queue.GetAttribute("title"));
        Assert.False(queue.HasAttribute("disabled"));
        Assert.False(queue.HasAttribute("aria-describedby"));
        Assert.Empty(band.FindAll("[data-testid='roadmap-editor-start-session']"));
    }

    [Fact]
    public async Task Queuing_asks_for_the_plan_in_the_first_repository_with_a_clone_folder()
    {
        var (id, tag) = await PlannedAsync("fincent", "backlog");
        RepositorySettings.SetCloneDirectory("backlog", @"D:\Repos\Backlog");

        using var context = Context();
        var band = Band(context);
        await OpenAsync(band, id);

        band.Find(Queue).Click();

        band.WaitForAssertion(() =>
        {
            Assert.Equal((tag.TrimStart('+'), "JSdotNet/Backlog", @"D:\Repos\Backlog"), Assert.Single(_queue.Asked));
            Assert.Contains("Waiting for a session", band.Find("[data-testid='roadmap-editor-session-chip']").TextContent, StringComparison.Ordinal);
            Assert.Empty(band.FindAll(Queue));
            Assert.NotEmpty(band.FindAll("[data-testid='roadmap-editor-copy-plan']"));
        });
    }

    [Fact]
    public async Task The_copy_copies_the_tag_with_its_plan_sigil()
    {
        var (id, tag) = await PlannedAsync("backlog");

        using var context = Context();
        var band = Band(context);
        await OpenAsync(band, id);

        var button = band.Find("[data-testid='roadmap-editor-copy-plan-button']");
        Assert.Equal($"Copy {Sigiled(tag)}", button.GetAttribute("aria-label"));
        button.Click();

        band.WaitForAssertion(() =>
            Assert.Equal(Sigiled(tag), Assert.Single(context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]));
    }

    [Fact]
    public async Task While_the_press_is_on_its_way_the_icon_stays_focusable_and_says_it_is_busy()
    {
        var (id, _) = await PlannedAsync("backlog");
        RepositorySettings.SetCloneDirectory("backlog", @"D:\Repos\Backlog");
        _queue.Gate = new TaskCompletionSource();

        using var context = Context();
        var band = Band(context);
        await OpenAsync(band, id);

        band.Find(Queue).Click();

        band.WaitForAssertion(() =>
        {
            var queue = band.Find(Queue);
            Assert.Equal("true", queue.GetAttribute("aria-busy"));
            Assert.Equal("true", queue.GetAttribute("aria-disabled"));
            Assert.False(queue.HasAttribute("disabled"));
        });

        band.Find(Queue).Click();
        _queue.Gate.SetResult();

        band.WaitForAssertion(() =>
        {
            Assert.Single(_queue.Asked);
            Assert.Empty(band.FindAll(Queue));
        });
    }

    [Fact]
    public async Task With_no_clone_folder_it_is_disabled_and_names_the_folders_missing()
    {
        var (id, tag) = await PlannedAsync("backlog", "fincent");

        using var context = Context();
        var band = Band(context);
        await OpenAsync(band, id);

        var queue = band.Find(Queue);
        Assert.True(queue.HasAttribute("disabled"));
        Assert.Equal($"Queue a session for the {Sigiled(tag)} plan", queue.GetAttribute("aria-label"));

        var reason = "No clone folder is set for JSdotNet/Backlog or JSdotNet/Fincent. Set one in Settings › Repositories to start a session there.";
        Assert.Equal(reason, queue.GetAttribute("title"));
        Assert.Equal(reason, band.Find("[data-testid='roadmap-editor-queue-session-wrapper']").GetAttribute("title"));

        var described = band.Find("[data-testid='roadmap-editor-queue-session-reason']");
        Assert.Equal(reason, described.TextContent);
        Assert.Contains("sr-only", described.ClassList);
        Assert.Equal(described.Id, queue.GetAttribute("aria-describedby"));
    }

    [Fact]
    public async Task With_the_switch_off_only_the_copy_is_offered()
    {
        var (id, tag) = await PlannedAsync("backlog");
        RepositorySettings.SetCloneDirectory("backlog", @"D:\Repos\Backlog");
        _queue.IsOffered = false;
        _queue.Open[tag.TrimStart('+')] = new(Guid.NewGuid(), PlanSessionRequestState.Requested, "JSdotNet/Backlog");

        using var context = Context();
        var band = Band(context);
        await OpenAsync(band, id);

        Assert.NotEmpty(band.FindAll("[data-testid='roadmap-editor-copy-plan']"));
        Assert.Empty(band.FindAll(Queue));
        Assert.Empty(band.FindAll("[data-testid='roadmap-editor-session-request']"));
    }

    [Theory]
    [InlineData(PlanSessionRequestState.Requested, true)]
    [InlineData(PlanSessionRequestState.Claimed, false)]
    [InlineData(PlanSessionRequestState.Started, false)]
    public async Task The_chip_shows_while_the_request_is_open_and_Cancel_only_while_Requested(PlanSessionRequestState state, bool cancellable)
    {
        var (id, tag) = await PlannedAsync("backlog");
        RepositorySettings.SetCloneDirectory("backlog", @"D:\Repos\Backlog");
        _queue.Open[tag.TrimStart('+')] = new(Guid.NewGuid(), state, "JSdotNet/Backlog");

        using var context = Context();
        var band = Band(context);
        await OpenAsync(band, id);

        band.WaitForAssertion(() =>
        {
            Assert.Contains("Waiting for a session", band.Find("[data-testid='roadmap-editor-session-chip']").TextContent, StringComparison.Ordinal);
            Assert.Equal(cancellable, band.FindAll("[data-testid='roadmap-editor-session-cancel']").Count == 1);
            Assert.Empty(band.FindAll(Queue));
            Assert.Empty(band.FindAll("[data-testid='roadmap-editor-queue-session-reason']"));
        });
    }

    [Fact]
    public async Task Cancel_withdraws_the_request_and_the_chip_goes_when_the_session_links()
    {
        var (id, tag) = await PlannedAsync("backlog");
        RepositorySettings.SetCloneDirectory("backlog", @"D:\Repos\Backlog");
        var requestId = Guid.NewGuid();
        _queue.Open[tag.TrimStart('+')] = new(requestId, PlanSessionRequestState.Requested, "JSdotNet/Backlog");

        using var context = Context();
        var band = Band(context);
        await OpenAsync(band, id);

        band.WaitForElement("[data-testid='roadmap-editor-session-cancel']").Click();

        band.WaitForAssertion(() =>
        {
            Assert.Equal([requestId], _queue.Cancelled);
            Assert.Empty(band.FindAll("[data-testid='roadmap-editor-session-request']"));
            Assert.NotEmpty(band.FindAll(Queue));
        });

        // Queued again, picked up, linked: the poll follows it to the end.
        band.Find(Queue).Click();
        band.WaitForElement("[data-testid='roadmap-editor-session-chip']");
        _queue.Open.Clear();
        _clock.Advance(RoadmapSessionRequests.PollInterval);

        band.WaitForAssertion(() => Assert.Empty(band.FindAll("[data-testid='roadmap-editor-session-request']")));
    }

    [Fact]
    public void An_absent_item_offers_nothing()
    {
        Assert.Null(PlanSessionOffer.For(null, _ => null));
    }

    [Fact]
    public void An_item_naming_no_repository_says_so()
    {
        var item = new RoadmapItemDto(Guid.NewGuid(), "Plan", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 9), PlanningPriority.Medium, [], null, null, [], Tag: "+plan");

        var offer = PlanSessionOffer.For(item, _ => null);

        Assert.Equal("plan", offer!.Tag);
        Assert.Equal(PlanSessionOffer.NoRepository, offer.DisabledReason);
    }

    /// <summary>The plan queue, in memory.</summary>
    private sealed class FakePlanSessionRequests : IPlanSessionRequests
    {
        public bool IsOffered { get; set; } = true;

        public Dictionary<string, PlanSessionRequest> Open { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Tag, string Repository, string CloneFolder)> Asked { get; } = [];

        public List<Guid> Cancelled { get; } = [];

        /// <summary>When set, a request waits on it, holding the press on its way.</summary>
        public TaskCompletionSource? Gate { get; set; }

        public Task<IReadOnlyDictionary<string, PlanSessionRequest>> OpenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, PlanSessionRequest>>(new Dictionary<string, PlanSessionRequest>(Open, StringComparer.OrdinalIgnoreCase));

        public async Task<PlanSessionRequest> RequestAsync(string planTag, string repository, string cloneFolder, CancellationToken cancellationToken = default)
        {
            Asked.Add((planTag, repository, cloneFolder));
            if (Gate is { } gate) await gate.Task;

            var request = new PlanSessionRequest(Guid.NewGuid(), PlanSessionRequestState.Requested, repository);
            Open[planTag] = request;
            return request;
        }

        public Task<bool> CancelAsync(Guid requestId, CancellationToken cancellationToken = default)
        {
            var held = Open.FirstOrDefault(pair => pair.Value.Id == requestId);
            if (held.Value is not { State: PlanSessionRequestState.Requested }) return Task.FromResult(false);

            Cancelled.Add(requestId);
            Open.Remove(held.Key);
            return Task.FromResult(true);
        }
    }
}
