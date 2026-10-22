using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Roadmap.UI;
using Backlog.UI.Components.Roadmap;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Start plan session on a roadmap item and the waiting chip in its dialog (local ADR
/// 0037 §2): a plan request for the item's tag in its first repository with a clone
/// folder, disabled with the reason when none has one, the chip through the open
/// states with Cancel while Requested, and nothing at all with the switch off.
/// </summary>
public sealed class RoadmapPlanSessionTests : RoadmapBandHarness
{
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

    private static async Task OpenAsync(IRenderedComponent<RoadmapBand> band, Guid id)
    {
        var timeline = band.FindComponent<RoadmapTimeline>();
        await band.InvokeAsync(() => timeline.Instance.OnBarSelected.InvokeAsync(id.ToString()));
        band.WaitForElement("[data-testid='roadmap-editor']");
    }

    [Fact]
    public async Task Start_plan_session_asks_for_the_plan_in_the_first_repository_with_a_clone_folder()
    {
        var (id, tag) = await PlannedAsync("fincent", "backlog");
        RepositorySettings.SetCloneDirectory("backlog", @"D:\Repos\Backlog");

        using var context = Context();
        var band = Band(context);
        await OpenAsync(band, id);

        var start = band.Find("[data-testid='roadmap-editor-start-session']");
        Assert.False(start.HasAttribute("disabled"));
        start.Click();

        band.WaitForAssertion(() =>
        {
            Assert.Equal((tag.TrimStart('+'), "JSdotNet/Backlog", @"D:\Repos\Backlog"), Assert.Single(_queue.Asked));
            Assert.Contains("Waiting for a session", band.Find("[data-testid='roadmap-editor-session-chip']").TextContent, StringComparison.Ordinal);
            Assert.Empty(band.FindAll("[data-testid='roadmap-editor-start-session']"));
        });
    }

    [Fact]
    public async Task With_no_clone_folder_it_is_disabled_and_names_the_folders_missing()
    {
        var (id, _) = await PlannedAsync("backlog", "fincent");

        using var context = Context();
        var band = Band(context);
        await OpenAsync(band, id);

        var start = band.Find("[data-testid='roadmap-editor-start-session']");
        Assert.True(start.HasAttribute("disabled"));
        var reason = "No clone folder is set for JSdotNet/Backlog or JSdotNet/Fincent. Set one in Settings › Repositories to start a session there.";
        Assert.Equal(reason, start.GetAttribute("title"));
        Assert.Equal(reason, band.Find("[data-testid='roadmap-editor-start-session-reason']").TextContent);
    }

    [Fact]
    public async Task Nothing_is_offered_while_the_switch_is_off()
    {
        var (id, tag) = await PlannedAsync("backlog");
        RepositorySettings.SetCloneDirectory("backlog", @"D:\Repos\Backlog");
        _queue.IsOffered = false;
        _queue.Open[tag.TrimStart('+')] = new(Guid.NewGuid(), PlanSessionRequestState.Requested, "JSdotNet/Backlog");

        using var context = Context();
        var band = Band(context);
        await OpenAsync(band, id);

        Assert.Empty(band.FindAll("[data-testid='roadmap-editor-start-session']"));
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
            Assert.Empty(band.FindAll("[data-testid='roadmap-editor-start-session']"));
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
            Assert.NotEmpty(band.FindAll("[data-testid='roadmap-editor-start-session']"));
        });

        // Asked again, picked up, linked: the poll follows it to the end.
        band.Find("[data-testid='roadmap-editor-start-session']").Click();
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

        public Task<IReadOnlyDictionary<string, PlanSessionRequest>> OpenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, PlanSessionRequest>>(new Dictionary<string, PlanSessionRequest>(Open, StringComparer.OrdinalIgnoreCase));

        public Task<PlanSessionRequest> RequestAsync(string planTag, string repository, string cloneFolder, CancellationToken cancellationToken = default)
        {
            Asked.Add((planTag, repository, cloneFolder));
            var request = new PlanSessionRequest(Guid.NewGuid(), PlanSessionRequestState.Requested, repository);
            Open[planTag] = request;
            return Task.FromResult(request);
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
