namespace Backlog.ArchitectureTests;

/// <summary>
/// Below 720 px a settings card's header - the open repository editor's and the open
/// account editor's - stacks its title over its actions, and is no taller than that
/// content.
///
/// <para>The header is a wrapping row on a desktop, and its title side carries
/// <c>flex: 1 1 12rem</c> so the text gives way before the buttons do. The narrow rule
/// turns the header into a column, and in a column that 12rem basis is a height: the
/// title side stood 192 px tall for one or two lines of text, which left a gap of that
/// size under the repository's name and made the account editor's header 247 px tall.
/// bUnit brings no layout engine, so the narrow rule is asserted here, against the
/// stylesheet.</para>
/// </summary>
public class SettingsCardHeaderNarrowStylingTests
{
    private const string Narrow = "@media (max-width: 720px)";

    [Fact]
    public void The_narrow_header_stacks_from_the_start()
    {
        var header = CssRules.Effective(NarrowRules, ".repo-card__header");

        Assert.Equal("column", header.GetValueOrDefault("flex-direction"));
        Assert.Equal("flex-start", header.GetValueOrDefault("align-items"));
    }

    /// <summary>In a column, a flex basis is a height: the title side takes the size
    /// of its text instead.</summary>
    [Fact]
    public void The_narrow_title_side_is_as_tall_as_its_text()
    {
        var title = CssRules.Effective(NarrowRules, ".repo-card__header > div");

        Assert.Equal("0 0 auto", title.GetValueOrDefault("flex"));
    }

    [Theory]
    [InlineData("height")]
    [InlineData("min-height")]
    public void The_narrow_header_has_no_fixed_height(string property)
    {
        Assert.False(CssRules.Effective(NarrowRules, ".repo-card__header").ContainsKey(property));
        Assert.False(CssRules.Effective(NarrowRules, ".repo-card__header > div").ContainsKey(property));
    }

    private static string Css => File.ReadAllText(
        Path.Combine(Repository.Root.FullName, "src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css"));

    /// <summary>The bodies of every <c>@media (max-width: 720px)</c> block in the
    /// desktop stylesheet, run together so their rules read as top-level ones.</summary>
    private static string NarrowRules
    {
        get
        {
            var css = Css;
            var bodies = new List<string>();

            for (var at = css.IndexOf(Narrow, StringComparison.Ordinal);
                 at >= 0;
                 at = css.IndexOf(Narrow, at + Narrow.Length, StringComparison.Ordinal))
            {
                var open = css.IndexOf('{', at);
                var depth = 0;

                for (var i = open; i < css.Length; i++)
                {
                    if (css[i] == '{')
                    {
                        depth++;
                    }
                    else if (css[i] == '}' && --depth == 0)
                    {
                        bodies.Add(css[(open + 1)..i]);
                        break;
                    }
                }
            }

            Assert.NotEmpty(bodies);

            return string.Join('\n', bodies);
        }
    }
}
