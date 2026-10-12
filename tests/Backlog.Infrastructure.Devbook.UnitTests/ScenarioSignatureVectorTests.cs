using System.Text;
using System.Text.Json;

using Backlog.Infrastructure.Devbook.Scenarios;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The page signature against devbook's shared vector,
/// <c>.devbook/_tools/scenarios/scenario-page.vector.json</c>, read from disk.
///
/// <para>The vector is the contract between every reader of a run: devbook's
/// <c>signature.mjs</c> writes the signature into a spec header and a run, and this
/// port recomputes it from the page to decide whether that run is stale. Two
/// implementations that disagree would report every run stale with nothing to show
/// why, so every case is asserted on both the canonical text and the hash. The file
/// is the materialized copy <c>devbook:update</c> refreshes; a release that changes
/// the vector fails here until the port follows.</para>
/// </summary>
public sealed class ScenarioSignatureVectorTests
{
    private static readonly JsonElement Vector = LoadVector();

    public static TheoryData<string> PageCases()
    {
        var data = new TheoryData<string>();
        foreach (var item in Vector.GetProperty("cases").EnumerateArray()) data.Add(item.GetProperty("name").GetString()!);
        return data;
    }

    public static TheoryData<string> DataSetCases()
    {
        var data = new TheoryData<string>();
        foreach (var item in Vector.GetProperty("dataSets").EnumerateArray()) data.Add(item.GetProperty("name").GetString()!);
        return data;
    }

    [Theory]
    [MemberData(nameof(PageCases))]
    public void Every_case_signs_as_the_vector_says(string name)
    {
        var item = Case("cases", name);
        var hashes = item.GetProperty("dataHashes").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
        var page = ScenarioPageParser.Parse(item.GetProperty("markdown").GetString()!, item.GetProperty("path").GetString()!);

        Assert.True(page.IsScenario);
        Assert.Equal(item.GetProperty("canonical").GetString(), ScenarioSignature.CanonicalText(page, n => hashes.GetValueOrDefault(n, ScenarioSignature.MissingData)));
        Assert.Equal(item.GetProperty("signature").GetString(), ScenarioSignature.Of(page, n => hashes.GetValueOrDefault(n, ScenarioSignature.MissingData)));

        if (item.TryGetProperty("sameAs", out var sameAs))
        {
            Assert.Equal(Case("cases", sameAs.GetString()!).GetProperty("signature").GetString(), item.GetProperty("signature").GetString());
        }
    }

    [Theory]
    [MemberData(nameof(DataSetCases))]
    public void Every_data_set_hashes_as_the_vector_says(string name)
    {
        var item = Case("dataSets", name);
        var files = item.GetProperty("files").EnumerateObject().ToDictionary(p => p.Name, p => Encoding.UTF8.GetBytes(p.Value.GetString()!), StringComparer.Ordinal);

        Assert.Equal(item.GetProperty("hash").GetString(), ScenarioSignature.HashFiles(files));
    }

    [Fact]
    public void A_data_set_folder_hashes_its_files_and_a_missing_one_reads_as_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-scenario-data", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "seed"));
            File.WriteAllText(Path.Combine(root, "seed", "items.csv"), "id,title\r\n1,Login\r\n");

            Assert.Equal("56653236", ScenarioSignature.HashDataSet(root));
            Assert.Equal(ScenarioSignature.MissingData, ScenarioSignature.HashDataSet(Path.Combine(root, "absent")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A data entry that is not a plain folder name is hashed as missing, so
    /// a page cannot have the reader walk a folder outside <c>data/</c>.</summary>
    [Theory]
    [InlineData("..")]
    [InlineData("../..")]
    [InlineData("C:/")]
    [InlineData("seed/../..")]
    public void A_data_entry_that_is_not_a_plain_name_is_not_read(string name)
    {
        Assert.False(ScenarioSignature.IsDataSetName(name));
        var page = ScenarioPageParser.Parse($"# P\n\n```meta\ntype: scenario\ndata: [{name}]\n```\n\n## A\n- **Given** x\n", "p.md");

        Assert.Contains($"data: {name}@missing", ScenarioSignature.CanonicalText(page, n => ScenarioSignature.IsDataSetName(n) ? "read" : ScenarioSignature.MissingData), StringComparison.Ordinal);
        Assert.Equal(ScenarioSignature.Of(page), ScenarioSignature.OfPage(page, Path.GetTempPath(), ".devbook/scenarios"));
    }

    [Fact]
    public void An_empty_profile_list_signs_as_the_empty_profile_as_parse_mjs_reads_it()
    {
        var page = ScenarioPageParser.Parse("# P\n\n```meta\ntype: scenario\nprofile: []\n```\n\n## A\n- **Given** x\n", "p.md");

        Assert.Equal("profile: \npart: A\nstep: Given x", ScenarioSignature.CanonicalText(page));
    }

    private static JsonElement Case(string list, string name) =>
        Vector.GetProperty(list).EnumerateArray().Single(item => item.GetProperty("name").GetString() == name);

    private static JsonElement LoadVector()
    {
        var path = Path.Combine(RepositoryRoot.Root.FullName, ".devbook", "_tools", "scenarios", "scenario-page.vector.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }
}
