namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A GitHub account's row keeps its width whether or not its editor is open.
/// <para>
/// The list used <c>repeat(auto-fit, …)</c>. <c>auto-fit</c> collapses the tracks
/// nothing sits in, so with one account and the editor closed the row took the whole
/// list: 844px at a 1280px window. The editor is a grid item spanning every track, so
/// once it opened no track was empty, none collapsed, and the row dropped to one
/// track: 279px, under a card three times its width. The same happened whenever
/// there were fewer accounts than columns. <c>auto-fill</c> keeps the empty tracks,
/// so the row is one track wide in both states.
/// </para>
/// </summary>
public sealed class AccountListLayoutTests
{
    [Fact]
    public void The_account_list_keeps_its_empty_tracks_so_a_row_does_not_reflow_when_the_editor_opens()
    {
        var list = Block(Css(), ".account-list {");

        Assert.Contains(
            "grid-template-columns: repeat(auto-fill, minmax(min(16rem, 100%), 1fr));",
            list,
            StringComparison.Ordinal);
        Assert.DoesNotContain("auto-fit", list, StringComparison.Ordinal);
    }

    [Fact]
    public void The_open_editor_spans_every_track_under_its_row()
    {
        var editor = Block(Css(), ".account-list__editor {");

        Assert.Contains("grid-column: 1 / -1;", editor, StringComparison.Ordinal);
    }

    private static string Css() => File.ReadAllText(
        RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css")).Replace("\r\n", "\n");

    private static string Block(string css, string opening)
    {
        var start = css.IndexOf("\n" + opening, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{opening} should exist.");

        var end = css.IndexOf('}', start);
        Assert.True(end > start, $"{opening} should close.");

        return css[start..(end + 1)];
    }
}
