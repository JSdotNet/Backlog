using Backlog.Desktop.UI.Tasks;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;
using Backlog.UI.Components.Feedback;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The save state as a band on every route rather than a line on one screen.
/// <para>
/// The indicator used to live in Home's header, where it was only ever on screen
/// while the backlog was, and where a latched "Saved" was harmless because leaving
/// the screen took it away. It is the app shell's footer now, which changes what
/// the state has to mean: a resting "Saved" would sit on Settings asserting
/// something about a screen the reader is not on, and
/// <c>.devbook/design/interaction-guidelines.md#save-state-indicator-vocabulary</c> says in
/// so many words that <c>Saved</c> "MUST NOT nag". So the band has a quiet state,
/// it starts in it, and it goes back to it.
/// </para>
/// <para>
/// The dwell runs on a <see cref="FakeTimeProvider"/> the tests move by hand, so
/// "goes back to it" is asserted on either side of the 2 s rather than after a
/// sleep comfortably past it.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TasksSaveStateBandTests : IDisposable
{
    /// <summary>The 2 s dwell, one millisecond short of it.</summary>
    private static readonly TimeSpan JustShortOfTheDwell = TimeSpan.FromMilliseconds(1999);

    /// <summary>A roadmap-level entry: the same grammar, and the one type word
    /// this path refuses.</summary>
    private const string PlanEntry = "# Imported plans on the roadmap\n`plan` `+roadmap-imported-plans`\n";

    private readonly FakeTimeProvider _clock = new();
    private readonly List<string> _tempDirs = [];
    private readonly List<TasksDesktopState> _states = [];

    [Fact]
    public async Task A_backlog_nobody_has_touched_says_nothing()
    {
        var (state, _) = Build();
        await state.InitializeAsync();

        Assert.Equal(AppSaveState.Idle, state.SaveState);
    }

    [Fact]
    public async Task A_save_says_so_and_then_goes_quiet_again()
    {
        var (state, _) = Build();
        await state.InitializeAsync();

        await WriteEntryAsync(state, "# Something worth saving");

        Assert.Equal(AppSaveState.Saved, state.SaveState);

        var raised = 0;
        state.Changed += () => raised++;

        _clock.Advance(JustShortOfTheDwell);

        Assert.Equal(AppSaveState.Saved, state.SaveState);
        var beforeTheSettle = raised;

        _clock.Advance(TimeSpan.FromMilliseconds(1));

        await TasksTestHost.UntilAsync(state, () => state.SaveState == AppSaveState.Idle);

        // The settle has to tell the footer, or the band would keep drawing the
        // word after the state behind it had stopped meaning it.
        Assert.True(raised > beforeTheSettle, "Settling back to Idle re-renders the band.");
    }

    /// <summary>
    /// A failure has no timeout. The vocabulary chapter gives an error a retry
    /// affordance rather than a dwell, and there is nowhere else on the shell the
    /// reader could find out afterwards that the write did not land.
    /// </summary>
    [Fact]
    public async Task A_failure_stays_up()
    {
        var (state, _) = Build(entries => new ImportThrows(entries));
        await state.InitializeAsync();

        await state.ImportPlanAsync("# One\n`prompt`\n");

        Assert.Equal(AppSaveState.Error, state.SaveState);

        _clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(AppSaveState.Error, state.SaveState);
    }

    /// <summary>
    /// A repository a plan names that the registry could not keep stops the
    /// import, and the import surface says so in the registry's own words rather
    /// than the generic "Import failed." — which says nothing about what to fix.
    /// Driven through the real Settings adapter over a registry that cannot be
    /// read, the worse of the two ways a registration fails.
    /// </summary>
    [Fact]
    public async Task An_import_whose_repository_could_not_be_registered_says_why()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-save-band", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(root);
        var registry = Path.Combine(root, "github", GitHubSettingsStore.RegistryFolderName, "repos.json");
        Directory.CreateDirectory(Path.GetDirectoryName(registry)!);
        File.WriteAllText(registry, "{ this is not a registry");

        var settings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        Assert.NotNull(settings.RegistryError);

        var (state, store) = Build(repositories: new SettingsRepositoryDirectory(settings));
        await state.InitializeAsync();

        var sentence = await state.ImportPlanAsync("# One\n`prompt` `repo:newcomer`\n");

        Assert.Equal(settings.RegistryError, sentence);
        Assert.Empty(await TasksTestHost.EntriesFor(store).ListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A <c>plan</c> entry is refused outright (ADR 0013 ruling 2), and that is
    /// not one of the failures this band holds quietly. The other two are states
    /// somebody is passing through — no title yet, or an entry deleted from under
    /// them. This is finished text, still on screen, that was not stored: a band
    /// reading "Saved" would be asserting something untrue about it, and nothing
    /// else on the row would say otherwise.
    /// </summary>
    [Fact]
    public async Task A_refused_plan_entry_says_so_rather_than_claiming_a_save()
    {
        var toasts = new ToastChannel();
        var (state, _) = Build(toasts: toasts);
        await state.InitializeAsync();

        await WriteEntryAsync(state, PlanEntry);

        Assert.Equal(AppSaveState.Error, state.SaveState);

        var toast = Assert.Single(toasts.Visible);
        Assert.Equal(ToastSeverity.Error, toast.Severity);
        Assert.Contains("Import", toast.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Saving runs on a debounce while somebody is still typing, and a commit can
    /// arrive twice for one gesture, so the refusal is announced on the way into
    /// it rather than once per save. Announced per save, a plan entry left sitting
    /// on screen would raise a toast for every keystroke that followed it.
    /// </summary>
    [Fact]
    public async Task A_refused_plan_entry_is_announced_once_however_often_it_is_saved()
    {
        var toasts = new ToastChannel();
        var (state, _) = Build(toasts: toasts);
        await state.InitializeAsync();

        var row = await WriteEntryAsync(state, PlanEntry);

        await state.EndEditAsync(row);
        await state.EndEditAsync(row);

        Assert.Single(toasts.Visible);
        Assert.Equal(AppSaveState.Error, state.SaveState);
    }

    /// <summary>Fixing the type word lets the entry land, and re-arms the
    /// announcement — a second mistake is reported rather than swallowed because
    /// the first one already was.</summary>
    [Fact]
    public async Task Fixing_a_refused_plan_entry_saves_and_re_arms_the_announcement()
    {
        var toasts = new ToastChannel();
        var (state, _) = Build(toasts: toasts);
        await state.InitializeAsync();

        var row = await WriteEntryAsync(state, PlanEntry);
        Assert.Equal(AppSaveState.Error, state.SaveState);

        state.OnRawTextInput(row, "# Imported plans on the roadmap\n`task` `+roadmap-imported-plans`\n");
        await state.EndEditAsync(row);

        Assert.Equal(AppSaveState.Saved, state.SaveState);

        state.OnRawTextInput(row, PlanEntry);
        await state.EndEditAsync(row);

        Assert.Equal(AppSaveState.Error, state.SaveState);
        Assert.Equal(2, toasts.Visible.Count);
    }

    /// <summary>
    /// Setting the state to what it already is changes nothing and says nothing.
    /// <para>
    /// The guard is not tidiness. <c>Changed</c> re-renders the whole of TasksPane,
    /// and it is raised at the end of every debounce flush — so an unguarded
    /// re-assert would redraw the list on every keystroke's worth of typing. It is
    /// also what
    /// <c>.devbook/design/accessibility.md#screen-reader--announcements</c> asks for: routine
    /// Saving/Saved transitions "MUST be throttled so screen readers are not
    /// flooded during continuous typing", and re-raising a polite live region for a
    /// state that did not change is exactly that flood.
    /// </para>
    /// <para>
    /// Driven through the Copilot CLI because it is the one public path that
    /// reports a successful save without passing through Saving on the way: the row
    /// goes busy, the CLI starts, and the state is asserted to be Saved when it
    /// already is. Two <c>Changed</c> raises are the ones the busy flag owes —
    /// going busy and coming back — and a third would be the redundant one.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Re_asserting_the_same_state_tells_nobody()
    {
        var (state, _) = Build(copilot: new TasksCopilotCli(new SilentCopilotCliLauncher()));
        await state.InitializeAsync();

        var row = await WriteEntryAsync(state, "# Hand this to the CLI");

        Assert.Equal(AppSaveState.Saved, state.SaveState);

        var raised = 0;
        state.Changed += () => raised++;

        await state.StartCopilotCliAsync(row);

        Assert.Equal(2, raised);
    }

    /// <summary>A quiet band has no sentence. The label's default arm used to
    /// answer "Saved" for anything it did not recognise, which the new quiet state
    /// walked straight into — a backlog that has just opened would have claimed a
    /// write that never happened.</summary>
    [Fact]
    public async Task A_quiet_band_has_no_sentence()
    {
        var (state, _) = Build();
        await state.InitializeAsync();

        Assert.Equal(AppSaveState.Idle, state.SaveState);
        Assert.Equal(string.Empty, state.SaveStateLabel);
    }

    /// <summary>
    /// A save that lands on the state the last one left behind still gets its own
    /// dwell.
    /// <para>
    /// The equality guard in <c>SetSaveState</c> is about not re-announcing, and it
    /// must not also swallow the timer: a second save 1.9 seconds into the first
    /// one's window would otherwise inherit its tail and be put away a tenth of a
    /// second later, so the reader gets no confirmation that it landed.
    /// </para>
    /// <para>
    /// Measured from the second save on a moved clock: one millisecond short of
    /// its own 2 s the band still says Saved, and it goes quiet on the next one.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_second_save_inside_the_dwell_gets_its_own()
    {
        var (state, _) = Build(copilot: new TasksCopilotCli(new SilentCopilotCliLauncher()));
        await state.InitializeAsync();

        var row = await WriteEntryAsync(state, "# Hand this to the CLI");

        Assert.Equal(AppSaveState.Saved, state.SaveState);

        // Partway into the first dwell, so the settle it armed is still pending.
        _clock.Advance(TimeSpan.FromMilliseconds(1000));

        await state.StartCopilotCliAsync(row);

        Assert.Equal(AppSaveState.Saved, state.SaveState);

        // Past where the first dwell would have ended, and short of the second.
        _clock.Advance(JustShortOfTheDwell);

        Assert.Equal(AppSaveState.Saved, state.SaveState);

        _clock.Advance(TimeSpan.FromMilliseconds(1));

        await TasksTestHost.UntilAsync(state, () => state.SaveState == AppSaveState.Idle);
    }

    /// <summary>
    /// A failure the reader may not be looking at is published as a toast as well
    /// as left on the row, and the toast has to say which row — the band it lands
    /// in is nowhere near the entry that failed.
    /// </summary>
    [Fact]
    public async Task A_copilot_failure_names_the_row_it_came_from()
    {
        var toasts = new ToastChannel();
        var (state, _) = Build(
            copilot: new TasksCopilotCli(new FailingCopilotCliLauncher("No CLI here.")),
            toasts: toasts);
        await state.InitializeAsync();

        var row = await WriteEntryAsync(state, "# Hand this to the CLI");

        await state.StartCopilotCliAsync(row);

        var toast = Assert.Single(toasts.Visible);
        Assert.Equal("copilot-cli-error", toast.TestId);
        Assert.Equal(ToastSeverity.Error, toast.Severity);
        Assert.Contains(row.PreviewTitle, toast.Message, StringComparison.Ordinal);
        Assert.Contains("No CLI here.", toast.Message, StringComparison.Ordinal);

        // The row keeps its own line as well. The toast is the action-level
        // feedback and goes away; the inline alert is the section-level record and
        // stays with the entry that failed, per
        // .devbook/design/interaction-guidelines.md#error-states.
        Assert.Equal("No CLI here.", row.CopilotError);
    }

    // --- Composition --------------------------------------------------------

    private (TasksDesktopState State, WorkspaceSettingsStore Store) Build(
        Func<ITaskItems, ITaskItems>? decorate = null,
        TasksCopilotCli? copilot = null,
        IToastChannel? toasts = null,
        IRepositoryDirectory? repositories = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-save-band", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(root);

        var store = new WorkspaceSettingsStore(Path.Combine(root, "settings"));
        Assert.Null(store.TryUseRoot(root));

        var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var integration = new GitHubIntegration(settings, new UnusedGitHubClient(), new UnusedProbe());

        var entries = TasksTestHost.EntriesFor(TasksTestHost.RepositoryFor(store), repositories);
        var state = new TasksDesktopState(
            TasksTestHost.TaskStoreFor(store),
            decorate is null ? entries : decorate(entries),
            integration,
            copilot,
            toasts: toasts,
            timeProvider: _clock);

        _states.Add(state);

        return (state, store);
    }

    private static async Task<EntryRow> WriteEntryAsync(TasksDesktopState state, string text)
    {
        state.NewRow();
        var row = state.Rows[^1];
        state.OnRawTextInput(row, text);
        await state.EndEditAsync(row);
        return row;
    }

    public void Dispose()
    {
        // Before the folders below go: the state arms timed saves and a timed
        // settle, and one that elapsed after its folder was deleted is work the
        // test host is still holding when the run is over.
        foreach (var state in _states)
        {
            state.Dispose();
        }

        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>The module, with its import replaced by a throw. Everything else
    /// delegates, so the state under test is otherwise the real one.</summary>
    private sealed class ImportThrows(ITaskItems inner) : ITaskItems
    {
        public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default) =>
            inner.ListAsync(cancellationToken);

        public Task<Result<SavedTaskDto>> SaveFromTextAsync(Guid? id, string rawText, int order, string? sourceInboxId = null, CancellationToken cancellationToken = default) =>
            inner.SaveFromTextAsync(id, rawText, order, sourceInboxId, cancellationToken);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(id, cancellationToken);

        public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) =>
            inner.ReorderAsync(idsInOrder, cancellationToken);

        public Task<Result<TaskItemDto>> LinkToIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            inner.LinkToIssueAsync(id, repoId, externalId, targetType, cancellationToken);

        public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default) =>
            inner.RecordUsageAsync(id, action, cancellationToken);

        public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) =>
            inner.ReconcileRepositoryIdsAsync(cancellationToken);

        public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) =>
            inner.RenameRepositoryAsync(oldId, newId, cancellationToken);

        public Task<Result<ImportPlanResultDto>> ImportPlanAsync(
            string rawText,
            string? defaultRepo = null,
            IReadOnlyDictionary<string, string>? repoMatches = null,
            string? sourceInboxId = null,
            IReadOnlyDictionary<string, string>? sourceInboxIds = null,
            bool layOutOnRoadmap = false,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The store is not reachable.");
    }

    private sealed class SilentCopilotCliLauncher : ICopilotCliLauncher
    {
        public Task LaunchAsync(CopilotCliRequest request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FailingCopilotCliLauncher(string message) : ICopilotCliLauncher
    {
        public Task LaunchAsync(CopilotCliRequest request, CancellationToken cancellationToken = default) =>
            throw new CopilotCliException(message);
    }

    /// <summary>Nothing here goes near GitHub; the integration only exists because
    /// the state takes one.</summary>
    private sealed class UnusedGitHubClient : IGitHubClient
    {
        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<GitHubIssue> CreateIssueAsync(GitHubRepositoryRef repository, string title, string? body, IEnumerable<string>? labels = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(GitHubRepositoryRef repository, string path, string branch, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not configured."));

        public void Invalidate()
        {
        }
    }
}
