using Backlog.Infrastructure.Sessions;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// The latest sync verdict per devbook chapter: a later run replaces exactly the
/// chapters it names, and a chapter is found by any spelling of its file.
/// </summary>
public sealed class DevbookSyncVerdictStoreTests : IDisposable
{
    private const string Repository = "JSdotNet/Backlog";

    private readonly string _home = Path.Combine(
        Path.GetTempPath(),
        "backlog-sync-verdict-tests",
        Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task A_later_run_replaces_the_chapters_it_names_and_leaves_the_rest()
    {
        var store = new DevbookSyncVerdictStore(_home);

        await store.RecordAsync(
            [
                Verdict(".arc42/building-blocks/delivery.md", "run-start", "code-ahead", "run-1"),
                Verdict(".arc42/building-blocks/delivery.md", "run-finish", "conflict", "run-1")
            ],
            TestContext.Current.CancellationToken);

        await store.RecordAsync(
            [Verdict(".arc42/building-blocks/delivery.md", "run-start", "aligned", "run-2")],
            TestContext.Current.CancellationToken);

        var filed = store.For(Repository, ".arc42/building-blocks/delivery.md");

        Assert.Equal("aligned", Assert.Single(filed, verdict => verdict.Anchor == "run-start").Verdict);

        // Not named by the second run, so the first run's word still stands.
        Assert.Equal("conflict", Assert.Single(filed, verdict => verdict.Anchor == "run-finish").Verdict);
    }

    [Fact]
    public async Task A_chapter_is_found_by_any_spelling_of_its_file_and_only_in_its_repository()
    {
        var store = new DevbookSyncVerdictStore(_home);

        await store.RecordAsync(
            [Verdict(".arc42/building-blocks/delivery.md", "run-start", "code-ahead", "run-1")],
            TestContext.Current.CancellationToken);

        Assert.Single(store.For(Repository, ".devbook/arc42/building-blocks/delivery.md"));
        Assert.Single(store.For("jsdotnet/backlog", @".arc42\building-blocks\delivery.md#run-start"));

        Assert.Empty(store.For("JSdotNet/devbook", ".arc42/building-blocks/delivery.md"));
        Assert.Empty(store.For(Repository, ".arc42/building-blocks/delivery-schedule.md"));
    }

    [Fact]
    public async Task A_repository_answers_every_chapter_recorded_in_it_and_no_other_repositorys()
    {
        var store = new DevbookSyncVerdictStore(_home);

        await store.RecordAsync(
            [
                Verdict(".domain/tasks/domain.md", "task", "spec-ahead", "run-1") with { Sync = "push", SyncFrom = ".domain/tasks/context.md" },
                Verdict(".arc42/building-blocks/delivery.md", "run-start", "aligned", "run-1"),
                Verdict(".domain/tasks/domain.md", "task", "aligned", "run-2") with { Repository = "JSdotNet/devbook" }
            ],
            TestContext.Current.CancellationToken);

        var recorded = new DevbookSyncVerdictStore(_home).ForRepository("jsdotnet/backlog");

        Assert.Equal(2, recorded.Count);
        var task = Assert.Single(recorded, verdict => verdict.Anchor == "task");
        Assert.Equal("push", task.Sync);
        Assert.Equal(".domain/tasks/context.md", task.SyncFrom);
        Assert.Null(Assert.Single(recorded, verdict => verdict.Anchor == "run-start").Sync);
        Assert.Empty(store.ForRepository(" "));
    }

    [Fact]
    public async Task Recording_raises_changed_and_a_second_store_reads_what_the_first_wrote()
    {
        var store = new DevbookSyncVerdictStore(_home);
        var raised = 0;
        store.Changed += () => raised++;

        await store.RecordAsync(
            [Verdict(".domain/tasks/domain.md", "task", "spec-ahead", "run-1")],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, raised);

        // Another process — the desktop app and the harness share the profile.
        var other = new DevbookSyncVerdictStore(_home);

        Assert.Equal("spec-ahead", Assert.Single(other.For(Repository, ".domain/tasks/domain.md")).Verdict);
    }

    [Fact]
    public void An_unreadable_file_reads_as_no_verdicts_rather_than_a_failure()
    {
        var store = new DevbookSyncVerdictStore(_home);

        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, "{ not json");

        Assert.Empty(store.For(Repository, ".domain/tasks/domain.md"));
    }

    [Theory]
    [InlineData("Delivery Run Recording", "delivery-run-recording")]
    [InlineData("  Run Started  ", "run-started")]
    [InlineData("What a run cost, reported by the session itself", "what-a-run-cost-reported-by-the-session-itself")]
    [InlineData("C4 / Containers", "c4--containers")]
    public void A_heading_is_addressed_by_the_anchor_the_devbook_tools_give_it(string heading, string anchor) =>
        Assert.Equal(anchor, DevbookSyncChapters.AnchorOf(heading));

    private static DevbookSyncVerdict Verdict(string path, string anchor, string verdict, string runId) =>
        new(
            Repository,
            path,
            anchor,
            verdict,
            Evidence: null,
            Unit: $"{path}#{anchor}",
            UnitKind: "building-block",
            UnitVerdict: verdict,
            Action: "issue",
            Link: "https://github.com/JSdotNet/Backlog/issues/900",
            RecordedAt: DateTimeOffset.UtcNow,
            RunId: runId);

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }
}
