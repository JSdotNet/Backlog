namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The open-work report's own stylesheet rules, where they decide whether the
/// dialog fits a narrow window and whether a task in it reads as something to
/// follow.
/// <para>
/// Measured in the harness at about 420px: the plans table's min-content width
/// widened the dialog's body grid, which stretched the tiles past the dialog's
/// edge and scrolled the whole dialog sideways. The body's column is now allowed
/// to be narrower than its content, the table scrolls in its own region, and the
/// tiles wrap two to a row.
/// </para>
/// </summary>
public sealed class OpenWorkReportLayoutTests
{
    [Fact]
    public void The_body_column_never_grows_past_the_dialog()
    {
        var body = Block(Css(), ".open-work-report__body {");

        Assert.Contains("grid-template-columns: minmax(0, 1fr);", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_plans_table_scrolls_inside_its_own_region()
    {
        var css = Css();

        Assert.Contains("overflow-x: auto;", Block(css, ".open-work-report__plans {"), StringComparison.Ordinal);

        // The numbers and the progress phrase stay on one line each: a column of
        // "0 of / 3 points / done" is the cramped table the scroll replaces.
        Assert.Contains("white-space: nowrap;", Block(css, ".open-work-report__table td {"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_breakdowns_never_ask_for_more_than_the_dialog_has()
    {
        var breakdowns = Block(Css(), ".open-work-report__breakdowns {");

        Assert.Contains("minmax(min(18rem, 100%), 1fr)", breakdowns, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tiles_wrap_at_a_narrow_width()
    {
        var dialog = File.ReadAllText(RepositoryRoot.File(
            "src", "Modules", "Tasks", "Backlog.Modules.Tasks.UI", "OpenWorkReportDialog.razor"));

        Assert.Contains("MinColumnWidth=\"8rem\"", dialog, StringComparison.Ordinal);
    }

    /// <summary>The library drops every metric grid to one column below a 40rem
    /// viewport. The report overrides that for its own grid only, so the four
    /// tiles sit two to a row in a narrow window; the library rule is untouched.</summary>
    [Fact]
    public void The_report_tiles_keep_two_columns_in_a_narrow_window()
    {
        var grid = Block(Css(), ".open-work-report .metric-grid {");

        Assert.Contains("repeat(auto-fit, minmax(min(var(--metric-grid-min, 8rem), 100%), 1fr))", grid, StringComparison.Ordinal);

        var library = File.ReadAllText(RepositoryRoot.File(
            "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css"));
        Assert.DoesNotContain("open-work-report", library, StringComparison.Ordinal);
    }

    /// <summary>The title is the primary text and reads as a link — the look a
    /// row's "Waiting for" names have — and the detail is quieter and smaller.
    /// Stacked rather than side by side, so a title that wraps cannot push its
    /// detail off the line it belongs to.</summary>
    [Fact]
    public void A_needs_attention_title_is_link_like_and_its_detail_is_secondary()
    {
        var css = Css();

        var title = Block(css, ".open-work-report__task-title {");
        Assert.Contains("color: var(--color-text-link);", title, StringComparison.Ordinal);
        Assert.Contains("font-size: var(--font-size-sm);", title, StringComparison.Ordinal);
        Assert.Contains("text-decoration: underline dotted;", title, StringComparison.Ordinal);

        var detail = Block(css, ".open-work-report__detail {");
        Assert.Contains("font-size: var(--font-size-xs);", detail, StringComparison.Ordinal);
        Assert.Contains("color: var(--color-text-secondary);", detail, StringComparison.Ordinal);

        var task = Block(css, ".open-work-report__task {");
        Assert.Contains("display: grid;", task, StringComparison.Ordinal);
        Assert.Contains("justify-items: start;", task, StringComparison.Ordinal);
    }

    private static string Css() => File.ReadAllText(
        RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css")).Replace("\r\n", "\n");

    private static string Block(string css, string opening)
    {
        var start = css.IndexOf(opening, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{opening} should exist.");

        var end = css.IndexOf('}', start);
        return css[start..(end + 1)];
    }
}
