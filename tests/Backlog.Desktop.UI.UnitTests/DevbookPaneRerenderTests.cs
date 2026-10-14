using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Devbook pane sits beside the Tasks pane, and typing in a task re-renders
/// the shell on every keystroke — which hands the pane its parameters again, every
/// one of them unchanged. On the Brain each of those used to read
/// <c>CONVENTIONS.md</c> and walk the whole Brain folder for conflict copies, on
/// the UI thread, usually inside OneDrive. The pane reads the Brain when what it
/// shows has moved — the Brain itself, its folder, or the side of the switch —
/// and walks for conflict copies off the UI thread.
/// </summary>
public sealed class DevbookPaneRerenderTests
{
    private const string WorkArea = "## Areas\n\n- Work — `work`\n";

    [Fact]
    public async Task A_parent_render_with_unchanged_parameters_reads_nothing_from_the_brain()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, WorkArea);
        harness.WriteNote("work/plan.md", "# Plan\n");
        harness.WriteNote("work/plan-LAPTOP.md", "# Plan\n");
        var brain = CountingBrainFolderSource.Install(harness);

        var component = harness.RenderBrain();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll(".devbook-menu__item--file"));
            Assert.Single(component.FindAll("[data-testid='devbook-brain-conflict']"));
        });
        var areas = brain.AreaReads;
        var walks = brain.ConflictWalks;

        for (var render = 0; render < 3; render++)
        {
            component.Render(parameters => parameters.Add(pane => pane.RepositoryAlias, string.Empty));
        }

        await component.InvokeAsync(() => { });
        Assert.Equal(areas, brain.AreaReads);
        Assert.Equal(walks, brain.ConflictWalks);
    }

    [Fact]
    public async Task The_conflict_walk_does_not_run_on_the_renderers_thread()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, WorkArea);
        harness.WriteNote("work/plan.md", "# Plan\n");
        harness.WriteNote("work/plan-LAPTOP.md", "# Plan\n");
        var brain = CountingBrainFolderSource.Install(harness);

        var component = harness.RenderBrain();

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='devbook-brain-conflict']")));
        Assert.True(brain.ConflictWalks > 0);
        Assert.Equal(0, brain.ConflictWalksOnRenderer);
    }

    [Fact]
    public async Task A_brain_change_reads_the_brain_again()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, WorkArea);
        harness.WriteNote("work/plan.md", "# Plan\n");

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.Equal(["Work"], DevbookPaneBrainHarness.Tabs(component)));

        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Work — `work`\n- Home life — `home`\n");
        harness.WriteNote("work/plan-LAPTOP.md", "# Plan\n");
        await Task.Run(harness.Brain.NotifyContentChanged, TestContext.Current.CancellationToken);

        component.WaitForAssertion(() =>
        {
            Assert.Equal(["Work", "Home life"], DevbookPaneBrainHarness.Tabs(component));
            Assert.Contains("work/plan-LAPTOP.md", component.Find("[data-testid='devbook-brain-conflict']").TextContent, StringComparison.Ordinal);
        });
    }

    /// <summary>A Brain folder moved in Settings is a different Brain, and the next
    /// render of the pane shows it, change notice or not.</summary>
    [Fact]
    public async Task A_parent_render_after_the_brain_folder_moved_reads_the_new_brain()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, WorkArea);
        harness.WriteNote("work/plan.md", "# Plan\n");
        harness.WriteNote("work/plan-LAPTOP.md", "# Plan\n");
        var other = OtherBrain(harness, conflict: false);

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='devbook-brain-conflict']")));

        harness.Settings.TrySetBrainFolder(other);
        component.Render(parameters => parameters.Add(pane => pane.RepositoryAlias, string.Empty));

        component.WaitForAssertion(() =>
        {
            Assert.Equal(["Garden"], DevbookPaneBrainHarness.Tabs(component));
            Assert.Empty(component.FindAll("[data-testid='devbook-brain-conflict']"));
        });
    }

    [Fact]
    public async Task Coming_back_from_the_repository_reads_the_brain_again()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, WorkArea);
        harness.WriteNote("work/plan.md", "# Plan\n");

        var component = harness.RenderBrain(harness.RepositoryAlias);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item--file")));

        component.Render(parameters => parameters.Add(pane => pane.RepositoryAlias, harness.RepositoryAlias));
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='devbook-brain-badge']")));

        harness.WriteNote("work/plan-LAPTOP.md", "# Plan\n");
        component.Render(parameters => parameters.Add(pane => pane.RepositoryAlias, string.Empty));

        component.WaitForAssertion(() =>
            Assert.Contains("work/plan-LAPTOP.md", component.Find("[data-testid='devbook-brain-conflict']").TextContent, StringComparison.Ordinal));
    }

    /// <summary>The walk runs while the pane goes on: a walk of the Brain the pane
    /// has since moved off lands nowhere, even when it finishes last.</summary>
    [Fact]
    public async Task A_walk_of_a_brain_the_pane_has_left_does_not_land()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, WorkArea);
        harness.WriteNote("work/plan.md", "# Plan\n");
        harness.WriteNote("work/plan-LAPTOP.md", "# Plan\n");
        var other = OtherBrain(harness, conflict: false);
        var brain = CountingBrainFolderSource.Install(harness);
        using var held = brain.HoldNextWalk();

        var component = harness.RenderBrain();
        Assert.True(held.Started.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        harness.Settings.TrySetBrainFolder(other);
        component.Render(parameters => parameters.Add(pane => pane.RepositoryAlias, string.Empty));
        component.WaitForAssertion(() => Assert.Equal(["Garden"], DevbookPaneBrainHarness.Tabs(component)));
        Assert.True(SpinWait.SpinUntil(() => brain.CompletedWalks == 1, TimeSpan.FromSeconds(10)));

        held.Release();
        Assert.True(SpinWait.SpinUntil(() => brain.CompletedWalks == 2, TimeSpan.FromSeconds(10)));

        // A bounded wait rather than a signal, because there is no signal to wait
        // for: a dropped answer changes nothing, by design, and the pane drops it
        // on the sequence before it so much as looks at it. The held walk's answer
        // goes back through the dispatcher straight after the walk returns, so an
        // answer that did land would be on screen well inside this.
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await component.InvokeAsync(() => { });
        Assert.Empty(component.FindAll("[data-testid='devbook-brain-conflict']"));
    }

    /// <summary>A second Brain folder beside the harness's own, with one area.</summary>
    private static string OtherBrain(DevbookPaneBrainHarness harness, bool conflict)
    {
        var folder = Path.Combine(harness.Root, "Other Brain");
        Directory.CreateDirectory(Path.Combine(folder, "garden"));
        File.WriteAllText(Path.Combine(folder, BrainConventions.FileName), "## Areas\n\n- Garden — `garden`\n");
        File.WriteAllText(Path.Combine(folder, "garden", "tomatoes.md"), "# Tomatoes\n");
        if (conflict) File.WriteAllText(Path.Combine(folder, "garden", "tomatoes-LAPTOP.md"), "# Tomatoes\n");
        return folder;
    }
}

/// <summary>
/// The harness's Brain, counting the two reads the pane makes of it: the areas
/// (<c>CONVENTIONS.md</c>) and the conflict walk (the whole folder). Registered
/// over the harness's own, so the pane reads through it.
/// </summary>
internal sealed class CountingBrainFolderSource(IBrainFolderSource inner, Func<bool> onRenderer) : IBrainFolderSource
{
    private int _areas;
    private int _walks;
    private int _walksOnRenderer;
    private int _completedWalks;
    private HeldWalk? _hold;

    public static CountingBrainFolderSource Install(DevbookPaneBrainHarness harness)
    {
        var counting = new CountingBrainFolderSource(harness.Brain, () => harness.Context.Renderer.Dispatcher.CheckAccess());
        harness.Context.Services.AddSingleton<IBrainFolderSource>(counting);
        return counting;
    }

    public int AreaReads => Volatile.Read(ref _areas);

    public int ConflictWalks => Volatile.Read(ref _walks);

    public int ConflictWalksOnRenderer => Volatile.Read(ref _walksOnRenderer);

    public int CompletedWalks => Volatile.Read(ref _completedWalks);

    /// <summary>Set once the pane has read a walk's answer — which it does on the
    /// dispatcher, as the answer lands. A walk that finds nothing changes nothing
    /// on screen, so this is the only sign it has landed.</summary>
    public ManualResetEventSlim AnswerRead { get; } = new();

    /// <summary>Holds the next walk after it has read the folder and before it
    /// answers, until <see cref="HeldWalk.Release"/>.</summary>
    public HeldWalk HoldNextWalk() => _hold = new HeldWalk();

    public string? BrainRoot => inner.BrainRoot;

    public bool BrainFolderPresent() => inner.BrainFolderPresent();

    public IReadOnlyList<BrainArea> Areas()
    {
        Interlocked.Increment(ref _areas);
        return inner.Areas();
    }

    public IReadOnlyList<BrainConflictCopy> ConflictCopies()
    {
        Interlocked.Increment(ref _walks);
        if (onRenderer()) Interlocked.Increment(ref _walksOnRenderer);

        var answer = inner.ConflictCopies();
        if (Interlocked.Exchange(ref _hold, null) is { } hold)
        {
            hold.Started.Set();
            hold.Released.Wait(TimeSpan.FromSeconds(10));
        }

        Interlocked.Increment(ref _completedWalks);
        return new ObservedAnswer(answer, AnswerRead);
    }

    public event Action? Changed
    {
        add => inner.Changed += value;
        remove => inner.Changed -= value;
    }

    public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) => inner.Folders(repositoryAlias);

    public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null) => inner.Resolve(key, repositoryAlias);

    public Task<DevbookFolderLocation> PrepareListingAsync(string key, string? repositoryAlias = null, CancellationToken cancellationToken = default) =>
        inner.PrepareListingAsync(key, repositoryAlias, cancellationToken);

    public Task<DevbookFolderLocation> PrepareContentAsync(
        string key,
        string? repositoryAlias = null,
        IReadOnlyCollection<string>? relativePaths = null,
        CancellationToken cancellationToken = default) =>
        inner.PrepareContentAsync(key, repositoryAlias, relativePaths, cancellationToken);

    public IDevbookFileTree FileTree(DevbookFolderLocation location) => inner.FileTree(location);

    public void NotifyContentChanged() => inner.NotifyContentChanged();

    /// <summary>A walk's answer that says when it is read.</summary>
    private sealed class ObservedAnswer(IReadOnlyList<BrainConflictCopy> answer, ManualResetEventSlim read) : IReadOnlyList<BrainConflictCopy>
    {
        public int Count
        {
            get
            {
                read.Set();
                return answer.Count;
            }
        }

        public BrainConflictCopy this[int index]
        {
            get
            {
                read.Set();
                return answer[index];
            }
        }

        public IEnumerator<BrainConflictCopy> GetEnumerator()
        {
            read.Set();
            return answer.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal sealed class HeldWalk : IDisposable
    {
        public ManualResetEventSlim Started { get; } = new();

        public ManualResetEventSlim Released { get; } = new();

        public void Release() => Released.Set();

        /// <summary>Releases rather than disposes: a walk still held when the test
        /// ends must find the gate open, not gone.</summary>
        public void Dispose() => Released.Set();
    }
}
