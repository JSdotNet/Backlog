using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// An offer toast stacks its title, its sentence and its button, and fits a phone.
///
/// <para>The roadmap's archive offer (<c>ProjectNoteArchiveOffers</c>) set its title, its
/// sentence and the button <c>Move the project note to the archive</c> side by side in
/// one flex row. Inside the toast's 26rem the button kept its width and the words wrapped
/// one or two to a line into two narrow columns. Asserted against the stylesheet for the
/// reason <c>DraftCheckCircleTests</c> gives: the markup was right, and bUnit brings no
/// layout engine to notice the words had no room.</para>
/// </summary>
public class ToastOfferLayoutTests
{
    [Fact]
    public void An_offer_lays_its_parts_out_in_rows_not_one_line()
    {
        var offer = Rule(Stylesheet(), @"^\.toast--offer\s*\{");

        Assert.Equal("grid", Declaration(offer, "display"));
    }

    [Fact]
    public void An_offers_button_and_words_share_the_first_column()
    {
        var css = Stylesheet();

        foreach (var part in new[] { "title", "message", "action" })
        {
            var rule = Rule(css, $@"^\.toast--offer\s*>\s*\.toast__{part}\b[^{{]*\{{");
            Assert.Equal("1", Declaration(rule, "grid-column"));
        }
    }

    [Fact]
    public void An_offers_dismiss_control_keeps_the_top_right_corner()
    {
        // Without a fixed place it would be auto-placed into the first column, under
        // the button, and the offer would read as having two actions.
        var dismiss = Rule(Stylesheet(), @"^\.toast--offer\s*>\s*\.toast__dismiss\s*\{");

        Assert.Equal("2", Declaration(dismiss, "grid-column"));
        Assert.Equal("1", Declaration(dismiss, "grid-row"));
    }

    [Fact]
    public void A_toast_never_runs_wider_than_the_viewport_leaves_room_for()
    {
        var maxWidth = Declaration(Rule(Stylesheet(), @"^\.toast\s*\{"), "max-width");

        Assert.NotNull(maxWidth);
        Assert.Contains("100vw", maxWidth, StringComparison.Ordinal);
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

        Assert.True(end > 0, $"The rule matching {pattern} in components.css is never closed.");

        return css[opening.Index..(end + 1)];
    }

    private static string Stylesheet() =>
        File.ReadAllText(Path.Combine(
            Repository.Root.FullName, "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css"));
}
