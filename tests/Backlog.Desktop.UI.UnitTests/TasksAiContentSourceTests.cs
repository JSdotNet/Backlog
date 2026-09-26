using Backlog.Desktop.UI.Tasks;
using Backlog.SharedKernel.Ai;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Tasks' Ask AI content: the entries in the repository scope as they were
/// written, whatever the status chips and the other filters have left in view.
/// </summary>
public class TasksAiContentSourceTests
{
    [Fact]
    public async Task A_record_is_the_entry_text_as_typed()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        await host.WriteEntryAsync("# Provision the box\n`task` `!ready` `repo:backlog`\n");

        var content = await new TasksAiContentSource(host.State).ComposeAsync(new AiContentRequest("box", 6000), TestContext.Current.CancellationToken);

        // The text as the list holds it after the store tidied the tokens on the
        // way in — what matters is that nothing is derived from them here.
        var row = Assert.Single(host.State.Rows);
        Assert.Equal("tasks", content.AreaKey);
        Assert.Equal($"Tasks: 1 entry.\n{Totals(open: 1, ready: 1, task: 1)}\n{row.RawText.Trim()}", content.Body);
        Assert.Contains("# Provision the box", content.Body, StringComparison.Ordinal);
    }

    /// <summary>"How many open tasks do I have?" used to be answered "I can't
    /// tell" — the body held the eleven entries that fit, not the backlog. The
    /// totals line counts the whole scope, whatever the budget let through.</summary>
    [Fact]
    public async Task The_totals_line_counts_the_whole_scope_when_the_records_are_trimmed()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        await host.WriteEntryAsync("# Ready one\n`task` `!ready` `repo:backlog`\n");
        await host.WriteEntryAsync("# Started one\n`task` `!in-progress` `repo:backlog`\n");
        await host.WriteEntryAsync("# Finished one\n`task` `!done` `repo:backlog`\n");
        await host.WriteEntryAsync("# Idea one\n`idea` `!draft` `repo:backlog`\n");

        // Room for the header and the totals line and nothing else.
        var budget = AiContentBudget.HeaderReserve("Tasks", 4) + Totals(open: 3, draft: 1, ready: 1, inProgress: 1, done: 1, task: 3, idea: 1).Length + 1;
        var content = await new TasksAiContentSource(host.State).ComposeAsync(new AiContentRequest("how many open tasks", budget), TestContext.Current.CancellationToken);

        Assert.True(content.Trimmed);
        Assert.Equal(0, content.Shown);
        Assert.Equal(
            $"Tasks: 0 of 4 entries, selected by relevance to the question.\n{Totals(open: 3, draft: 1, ready: 1, inProgress: 1, done: 1, task: 3, idea: 1)}",
            content.Body);
        Assert.True(content.Body.Length <= budget);
    }

    [Fact]
    public async Task An_empty_scope_carries_no_totals_line()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");

        var content = await new TasksAiContentSource(host.State).ComposeAsync(new AiContentRequest("how many", 6000), TestContext.Current.CancellationToken);

        Assert.Equal("Tasks: 0 entries.", content.Body);
    }

    private static string Totals(int open, int draft = 0, int ready = 0, int inProgress = 0, int done = 0, int archived = 0, int prompt = 0, int task = 0, int idea = 0, int test = 0) =>
        $"Totals across all entries in scope: {open} open (not done or archived). " +
        $"By status: {draft} draft, {ready} ready, {inProgress} in progress, {done} done, {archived} archived. " +
        $"By type: {prompt} prompt, {task} task, {idea} idea, {test} test.";

    /// <summary>The old panel read <c>FilteredRows</c>, so pressing "Draft"
    /// changed the answer. The status chip is screen state; the body is the
    /// scope either way.</summary>
    [Fact]
    public async Task The_status_filter_does_not_change_the_body()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        await host.WriteEntryAsync("# Ready one\n`task` `!ready` `repo:backlog`\n");
        await host.WriteEntryAsync("# Draft one\n`task` `!draft` `repo:backlog`\n");
        var source = new TasksAiContentSource(host.State);

        var whole = await source.ComposeAsync(new AiContentRequest("one", 6000), TestContext.Current.CancellationToken);

        host.State.SetStatusFilter("draft");
        Assert.Single(host.State.FilteredRows);

        var filtered = await source.ComposeAsync(new AiContentRequest("one", 6000), TestContext.Current.CancellationToken);

        Assert.Equal(whole.Body, filtered.Body);
        Assert.Equal(2, filtered.Total);
        Assert.Contains("# Ready one", filtered.Body, StringComparison.Ordinal);
    }

    /// <summary>The repository scope is content — which projects the reader is
    /// working in — so, unlike the status chips, it does narrow the body.</summary>
    [Fact]
    public async Task The_repository_scope_narrows_the_body()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");
        await host.WriteEntryAsync("# Mine\n`task` `repo:backlog`\n");
        await host.WriteEntryAsync("# Theirs\n`task` `repo:docs`\n");
        host.State.SetRepositoryFilter("docs");

        var content = await new TasksAiContentSource(host.State).ComposeAsync(new AiContentRequest("which", 6000), TestContext.Current.CancellationToken);

        Assert.Equal(1, content.Total);
        Assert.Contains("# Theirs", content.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("# Mine", content.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_selected_entry_is_pinned_ahead_of_a_better_match()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        await host.WriteEntryAsync("# Sync the sync client sync\n`task` `repo:backlog`\n");
        var plain = await host.WriteEntryAsync("# Water the plants\n`task` `repo:backlog`\n");
        await host.OpenAsync(plain);

        var source = new TasksAiContentSource(host.State);
        var whole = await source.ComposeAsync(new AiContentRequest("sync", 6000), TestContext.Current.CancellationToken);
        var totals = whole.Body.Split('\n')[1];

        var budget = AiContentBudget.HeaderReserve("Tasks", 2) + totals.Length + 1 + plain.RawText.Trim().Length;
        var content = await source.ComposeAsync(new AiContentRequest("sync", budget), TestContext.Current.CancellationToken);

        Assert.True(content.Trimmed);
        Assert.Contains("# Water the plants", content.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("sync client", content.Body, StringComparison.Ordinal);
    }
}
