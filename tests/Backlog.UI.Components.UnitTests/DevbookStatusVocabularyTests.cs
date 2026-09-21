using System.Text.RegularExpressions;

using Backlog.UI.Components.Devbook;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <see cref="DevbookStatus"/> and the knowledge-meta generator have to agree
/// on what a folder's status words are.
///
/// <para>They are two independent copies of one vocabulary. The generator's
/// <c>STATUS_BY_FOLDER</c> in <c>.github/tools/knowledge-meta/metadata.mjs</c>
/// decides what CI accepts in a <c>meta</c> block; this class decides what the
/// Devbook panels offer in the chapter editor and which value gets a badge
/// rather than being drawn as an unknown. Nothing connects them — the generator
/// is JavaScript installed from a plugin and this is C# — so a status added on
/// one side is silently missing on the other: a value CI accepts that the editor
/// will not offer, or a value the editor offers that CI rejects on push.</para>
///
/// <para>Which is the same class of defect as issue #241, one layer up. That
/// issue was a chapter carrying <c>status: idea</c>, a word in no folder's
/// vocabulary, going undetected because nothing checked authored values against
/// the tooling's list. <c>tools/devbook/check-metadata.mjs</c> closes that for
/// the Markdown; this closes it for the C# copy of the same list, which that
/// check cannot see.</para>
///
/// <para>Asserted against the installed generator's source text rather than a
/// list repeated here, because a third copy would only prove this file agrees
/// with itself.</para>
/// </summary>
public class DevbookStatusVocabularyTests
{
    /// <summary>The installed generator's copy, by the key it uses for each folder.</summary>
    private static readonly Dictionary<DevbookFolder, string> GeneratorKeys = new()
    {
        [DevbookFolder.Domain] = "domain",
        [DevbookFolder.Arc42] = "arc42",
        [DevbookFolder.Backlog] = "backlog",
        [DevbookFolder.Tech] = "tech",
        [DevbookFolder.Design] = "design"
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
                generator[key].SequenceEqual(DevbookStatus.Values(folder)),
                $"The {key} status vocabulary has drifted. metadata.mjs allows "
                + $"[{string.Join(", ", generator[key])}] and DevbookStatus offers "
                + $"[{string.Join(", ", DevbookStatus.Values(folder))}]. One side gained or lost a "
                + "value without the other: a value CI accepts that the chapter editor will not offer, "
                + "or a value the editor offers that CI rejects on push. Change both, in the same order.");
        }
    }

    /// <summary>
    /// <c>.ai</c> is the one folder the application adopted ahead of the installed
    /// generator: the plugin's current <c>metadata.mjs</c> rates it on
    /// <c>.tech</c>'s ladder, but the copy under <c>.github/tools/knowledge-meta/</c>
    /// predates the folder and CLAUDE.md says to re-sync that copy rather than
    /// edit it. Until the re-sync lands, the rule the folder's instructions
    /// state — the same five words as <c>.tech</c> — is what pins the C# list;
    /// once the generator knows the folder, its list takes over, and
    /// <see cref="GeneratorKeys"/> should gain the entry so the general rule
    /// above covers it.
    /// </summary>
    [Fact]
    public void The_ai_vocabulary_matches_the_generator_or_until_it_knows_the_folder_the_tech_ladder()
    {
        var generator = StatusByFolder();

        if (generator.TryGetValue("ai", out var ladder))
        {
            Assert.True(
                ladder.SequenceEqual(DevbookStatus.Values(DevbookFolder.Ai)),
                "The installed generator now knows `.ai`, and its ladder differs from DevbookStatus's. "
                + "Change both, in the same order — and move `ai` into GeneratorKeys so the general rule checks it.");
            return;
        }

        Assert.Equal(DevbookStatus.Values(DevbookFolder.Tech), DevbookStatus.Values(DevbookFolder.Ai));
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
        // `ai` is checked by its own rule above until the generator is re-synced.
        var unmapped = StatusByFolder().Keys.Except(GeneratorKeys.Values).Except(["ai"]).ToArray();

        Assert.True(
            unmapped.Length == 0,
            $"metadata.mjs defines a status ladder for [{string.Join(", ", unmapped)}], which this test "
            + "does not map to a DevbookFolder. Either the application adopted the folder and needs a "
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
