using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// An empty state's title is a fragment: it never ends with a period.
/// <para>
/// The same concept was titled "Nothing here yet." in one pane and "No open work"
/// in the next, so the house style the library's own stories already used — a
/// fragment, with the sentence going in the description — is the one every screen
/// takes. Issue #758 swept the panes that disagreed.
/// </para>
/// <para>
/// Read off the source rather than off a render, the way
/// <see cref="LoadingPlaceholderTests"/> reads it. Every <c>.razor</c> file under
/// <c>src</c> counts — the shared library and the storybook as well as the screens,
/// because the storybook's examples are what the next screen is copied from.
/// A bound title (<c>Title="@_view.Message"</c>) is the caller's and is left alone;
/// a literal, an interpolation, and each literal branch of a conditional are not.
/// </para>
/// </summary>
public class EmptyStateTitleTests
{
    /// <summary>
    /// An empty-state tag's title value, on the tag's first line or a continuation
    /// line: either a plain literal, or an <c>@( ... )</c> expression whose string
    /// literals are read one by one below.
    /// </summary>
    private static readonly Regex EmptyStateTitle = new(
        @"<EmptyState\b[^>]*?\bTitle=""(?:@\((?<expression>(?:[^()]|\((?<depth>)|\)(?<-depth>))*(?(depth)(?!)))\)|(?<literal>[^@""][^""]*))""",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>A string literal inside a title expression, interpolated or not.</summary>
    private static readonly Regex StringLiteral = new(
        @"\$?""(?<text>[^""]*)""",
        RegexOptions.Compiled);

    [Fact]
    public void No_empty_state_title_ends_with_a_period()
    {
        var files = RazorFiles().ToList();

        // Against a moved folder making this silently vacuous.
        Assert.NotEmpty(files);

        var offenders = files
            .SelectMany(file =>
            {
                var text = File.ReadAllText(file.FullName);
                return Offenders(text).Select(offender => $"{Relative(file)}:{LineOf(text, offender.Index)} \"{offender.Text}\"");
            })
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These empty-state titles end with a period. A title is a fragment; drop the period, and put "
            + "a full sentence in the Description instead:\n"
            + string.Join('\n', offenders));
    }

    /// <summary>
    /// The rule is a regular expression over markup, and a regular expression that
    /// matched nothing would pass everything. So it is shown the shapes it exists
    /// to catch — a literal, a literal on a continuation line, an interpolation and
    /// a conditional — and the ones it must leave alone.
    /// </summary>
    [Theory]
    [InlineData("""<EmptyState Title="Nothing here yet." TestId="tasks-empty" />""", true)]
    [InlineData("<EmptyState BaseClass=\"devbook-empty\"\n            Title=\"No arc42 folder here yet.\"\n            role=\"status\">", true)]
    [InlineData("""<EmptyState Title="@($"Nothing in {State.SliceName}.")" />""", true)]
    [InlineData("""<EmptyState Title="@(string.IsNullOrWhiteSpace(Alias) ? "Select a repository." : "No source")" />""", true)]
    [InlineData("""<EmptyState Title="@(string.IsNullOrWhiteSpace(Alias) ? "Select a repository" : "No source.")" />""", true)]
    [InlineData("""<EmptyState Title="Nothing here yet" Description="Capture something to get started." />""", false)]
    [InlineData("""<EmptyState Title="@($"Nothing in {State.SliceName}")" />""", false)]
    [InlineData("""<EmptyState Title="@(string.IsNullOrWhiteSpace(Alias) ? "Select a repository" : "No source")" />""", false)]
    [InlineData("""<EmptyState Title="@_view.Message" role="status" />""", false)]
    [InlineData("""<EmptyState Title="@EmptyTitle" />""", false)]
    [InlineData("""<Alert Title="Could not read the folder." />""", false)]
    public void The_rule_sees_a_period_in_every_shape_a_title_was_written(string markup, bool offends)
    {
        Assert.Equal(offends, Offenders(markup).Any());
    }

    private static IEnumerable<(int Index, string Text)> Offenders(string text) =>
        EmptyStateTitle.Matches(text).SelectMany(match => Titles(match)
            .Where(title => title.TrimEnd().EndsWith('.'))
            .Select(title => (TitleIndex(match), title)));

    /// <summary>Where the title itself sits, which is not the tag's line when it is on a continuation line.</summary>
    private static int TitleIndex(Match match) =>
        match.Groups["literal"].Success ? match.Groups["literal"].Index : match.Groups["expression"].Index;

    private static IEnumerable<string> Titles(Match match) =>
        match.Groups["literal"].Success
            ? [match.Groups["literal"].Value]
            : StringLiteral.Matches(match.Groups["expression"].Value).Select(literal => literal.Groups["text"].Value);

    private static IEnumerable<FileInfo> RazorFiles()
    {
        var src = new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src"));
        return src.Exists
            ? src.EnumerateFiles("*.razor", SearchOption.AllDirectories)
                .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                            && !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            : [];
    }

    private static int LineOf(string text, int index) =>
        text.AsSpan(0, index).Count('\n') + 1;

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/');
}
