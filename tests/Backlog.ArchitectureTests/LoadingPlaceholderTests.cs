using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// No screen announces a load with an alert.
/// <para>
/// The guidance (.devbook/design/interaction-guidelines.md, Loading States) sends
/// lists, trees and tables to the skeleton and a whole-panel load to the spinner,
/// and never to a sentence. A line of grey text saying "Loading..." on an otherwise
/// empty surface is indistinguishable from a surface that failed to draw, and the
/// library's alert is the component that line kept being written with — six panels
/// had it when this rule arrived.
/// </para>
/// <para>
/// Read off the source rather than off a render, the way
/// <see cref="SharedControlAdoptionTests"/> reads it: the question is what a screen
/// was written to say, and every screen is one file.
/// </para>
/// </summary>
public class LoadingPlaceholderTests
{
    /// <summary>
    /// An alert tag, and its message: a literal, or an interpolation that opens
    /// with a literal. "Reading" is here beside "Loading" because that is the other
    /// way the same placeholder was spelled — the sessions pane's first read and
    /// the atlas both said it — and a rule that only knew one word would have
    /// missed half of them.
    /// </summary>
    private static readonly Regex LoadingAlert = new(
        """<Alert\b[^>]*?\bMessage="(?:@\(\$?")?(?<text>(?:Loading|Reading)\b[^"]*)""",
        RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact]
    public void No_alert_in_a_screen_announces_a_load()
    {
        var screens = Screens().ToList();

        // Against a moved folder making this silently vacuous.
        Assert.NotEmpty(screens);

        var offenders = screens
            .SelectMany(screen =>
            {
                var text = File.ReadAllText(screen.FullName);
                return Offenders(text).Select(offender => $"{Relative(screen)}:{LineOf(text, offender.Index)} \"{offender.Text}\"");
            })
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These screens announce a load with an Alert. Render the library's Skeleton in the shape of the "
            + "list, tree or table that is on its way, or its Spinner for a whole-panel load with no layout "
            + "to preview (.devbook/design/interaction-guidelines.md, Loading States):\n"
            + string.Join('\n', offenders));
    }

    /// <summary>
    /// The rule is a regular expression over markup, and a regular expression that
    /// matched nothing would pass everything. So it is shown the shapes it exists
    /// to catch — the literal and the interpolated message the Devbook panels had,
    /// and the sessions pane's — and the ones it must leave alone.
    /// </summary>
    [Theory]
    [InlineData("""<Alert Message="Loading the architecture area..." BaseClass="devbook-empty" Role="status" />""", true)]
    [InlineData("""<Alert Message="@($"Loading the {Folder.DisplayName} area...")" Role="status" />""", true)]
    [InlineData("""<Alert Message="Reading the sessions on this PC." BaseClass="sessions-panel__empty" Role="status" TestId="sessions-loading" />""", true)]
    [InlineData("<Alert BaseClass=\"devbook-empty\"\n       Message=\"Loading devbook menu...\" />", true)]
    [InlineData("""<Alert Message="@_loadError" Role="alert" ActionLabel="Retry" />""", false)]
    [InlineData("""<Alert Message="Could not read the folder." />""", false)]
    [InlineData("""<Spinner Label="Loading the inbox" />""", false)]
    [InlineData("""<Alert Message="Readings are up to date." Role="status" />""", false)]
    public void The_rule_sees_a_loading_alert_in_every_shape_it_was_written(string markup, bool offends)
    {
        Assert.Equal(offends, Offenders(markup).Any());
    }

    private static IEnumerable<(int Index, string Text)> Offenders(string text) =>
        LoadingAlert.Matches(text).Select(match => (match.Index, match.Groups["text"].Value));

    private static IEnumerable<FileInfo> Screens() =>
        new[] { "Modules", "App" }
            .Select(folder => new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src", folder)))
            .Where(folder => folder.Exists)
            .SelectMany(folder => folder.EnumerateFiles("*.razor", SearchOption.AllDirectories))
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static int LineOf(string text, int index) =>
        text.AsSpan(0, index).Count('\n') + 1;

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/');
}
