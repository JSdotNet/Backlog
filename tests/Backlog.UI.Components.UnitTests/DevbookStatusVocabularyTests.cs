using System.Text.RegularExpressions;

using Backlog.SharedKernel.Devbook;
using Backlog.UI.Components.Devbook;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <see cref="DevbookStatus"/> and the devbook-meta generator have to agree
/// on what a folder's status words are.
///
/// <para>They are two independent copies of one vocabulary. The generator's
/// <c>STATUS_BY_FOLDER</c> in <c>.devbook/_tools/devbook-meta/metadata.mjs</c>
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
    /// <summary>The installed generator's copy, by the key it uses for each folder.
    ///
    /// <para><see cref="DevbookFolder.Backlog"/> is not here: the root-level
    /// <c>.backlog</c> folder has no <c>.devbook/</c> successor (local ADR 0016), so
    /// the generator defines no ladder for it and the application's own list is the
    /// only one.</para></summary>
    private static readonly Dictionary<DevbookFolder, string> GeneratorKeys = new()
    {
        [DevbookFolder.Domain] = "domain",
        [DevbookFolder.Arc42] = "arc42",
        [DevbookFolder.Tech] = "tech",
        [DevbookFolder.Design] = "design",
        [DevbookFolder.Ai] = "ai"
    };

    private static readonly string[] MetadataModule =
        [".devbook", "_tools", "devbook-meta", "metadata.mjs"];

    /// <summary>
    /// A folder's whole vocabulary: the words it offers, then the ones it
    /// recognises and never offers. The generator lists <c>domain/</c>'s two
    /// decision rungs on its ladder; <see cref="DevbookStatus"/> keeps them out of
    /// the select, which is why the comparison is against both lists together.
    /// </summary>
    private static string[] Vocabulary(DevbookFolder folder) =>
        [.. DevbookStatus.Values(folder), .. DevbookStatus.RecognisedOnly(folder)];

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
                generator[key].SequenceEqual(Vocabulary(folder)),
                $"The {key} status vocabulary has drifted. metadata.mjs allows "
                + $"[{string.Join(", ", generator[key])}] and DevbookStatus knows "
                + $"[{string.Join(", ", Vocabulary(folder))}]. One side gained or lost a "
                + "value without the other: a value CI accepts that the chapter editor will not offer, "
                + "or a value the editor offers that CI rejects on push. Change both, in the same order.");
        }
    }

    /// <summary>
    /// The mapping above covers every folder the generator knows.
    ///
    /// <para>Without this, a folder added upstream — the way <c>ai/</c> was —
    /// would leave the rule above passing on the others and silent about the new
    /// one.</para>
    /// </summary>
    [Fact]
    public void No_generator_folder_is_left_unchecked()
    {
        var unmapped = StatusByFolder().Keys.Except(GeneratorKeys.Values).ToArray();

        Assert.True(
            unmapped.Length == 0,
            $"metadata.mjs defines a status ladder for [{string.Join(", ", unmapped)}], which this test "
            + "does not map to a DevbookFolder. Either the application adopted the folder and needs a "
            + "vocabulary, or it did not and this mapping needs a note saying so.");
    }

    /// <summary>
    /// <c>STATUS_BY_FOLDER</c> as the installed generator declares it: a
    /// folder key per line, each holding a bracketed list of quoted values or of
    /// names the module declares as <c>const NAME = "value";</c>.
    /// </summary>
    private static Dictionary<string, string[]> StatusByFolder()
    {
        var source = File.ReadAllText(RepositoryRoot.File(MetadataModule));

        var constants = Regex.Matches(source, @"^const (?<name>[A-Z_]+) = ""(?<value>[^""]*)"";", RegexOptions.Multiline)
            .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["value"].Value);

        var block = Regex.Match(source, @"const STATUS_BY_FOLDER = \{(.*?)\};", RegexOptions.Singleline);
        Assert.True(
            block.Success,
            "Could not find `const STATUS_BY_FOLDER = { ... };` in metadata.mjs. The generator was "
            + "refreshed and declares its status ladders differently now, so this test reads nothing "
            + "and would pass on an empty list.");

        var ladders = Regex.Matches(block.Groups[1].Value, @"(?<folder>[a-z0-9]+):\s*\[(?<values>[^\]]*)\]")
            .ToDictionary(
                match => match.Groups["folder"].Value,
                match => match.Groups["values"].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(value => Resolve(value, constants))
                    .ToArray());

        Assert.NotEmpty(ladders);
        return ladders;
    }

    /// <summary>A quoted value as written, or a named constant's value.</summary>
    private static string Resolve(string value, Dictionary<string, string> constants)
    {
        if (value.StartsWith('"'))
        {
            return value.Trim('"');
        }

        Assert.True(
            constants.TryGetValue(value, out var resolved),
            $"STATUS_BY_FOLDER names {value}, which metadata.mjs does not declare as a string constant.");
        return resolved!;
    }
}
