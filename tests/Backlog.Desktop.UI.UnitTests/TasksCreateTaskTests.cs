namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// <see cref="TasksDesktopState.CreateTaskAsync"/>: the task the Goals view's "New task"
/// row asks the shell for (<c>.devbook/domain/progress/features.md#attaching-work-to-a-goal</c>).
/// It is saved through the module the way any new entry is, seeded as
/// <see cref="TasksDesktopState.NewRow"/> seeds one and with no plan tag, and the row is in
/// the list when the call answers, so the Goals view that reads the list next finds it.
/// A refusal is the module's, handed back, and leaves nothing behind.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TasksCreateTaskTests
{
    [Fact]
    public async Task A_task_is_saved_with_the_title_and_the_new_entry_seeds_and_is_in_the_list_on_return()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var changed = 0;
        host.State.Changed += () => changed++;

        var created = await host.State.CreateTaskAsync("  Bank export  ");

        Assert.True(created.IsSuccess);
        var row = Assert.Single(host.State.Rows, row => row.Id == created.Value);
        Assert.Equal("Bank export", row.PreviewTitle);
        Assert.Equal(EntryType.Task, row.Type);
        Assert.Equal(EntryStatus.Draft, row.PreviewStatus);
        Assert.DoesNotContain(row.PreviewTags, tag => tag.StartsWith('+'));
        Assert.True(changed > 0);

        var stored = Assert.Single(await host.EntriesElsewhere().ListAsync(TestContext.Current.CancellationToken), entry => entry.Id == created.Value);
        Assert.Equal("Bank export", stored.Title);
        Assert.Equal(EntryStatus.Draft, stored.Status);
        Assert.Equal(Priority.Medium, stored.Priority);
        Assert.Contains("`task` `*medium` `!draft`", row.RawText, StringComparison.Ordinal);
        Assert.Empty(row.PreviewRepoIds);
    }

    /// <summary>Typed while a repository is scoped, the task is filed there as
    /// <see cref="TasksDesktopState.NewRow"/> files a new entry, so it stays in the scope it
    /// was typed in.</summary>
    [Fact]
    public async Task A_task_typed_while_a_repository_is_scoped_is_filed_there()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");
        host.State.SetRepositoryFilter("docs");

        var created = await host.State.CreateTaskAsync("Bank export");

        Assert.True(created.IsSuccess);
        var row = Assert.Single(host.State.Rows, row => row.Id == created.Value);
        Assert.Equal(["docs"], row.PreviewRepoIds);
        Assert.Contains("`repo:docs`", row.RawText, StringComparison.Ordinal);
        Assert.Contains(row, host.State.RowsInRepositoryScope);
    }

    /// <summary>The title comes from a one-line search box, and is written as one title
    /// line: text the caller hands in never becomes a step or a second entry.</summary>
    [Fact]
    public async Task A_title_is_written_as_one_line_whatever_it_holds()
    {
        using var host = await TasksPaneHost.CreateAsync();

        var created = await host.State.CreateTaskAsync("Bank\n## export\r\n- [ ] step");

        Assert.True(created.IsSuccess);
        var stored = Assert.Single(await host.EntriesElsewhere().ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Bank ## export - [ ] step", stored.Title);
        Assert.Equal(0, stored.TotalSubItems);
    }

    [Fact]
    public async Task A_title_the_module_refuses_comes_back_as_its_refusal_and_leaves_no_row()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var before = host.State.Rows.Count;

        var created = await host.State.CreateTaskAsync("   ");

        Assert.True(created.IsFailure);
        Assert.False(string.IsNullOrWhiteSpace(created.Error.Message));
        Assert.Equal(before, host.State.Rows.Count);
        Assert.Empty(await host.EntriesElsewhere().ListAsync(TestContext.Current.CancellationToken));
    }
}
