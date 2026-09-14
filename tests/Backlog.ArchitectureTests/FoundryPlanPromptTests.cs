using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The plan prompt the desktop sends Azure Foundry has two things to stay in
/// step with, and neither is a compile-time reference.
///
/// <para>The first is the import grammar. The model sees nothing but the prompt,
/// so every token the grammar names — the ones under
/// <c>plugins/backlog-tools/skills/backlog-import-plan/assets/backlog-import-grammar.md</c>'s
/// two metadata tables — has to be named in the prompt too, either as a token
/// to write or as one to leave alone. A token added to the grammar and not to
/// the prompt is a plan the model cannot be asked for; one dropped from the
/// grammar and kept in the prompt is a plan the import cannot read.</para>
///
/// <para>The second is the local test service, which keys its canned plan on
/// the prompt's first line. The harness has no project references by design,
/// so it carries that line as its own literal; these hold the two copies equal
/// by reading the source, because an architecture test cannot load either
/// assembly.</para>
/// </summary>
public class FoundryPlanPromptTests
{
    private static readonly string PromptSource = Path.Combine(
        Repository.Root.FullName, "src", "Infrastructure", "Backlog.Infrastructure.AzureFoundry", "AzureFoundryPlanPrompt.cs");

    private static readonly string HarnessSource = Path.Combine(
        Repository.Root.FullName, "src", "Harness", "Backlog.AzureFoundry.TestService", "LocalAzureFoundryCompletion.cs");

    private static readonly string GrammarAsset = Path.Combine(
        Repository.Root.FullName, "plugins", "backlog-tools", "skills", "backlog-import-plan", "assets", "backlog-import-grammar.md");

    /// <summary>A table row whose first cell is a backtick-quoted token: the
    /// sigil rows and the named-token rows. The type row's first cell is
    /// <c>*(none)*</c> and is not a token, so it does not match.</summary>
    private static readonly Regex TokenRow = new(@"^\|\s*`([^`]+)`\s*\|", RegexOptions.Multiline | RegexOptions.Compiled);

    public static TheoryData<string> GrammarTokens
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var token in ReadGrammarTokens()) data.Add(token);
            return data;
        }
    }

    [Fact]
    public void The_grammar_asset_still_names_the_tokens_this_test_reads()
    {
        // The derivation below is only a guard if the tables it reads are
        // still there. Pin the ones the prompt is written around, so a
        // reshaped asset fails here rather than passing an empty theory.
        var tokens = ReadGrammarTokens();

        Assert.Contains("id:", tokens);
        Assert.Contains("after:", tokens);
        Assert.Contains("repo:", tokens);
        Assert.Contains("due:", tokens);
        Assert.Contains("effort:", tokens);
        Assert.Contains("!", tokens);
        Assert.Contains("*", tokens);
        Assert.Contains("@", tokens);
        Assert.Contains("#", tokens);
    }

    [Theory]
    [MemberData(nameof(GrammarTokens))]
    public void Every_grammar_token_is_named_in_the_prompt(string token)
    {
        var text = ReadPromptText();

        // Backtick-quoted, as the prompt writes every token it talks about, so
        // a bare `#` in a sentence or a `*` in prose cannot stand in for one.
        Assert.Contains("`" + token, text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_prompt_opens_with_its_marker()
    {
        var source = File.ReadAllText(PromptSource);

        var declaration = Regex.Match(source, @"public const string Text\s*=\s*(\w+)\s*\+");
        Assert.True(declaration.Success, "AzureFoundryPlanPrompt.Text must be composed from Marker so the marker is its first line.");
        Assert.Equal("Marker", declaration.Groups[1].Value);
    }

    [Fact]
    public void The_harness_marker_literal_equals_the_prompt_marker()
    {
        var promptMarker = ReadLiteral(PromptSource, "Marker");
        var harnessMarker = ReadLiteral(HarnessSource, "PlanPromptMarker");

        Assert.Equal(promptMarker, harnessMarker);
    }

    private static IReadOnlyList<string> ReadGrammarTokens()
    {
        Assert.True(File.Exists(GrammarAsset), $"Grammar asset not found: {GrammarAsset}");

        var tokens = new List<string>();
        foreach (Match row in TokenRow.Matches(File.ReadAllText(GrammarAsset)))
        {
            var cell = row.Groups[1].Value.Trim();
            var colon = cell.IndexOf(':', StringComparison.Ordinal);

            // `id:<slug>` names the token `id:`; a sigil row's cell is the sigil.
            tokens.Add(colon > 0 ? cell[..(colon + 1)] : cell);
        }

        return tokens.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>The body of the <c>Text</c> constant: from its declaration to
    /// the raw literal's closing quotes.</summary>
    private static string ReadPromptText()
    {
        Assert.True(File.Exists(PromptSource), $"Prompt source not found: {PromptSource}");

        var source = File.ReadAllText(PromptSource);
        var start = source.IndexOf("public const string Text", StringComparison.Ordinal);
        Assert.True(start >= 0, "AzureFoundryPlanPrompt.Text not found.");

        var end = source.IndexOf("\"\"\";", start, StringComparison.Ordinal);
        Assert.True(end > start, "AzureFoundryPlanPrompt.Text is not a raw string literal.");

        return source[start..end];
    }

    private static string ReadLiteral(string path, string constant)
    {
        Assert.True(File.Exists(path), $"Source not found: {path}");

        var match = Regex.Match(File.ReadAllText(path), $@"public const string {constant}\s*=\s*""([^""]*)"";");
        Assert.True(match.Success, $"{constant} literal not found in {Path.GetFileName(path)}.");

        return match.Groups[1].Value;
    }
}
