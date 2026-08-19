namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The picker's semantics rather than its looks: one selection across two kinds
/// of row, a zero-file range that is still a row you can land on, and arrow keys
/// that actually move the selection.
/// </summary>
public sealed class ChangeScopePickerTests
{
    private static readonly IReadOnlyList<ChangeScope> Scopes =
    [
        new("committed", "Committed", 3),
        new("last-commit", "Last commit", 3),
        new("uncommitted", "Uncommitted", 0)
    ];

    private const string LongSubject = "Run Deploy Foundry on a self-hosted runner";

    private static readonly IReadOnlyList<ChangeCommit> Commits =
    [
        new("6e636df", "6e636df", LongSubject, "36m ago"),
        new("b19f9d1", "b19f9d1", "Merge pull request #134 from the session branch", "2h ago")
    ];

    [Fact]
    public void Exactly_one_row_reports_itself_selected_across_ranges_and_commits()
    {
        using var context = new BunitContext();

        var picker = Render(context, "last-commit");

        // One id space across both groups, because exactly one thing is
        // selected across both. Two selected options would leave the panes
        // beside this one unable to say what they are showing.
        Assert.Single(picker.FindAll("[role=option][aria-selected=true]"));
        Assert.Equal(
            "Last commit",
            picker.Find("[role=option][aria-selected=true] .change-scope__label").TextContent);
    }

    [Fact]
    public void The_range_with_no_files_is_still_focusable_and_still_selectable()
    {
        using var context = new BunitContext();

        string? selected = null;
        var picker = Render(context, "committed", value => selected = value);

        var empty = picker.Find("[data-testid=scope-scope-uncommitted]");

        // The count is the answer: "there are none" answers the reader's
        // question completely. A disabled row would put that fact behind a
        // wall, and disabled means "you may not", which is not true of anything
        // here.
        Assert.False(empty.HasAttribute("disabled"));
        Assert.False(empty.HasAttribute("aria-disabled"));
        Assert.Equal("0 files", empty.QuerySelector(".change-scope__count")!.TextContent);

        empty.Click();

        Assert.Equal("uncommitted", selected);
    }

    [Fact]
    public void A_commit_subject_is_in_the_dom_in_full()
    {
        using var context = new BunitContext();

        var picker = Render(context, "committed");

        // Truncated visually when the row is narrow, never in the DOM, so a
        // screen reader still reads the whole subject — the same bargain
        // .file-view__path makes with a path.
        Assert.Equal(LongSubject, picker.Find(".change-scope__subject").TextContent);
        Assert.Empty(picker.FindAll(".change-scope__row[title]"));
    }

    [Fact]
    public void The_accessible_name_of_a_commit_row_is_sha_then_subject_then_age()
    {
        using var context = new BunitContext();

        var row = Render(context, "committed").Find("[data-testid=scope-commit-6e636df]");

        // Whitespace between the spans is markup formatting; the accessible
        // name a platform computes collapses it, so the test does too.
        Assert.Equal($"6e636df {LongSubject} 36m ago", Flatten(row.TextContent));
    }

    [Fact]
    public void Arrow_keys_move_the_selection_and_wrap_at_the_ends()
    {
        using var context = new BunitContext();

        string? selected = null;
        var picker = Render(context, "committed", value => selected = value);

        picker.Find("[data-testid=scope-scope-committed]").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        // Selection follows focus: a filter that needs a second key to commit
        // makes the keyboard reader do two things where the mouse reader does
        // one, and nothing here is destructive.
        Assert.Equal("last-commit", selected);

        picker.Find("[data-testid=scope-scope-committed]").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        Assert.Equal("b19f9d1", selected);
    }

    [Fact]
    public void Home_and_end_reach_the_first_range_and_the_last_commit()
    {
        using var context = new BunitContext();

        string? selected = null;
        var picker = Render(context, "last-commit", value => selected = value);

        picker.Find("[data-testid=scope-scope-last-commit]").KeyDown(new KeyboardEventArgs { Key = "End" });
        Assert.Equal("b19f9d1", selected);

        picker.Find("[data-testid=scope-scope-last-commit]").KeyDown(new KeyboardEventArgs { Key = "Home" });
        Assert.Equal("committed", selected);
    }

    [Fact]
    public void Exactly_one_row_is_in_the_tab_order()
    {
        using var context = new BunitContext();

        var picker = Render(context, "last-commit");

        // A roving tabindex is the whole reason this is a listbox: five rows
        // with tabindex 0 would be five tab stops between here and the file
        // list beside it.
        Assert.Single(picker.FindAll("[role=option][tabindex='0']"));
    }

    [Fact]
    public void The_tick_slot_is_rendered_on_every_row_whether_or_not_it_is_selected()
    {
        using var context = new BunitContext();

        var picker = Render(context, "committed");

        // Always present, empty when unselected, so selecting never shifts the
        // labels sideways. It is aria-hidden because aria-selected already says
        // the same thing.
        Assert.Equal(5, picker.FindAll(".change-scope__mark").Count);
        Assert.All(
            picker.FindAll(".change-scope__mark"),
            mark => Assert.Equal("true", mark.GetAttribute("aria-hidden")));

        Assert.Equal("✔", picker.Find("[data-testid=scope-scope-committed] .change-scope__mark").TextContent);
        Assert.Equal(string.Empty, picker.Find("[data-testid=scope-scope-uncommitted] .change-scope__mark").TextContent);
    }

    [Fact]
    public void The_commit_group_is_named_by_the_caption_the_eye_sees()
    {
        using var context = new BunitContext();

        var picker = Render(context, "committed");

        var caption = picker.Find(".change-scope__caption");
        var group = picker.Find("[role=group]");

        // The caption separates a computed range from a point in history for
        // the eye, and names the group for everything else — one element doing
        // both, rather than an aria-label with no visible counterpart.
        Assert.Equal("Commits", caption.TextContent);
        Assert.Equal(caption.Id, group.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void With_no_commits_there_is_no_caption_and_no_group()
    {
        using var context = new BunitContext();

        var picker = context.Render<ChangeScopePicker>(parameters => parameters
            .Add(p => p.Scopes, Scopes)
            .Add(p => p.TestId, "scope"));

        Assert.Empty(picker.FindAll(".change-scope__caption"));
        Assert.Empty(picker.FindAll("[role=group]"));
    }

    private static string Flatten(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    private static IRenderedComponent<ChangeScopePicker> Render(
        BunitContext context,
        string? selectedId,
        Action<string?>? onSelected = null) =>
        context.Render<ChangeScopePicker>(parameters => parameters
            .Add(p => p.Scopes, Scopes)
            .Add(p => p.Commits, Commits)
            .Add(p => p.SelectedId, selectedId)
            .Add(p => p.SelectedIdChanged, value => onSelected?.Invoke(value))
            .Add(p => p.TestId, "scope"));
}
