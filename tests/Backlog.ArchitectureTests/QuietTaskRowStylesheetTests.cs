using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// What the quiet task row (TaskItem <c>Quiet</c>, local ADR 0062) paints, asserted
/// against the stylesheet for the reason <c>DraftCheckCircleTests</c> gives: bUnit
/// brings no layout engine, so the markup can be right while the look is not.
/// Every value is one of the library's own tokens.
/// </summary>
public class QuietTaskRowStylesheetTests
{
    [Theory]
    [InlineData(@"\.badge--status-inprogress\) \{", "var(--color-text-secondary)")]
    [InlineData(@"\.badge--status-ready \{", "var(--color-band-2)")]
    [InlineData(@"\.badge--status-archived\) \{", "var(--color-text-disabled)")]
    [InlineData(@"\.task-item--blocked-flag[^\n]*\.badge--status \{", "var(--color-error-text)")]
    public void A_quiet_status_is_text_in_its_states_ink(string selectorEnd, string ink)
    {
        var rule = Rule(Stylesheet(), $@"^\.task-item--quiet[^\n]*{selectorEnd}");

        Assert.Equal(ink, Declaration(rule, "color"));
    }

    [Fact]
    public void A_quiet_status_has_no_pill()
    {
        var rule = Rule(Stylesheet(), @"^\.task-item--quiet \.task-item__actions \.badge--status,");

        Assert.Equal("transparent", Declaration(rule, "background"));
        Assert.Equal("0", Declaration(rule, "border"));
        Assert.Equal("none", Declaration(rule, "text-transform"));
    }

    [Fact]
    public void The_copy_and_the_queue_are_26px_borderless_squares()
    {
        var rule = Rule(Stylesheet(), @"^\.task-item--quiet \.task-item__copy-button,\s*\n\.queue-button \{");

        Assert.Equal("26px", Declaration(rule, "width"));
        Assert.Equal("26px", Declaration(rule, "height"));
        Assert.Equal("0", Declaration(rule, "border"));
        Assert.Equal("transparent", Declaration(rule, "background"));
    }

    [Fact]
    public void Queued_is_amber_on_dark_amber()
    {
        var rule = Rule(Stylesheet(), @"^\.queue-button--queued,");

        Assert.Equal("var(--color-warning)", Declaration(rule, "background"));
        Assert.Equal("var(--color-primary)", Declaration(rule, "color"));
    }

    [Fact]
    public void The_quiet_copy_hides_only_where_there_is_hover_and_returns_on_focus()
    {
        var css = Stylesheet();
        var media = Regex.Match(css, @"@media \(hover: hover\) \{\s*\.task-item--quiet \.task-item__copy-button \{\s*opacity: 0;");

        Assert.True(media.Success, "The quiet copy must hide only under (hover: hover).");
        Assert.Contains(".task-item--quiet:focus-within .task-item__copy-button", css, StringComparison.Ordinal);
    }

    private static string? Declaration(string rule, string property)
    {
        var match = Regex.Match(rule, $@"(?:^|[{{;])\s*{Regex.Escape(property)}\s*:\s*(?<value>[^;}}]+)");

        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static string Rule(string css, string pattern)
    {
        var opening = Regex.Match(css, pattern, RegexOptions.Multiline);

        Assert.True(opening.Success, $"components.css has no rule matching {pattern}.");

        var end = css.IndexOf('}', opening.Index);

        return css[opening.Index..(end + 1)];
    }

    private static string Stylesheet() =>
        File.ReadAllText(Path.Combine(
            Repository.Root.FullName, "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css")).ReplaceLineEndings("\n");
}
