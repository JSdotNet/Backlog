using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The pressed plan filter's queue icon (local ADR 0038 §2;
/// <c>.devbook/domain/sessions/features.md#start-a-session-from-an-entry</c>): beside the
/// plan chip's copy only while a plan is pressed and the <c>session-requests</c> switch is
/// on, one session for the whole plan in the first of its roadmap item's repositories with
/// a clone folder, disabled with the reason otherwise, and the waiting chip — with Cancel
/// while Requested — in its place while a request is open.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TasksPlanSessionRequestTests
{
    private const string Backlog = "JSdotNet/Backlog";
    private const string Tools = "JSdotNet/Tools";
    private const string BacklogClone = @"D:\Repos\Backlog";

    private static async Task<(TasksPaneHost Host, FakePlanTagSessionRequests Queue)> HostAsync(
        bool cloneBacklog = true,
        bool pressPlan = true,
        params string[] planRepositories)
    {
        var host = await TasksPaneHost.CreateAsync(Backlog, Tools);
        if (cloneBacklog) host.GitHub.Settings.SetCloneDirectory("backlog", BacklogClone);

        await host.WriteEntryAsync("# Start it +alpha\n`task` `!ready` `@platform`\n");
        await host.WriteEntryAsync("# Tidy it +zeta\n`task` `!ready` `@platform`\n");
        await host.State.SelectAsync(null);
        if (pressPlan) host.State.ShowPlan("+alpha");

        var queue = new FakePlanTagSessionRequests();
        queue.Repositories["alpha"] = planRepositories.Length == 0 ? ["backlog"] : planRepositories;
        host.Context.Services.AddSingleton<IPlanTagSessionRequests>(queue);
        host.Context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow));

        return (host, queue);
    }

    [Fact]
    public async Task The_pressed_plan_carries_the_icon_just_after_its_copy_and_queues_the_plan_there()
    {
        var (host, queue) = await HostAsync();
        using var _host = host;

        var pane = host.Render();
        var icon = pane.WaitForElement("[data-testid='tag-filter-queue-plan']");

        var wrapper = icon.ParentElement!;
        Assert.Equal("tag-filter-queue-plan-wrapper", wrapper.GetAttribute("data-testid"));
        Assert.False(wrapper.HasAttribute("title"));
        Assert.Equal("tag-filter-plan-pair", wrapper.ParentElement?.GetAttribute("data-testid"));
        Assert.Equal("tag-filter-copy-plan", wrapper.PreviousElementSibling?.GetAttribute("data-testid"));
        Assert.Equal($"Queue a session for the +alpha plan in {Backlog}", icon.GetAttribute("aria-label"));
        Assert.Equal($"Queue a session for the +alpha plan in {Backlog}", icon.GetAttribute("title"));
        Assert.False(icon.HasAttribute("disabled"));
        Assert.False(icon.HasAttribute("aria-describedby"));
        Assert.NotNull(icon.QuerySelector("svg.list-end-icon"));

        icon.Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(("alpha", Backlog, BacklogClone), Assert.Single(queue.Asked));
            Assert.Empty(pane.FindAll("[data-testid='tag-filter-queue-plan']"));
            var chip = pane.Find("[data-testid='tag-filter-plan-pair'] [data-testid='tag-filter-plan-session-chip']");
            Assert.Contains("Waiting for a session", chip.TextContent, StringComparison.Ordinal);
            Assert.NotNull(pane.Find("[data-testid='tag-filter-copy-plan']"));
        });
        Assert.Contains(host.Toasts.Visible, toast => toast.TestId == "plan-session-request-queued");
    }

    [Fact]
    public async Task With_no_plan_pressed_there_is_no_icon()
    {
        var (host, _) = await HostAsync(pressPlan: false);
        using var _host = host;

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[data-testid='tag-filter-plan-pair']"));
        Assert.Empty(pane.FindAll("[data-testid='tag-filter-queue-plan']"));
    }

    [Fact]
    public async Task With_the_switch_off_the_pair_is_the_chip_and_its_copy_alone()
    {
        var (host, queue) = await HostAsync();
        using var _host = host;
        queue.IsOffered = false;
        queue.Open["alpha"] = new EntrySessionRequest(Guid.NewGuid(), EntrySessionRequestState.Requested, Backlog);

        var pane = host.Render();

        Assert.NotNull(pane.Find("[data-testid='tag-filter-plan-pair'] [data-testid='tag-filter-copy-plan']"));
        Assert.Empty(pane.FindAll("[data-testid='tag-filter-queue-plan']"));
        Assert.Empty(pane.FindAll("[data-testid='tag-filter-plan-session-request']"));
    }

    [Fact]
    public async Task A_plan_no_roadmap_item_carries_offers_no_icon()
    {
        var (host, queue) = await HostAsync();
        using var _host = host;
        queue.Repositories.Clear();

        var pane = host.Render();

        Assert.NotNull(pane.Find("[data-testid='tag-filter-copy-plan']"));
        Assert.Empty(pane.FindAll("[data-testid='tag-filter-queue-plan']"));
    }

    [Fact]
    public async Task The_session_starts_in_the_first_of_the_plan_s_repositories_with_a_clone_folder()
    {
        var (host, queue) = await HostAsync(cloneBacklog: true, pressPlan: true, "tools", "backlog");
        using var _host = host;

        var pane = host.Render();
        var icon = pane.WaitForElement("[data-testid='tag-filter-queue-plan']");
        Assert.Equal($"Queue a session for the +alpha plan in {Backlog}", icon.GetAttribute("aria-label"));

        icon.Click();

        pane.WaitForAssertion(() => Assert.Equal(("alpha", Backlog, BacklogClone), Assert.Single(queue.Asked)));
    }

    [Fact]
    public async Task Without_a_clone_folder_the_icon_is_disabled_and_says_why()
    {
        var (host, queue) = await HostAsync(cloneBacklog: false);
        using var _host = host;

        var pane = host.Render();
        var icon = pane.WaitForElement("[data-testid='tag-filter-queue-plan']");
        var reason = pane.Find("[data-testid='tag-filter-queue-plan-reason']");
        const string expected = $"No clone folder is set for {Backlog}. Set one in Settings › Repositories to start a session there.";

        Assert.True(icon.HasAttribute("disabled"));
        Assert.Equal("Queue a session for the +alpha plan", icon.GetAttribute("aria-label"));
        Assert.Equal(expected, icon.GetAttribute("title"));
        Assert.Equal(expected, pane.Find("[data-testid='tag-filter-queue-plan-wrapper']").GetAttribute("title"));
        Assert.Equal(expected, reason.TextContent);
        Assert.Contains("sr-only", reason.ClassList);
        Assert.Equal(reason.Id, icon.GetAttribute("aria-describedby"));
        Assert.Empty(queue.Asked);
    }

    [Fact]
    public async Task A_plan_filed_under_no_repository_is_disabled_with_the_roadmap_s_reason()
    {
        var (host, queue) = await HostAsync();
        using var _host = host;
        queue.Repositories["alpha"] = [];

        var pane = host.Render();
        var icon = pane.WaitForElement("[data-testid='tag-filter-queue-plan']");

        Assert.True(icon.HasAttribute("disabled"));
        Assert.Equal(PlanQueueOffer.NoRepository, icon.GetAttribute("title"));
        Assert.Equal(PlanQueueOffer.NoRepository, pane.Find("[data-testid='tag-filter-queue-plan-reason']").TextContent);
        Assert.Equal(
            "This plan names no repository, so there is no clone folder to start a session in.",
            PlanQueueOffer.NoRepository);
    }

    [Theory]
    [InlineData(EntrySessionRequestState.Requested, true)]
    [InlineData(EntrySessionRequestState.Claimed, false)]
    [InlineData(EntrySessionRequestState.Started, false)]
    public async Task While_a_request_is_open_the_waiting_chip_stands_in_the_icon_s_place(EntrySessionRequestState state, bool cancellable)
    {
        var (host, queue) = await HostAsync();
        using var _host = host;
        queue.Open["alpha"] = new EntrySessionRequest(Guid.NewGuid(), state, Backlog);

        var pane = host.Render();

        pane.WaitForAssertion(() =>
        {
            var wait = pane.Find("[data-testid='tag-filter-plan-pair'] [data-testid='tag-filter-plan-session-request']");
            Assert.Contains(Backlog, wait.QuerySelector("[data-testid='tag-filter-plan-session-chip']")!.GetAttribute("title"), StringComparison.Ordinal);
            Assert.Equal(cancellable, wait.QuerySelector("[data-testid='tag-filter-plan-session-cancel']") is not null);
            Assert.Empty(pane.FindAll("[data-testid='tag-filter-queue-plan']"));
            Assert.Empty(pane.FindAll("[data-testid='tag-filter-queue-plan-reason']"));
        });
    }

    [Fact]
    public async Task Cancel_brings_the_icon_back()
    {
        var (host, queue) = await HostAsync();
        using var _host = host;
        var id = Guid.NewGuid();
        queue.Open["alpha"] = new EntrySessionRequest(id, EntrySessionRequestState.Requested, Backlog);

        var pane = host.Render();
        pane.WaitForElement("[data-testid='tag-filter-plan-session-cancel']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(id, Assert.Single(queue.Cancelled));
            Assert.Empty(pane.FindAll("[data-testid='tag-filter-plan-session-request']"));
            Assert.NotNull(pane.Find("[data-testid='tag-filter-queue-plan']"));
        });
    }

    [Fact]
    public async Task While_the_press_is_on_its_way_the_icon_stays_focusable_and_a_second_press_queues_nothing()
    {
        var (host, queue) = await HostAsync();
        using var _host = host;
        queue.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var pane = host.Render();
        pane.WaitForElement("[data-testid='tag-filter-queue-plan']").Click();

        pane.WaitForAssertion(() =>
        {
            var icon = pane.Find("[data-testid='tag-filter-queue-plan']");
            Assert.Equal("true", icon.GetAttribute("aria-busy"));
            Assert.Equal("true", icon.GetAttribute("aria-disabled"));
            Assert.False(icon.HasAttribute("disabled"));
        });

        pane.Find("[data-testid='tag-filter-queue-plan']").Click();
        queue.Gate.SetResult();

        pane.WaitForAssertion(() =>
        {
            Assert.Single(queue.Asked);
            Assert.Empty(pane.FindAll("[data-testid='tag-filter-queue-plan']"));
            Assert.NotNull(pane.Find("[data-testid='tag-filter-plan-session-chip']"));
        });
    }

    [Fact]
    public async Task A_roadmap_edit_made_while_the_switch_was_off_is_read_when_it_turns_on()
    {
        var (host, queue) = await HostAsync();
        using var _host = host;
        host.GitHub.Settings.SetCloneDirectory("tools", @"D:\Repos\Tools");

        var pane = host.Render();
        pane.WaitForAssertion(() => Assert.Equal(
            $"Queue a session for the +alpha plan in {Backlog}",
            pane.Find("[data-testid='tag-filter-queue-plan']").GetAttribute("aria-label")));

        // Switch off, the roadmap item moves to Tools, switch on.
        queue.IsOffered = false;
        queue.Repositories["alpha"] = ["tools"];
        queue.RaisePlanChanged();
        queue.IsOffered = true;
        await pane.InvokeAsync(() => host.Features.SetEnabled(SessionFeatures.SessionRequests, true));

        pane.WaitForAssertion(() => Assert.Equal(
            $"Queue a session for the +alpha plan in {Tools}",
            pane.Find("[data-testid='tag-filter-queue-plan']").GetAttribute("aria-label")));
    }

    [Fact]
    public async Task The_request_names_the_plan_as_its_roadmap_item_spells_it()
    {
        var (host, queue) = await HostAsync();
        using var _host = host;
        queue.Repositories.Clear();
        queue.Repositories["Alpha"] = ["backlog"];

        var pane = host.Render();
        pane.WaitForElement("[data-testid='tag-filter-queue-plan']").Click();

        pane.WaitForAssertion(() => Assert.Equal(("Alpha", Backlog, BacklogClone), Assert.Single(queue.Asked)));
    }

    [Fact]
    public async Task A_poll_that_set_out_before_the_press_landed_does_not_bring_the_icon_back()
    {
        var queue = new FakePlanTagSessionRequests();
        queue.Repositories["alpha"] = ["backlog"];
        using var requests = new TasksPlanSessionRequests(queue, new FakeTimeProvider(DateTimeOffset.UtcNow));
        await requests.RefreshAsync(withRepositories: true);
        var offer = PlanQueueOffer.For("alpha", [new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog") { CloneDirectory = BacklogClone }])!;

        // The press is on its way; a poll reads the queue before the store writes …
        queue.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var press = requests.RequestAsync(offer);
        var pollGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.OpenGate = pollGate;
        var poll = requests.RefreshAsync();

        // … and lands after the press has.
        queue.Gate.SetResult();
        Assert.True(await press);
        pollGate.SetResult();
        await poll;

        Assert.NotNull(requests.OpenFor("alpha"));
    }

    [Fact]
    public async Task Of_two_overlapping_plan_changes_the_later_read_is_kept()
    {
        var queue = new FakePlanTagSessionRequests();
        queue.Repositories["alpha"] = ["backlog"];
        using var requests = new TasksPlanSessionRequests(queue, new FakeTimeProvider(DateTimeOffset.UtcNow));
        await requests.RefreshAsync(withRepositories: true);

        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.RepositoryGates.Enqueue(first);
        queue.RepositoryGates.Enqueue(second);

        queue.Repositories["alpha"] = ["tools"];
        var earlier = requests.RefreshAsync(withRepositories: true);
        queue.Repositories["alpha"] = ["backlog", "tools"];
        var later = requests.RefreshAsync(withRepositories: true);

        // The later read lands first, the earlier one after it.
        second.SetResult();
        await later;
        first.SetResult();
        await earlier;

        Assert.Equal(["backlog", "tools"], requests.RepositoriesOf("+alpha")!.Repositories);
    }

    // --- The offer on its own -------------------------------------------------

    [Fact]
    public void The_offer_strips_the_sigil_and_skips_archived_and_unconfigured_repositories()
    {
        var offer = PlanQueueOffer.For(
            "+alpha",
            [null, new GitHubRepositoryRef("old", "JSdotNet", "Old") { CloneDirectory = @"D:\Old", Archived = true }, new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog") { CloneDirectory = BacklogClone }]);

        Assert.Equal(new PlanQueueOffer("alpha", Backlog, BacklogClone, null), offer);
        Assert.Null(PlanQueueOffer.For("+", []));
    }

    /// <summary>The port, in memory: where each plan is filed, what was asked and
    /// cancelled, and what is open, which a test sets as a dispatcher would.</summary>
    private sealed class FakePlanTagSessionRequests : IPlanTagSessionRequests
    {
        public event Action? PlanChanged;

        public bool IsOffered { get; set; } = true;

        /// <summary>Raises <see cref="PlanChanged"/>, as a roadmap save would.</summary>
        public void RaisePlanChanged() => PlanChanged?.Invoke();

        /// <summary>Gates the next repositories reads take in turn, each after it has
        /// read <see cref="Repositories"/>, so a test can make two reads overlap.</summary>
        public Queue<TaskCompletionSource> RepositoryGates { get; } = new();

        /// <summary>Set, an open-requests read waits on it after it has read
        /// <see cref="Open"/>, so a test can land a poll after a press.</summary>
        public TaskCompletionSource? OpenGate { get; set; }

        public Dictionary<string, IReadOnlyList<string>> Repositories { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, EntrySessionRequest> Open { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Tag, string Repository, string CloneFolder)> Asked { get; } = [];

        public List<Guid> Cancelled { get; } = [];

        /// <summary>Set, a request waits on it after it is recorded in
        /// <see cref="Asked"/>, so a test can look at the pane mid-press.</summary>
        public TaskCompletionSource? Gate { get; set; }

        public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> RepositoriesAsync(CancellationToken cancellationToken = default)
        {
            var read = new Dictionary<string, IReadOnlyList<string>>(Repositories, StringComparer.OrdinalIgnoreCase);
            if (RepositoryGates.TryDequeue(out var gate)) await gate.Task;
            return read;
        }

        public async Task<IReadOnlyDictionary<string, EntrySessionRequest>> OpenAsync(CancellationToken cancellationToken = default)
        {
            var read = new Dictionary<string, EntrySessionRequest>(Open, StringComparer.OrdinalIgnoreCase);
            if (OpenGate is { } gate)
            {
                OpenGate = null;
                await gate.Task;
            }

            return read;
        }

        public async Task<EntrySessionRequest> RequestAsync(string planTag, string repository, string cloneFolder, CancellationToken cancellationToken = default)
        {
            Asked.Add((planTag, repository, cloneFolder));
            if (Gate is { } gate) await gate.Task;

            var request = Open.TryGetValue(planTag, out var open) ? open : new EntrySessionRequest(Guid.NewGuid(), EntrySessionRequestState.Requested, repository);
            Open[planTag] = request;
            return request;
        }

        public Task<bool> CancelAsync(Guid requestId, CancellationToken cancellationToken = default)
        {
            var held = Open.FirstOrDefault(pair => pair.Value.Id == requestId);
            if (held.Value is not { State: EntrySessionRequestState.Requested }) return Task.FromResult(false);

            Cancelled.Add(requestId);
            Open.Remove(held.Key);
            return Task.FromResult(true);
        }
    }
}
