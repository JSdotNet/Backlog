using System.Text.RegularExpressions;

using Backlog.UI.Components.Knowledge;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <see cref="KnowledgeStatus"/> and the knowledge-meta generator have to agree
/// on what a folder's status words are.
///
/// <para>They are two independent copies of one vocabulary. The generator's
/// <c>STATUS_BY_FOLDER</c> in <c>.github/tools/knowledge-meta/metadata.mjs</c>
/// decides what CI accepts in a <c>meta</c> block; this class decides what the
/// Knowledge panels offer in the chapter editor and which value gets a badge
/// rather than being drawn as an unknown. Nothing connects them — the generator
/// is JavaScript installed from a plugin and this is C# — so a status added on
/// one side is silently missing on the other: a value CI accepts that the editor
/// will not offer, or a value the editor offers that CI rejects on push.</para>
///
/// <para>Which is the same class of defect as issue #241, one layer up. That
/// issue was a chapter carrying <c>status: idea</c>, a word in no folder's
/// vocabulary, going undetected because nothing checked authored values against
/// the tooling's list. <c>tools/knowledge/check-metadata.mjs</c> closes that for
/// the Markdown; this closes it for the C# copy of the same list, which that
/// check cannot see.</para>
///
/// <para>Asserted against the installed generator's source text rather than a
/// list repeated here, because a third copy would only prove this file agrees
/// with itself.</para>
/// </summary>
public class KnowledgeStatusVocabularyTests
{
    /// <summary>The installed generator's copy, by the key it uses for each folder.</summary>
    private static readonly Dictionary<KnowledgeFolder, string> GeneratorKeys = new()
    {
        [KnowledgeFolder.Domain] = "domain",
        [KnowledgeFolder.Arc42] = "arc42",
        [KnowledgeFolder.Backlog] = "backlog",
        [KnowledgeFolder.Tech] = "tech",
        [KnowledgeFolder.Design] = "design"
    };

    private static readonly string[] MetadataModule =
        [".github", "tools", "knowledge-meta", "metadata.mjs"];

    [Fact]
    public void Every_folder_vocabulary_matches_the_generator()
    {
        var generator = StatusByFolder();

        foreach (var (folder, key) in GeneratorKeys)
        {
            Assert.True(
                generator.ContainsKey(key),
                $"metadata.mjs has no STATUS_BY_FOLDER entry for '{key}'. Either the generator "
                + "dropped the folder or the key it uses changed, and this mapping is stale.");

            Assert.True(
                generator[key].SequenceEqual(KnowledgeStatus.Values(folder)),
                $"The {key} status vocabulary has drifted. metadata.mjs allows "
                + $"[{string.Join(", ", generator[key])}] and KnowledgeStatus offers "
                + $"[{string.Join(", ", KnowledgeStatus.Values(folder))}]. One side gained or lost a "
                + "value without the other: a value CI accepts that the chapter editor will not offer, "
                + "or a value the editor offers that CI rejects on push. Change both, in the same order.");
        }
    }

    /// <summary>
    /// The mapping above covers every folder the generator knows.
    ///
    /// <para>Without this, a sixth folder added upstream — the plugin has since
    /// added <c>.ai</c> — would leave the rule above passing on five folders and
    /// silent about the new one.</para>
    /// </summary>
    [Fact]
    public void No_generator_folder_is_left_unchecked()
    {
        var unmapped = StatusByFolder().Keys.Except(GeneratorKeys.Values).ToArray();

        Assert.True(
            unmapped.Length == 0,
            $"metadata.mjs defines a status ladder for [{string.Join(", ", unmapped)}], which this test "
            + "does not map to a KnowledgeFolder. Either the application adopted the folder and needs a "
            + "vocabulary, or it did not and this mapping needs a note saying so.");
    }

    /// <summary>
    /// <c>STATUS_BY_FOLDER</c> as the installed generator declares it: a
    /// folder key per line, each holding a bracketed list of quoted values.
    /// </summary>
    private static Dictionary<string, string[]> StatusByFolder()
    {
        var source = File.ReadAllText(RepositoryRoot.File(MetadataModule));

        var block = Regex.Match(source, @"const STATUS_BY_FOLDER = \{(.*?)\};", RegexOptions.Singleline);
        Assert.True(
            block.Success,
            "Could not find `const STATUS_BY_FOLDER = { ... };` in metadata.mjs. The generator was "
            + "re-synced and declares its status ladders differently now, so this test reads nothing "
            + "and would pass on an empty list.");

        var ladders = Regex.Matches(block.Groups[1].Value, @"(?<folder>[a-z0-9]+):\s*\[(?<values>[^\]]*)\]")
            .ToDictionary(
                match => match.Groups["folder"].Value,
                match => match.Groups["values"].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(value => value.Trim('"'))
                    .ToArray());

        Assert.NotEmpty(ladders);
        return ladders;
    }
}
