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
        Assert.Equal($"Tasks: 1 entry.\n{row.RawText.Trim()}", content.Body);
        Assert.Contains("# Provision the box", content.Body, StringComparison.Ordinal);
    }

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

        var budget = AiContentBudget.HeaderReserve("Tasks", 2) + plain.RawText.Trim().Length;
        var content = await new TasksAiContentSource(host.State).ComposeAsync(new AiContentRequest("sync", budget), TestContext.Current.CancellationToken);

        Assert.True(content.Trimmed);
        Assert.Contains("# Water the plants", content.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("sync client", content.Body, StringComparison.Ordinal);
    }
}
