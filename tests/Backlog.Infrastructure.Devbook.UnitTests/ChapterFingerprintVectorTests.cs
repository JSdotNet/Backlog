using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Backlog.Infrastructure.Devbook.Fingerprints;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// <c>devbook.chapter.fingerprint@1</c> against devbook's shared vector,
/// <c>chapter-fingerprint.vector.json</c>.
///
/// <para>The fingerprint is what a proposal's approval and acceptance are recorded
/// at, and devbook's <c>chapter-hash.mjs</c>, spec-manager and Backlog each compute
/// it. Three implementations that disagreed would refuse every accept as a
/// <c>fingerprint-mismatch</c> with nothing to show why, so every case is asserted
/// on the canonical text as well as the value, as
/// <see cref="ScenarioSignatureVectorTests"/> does for the journey signature.</para>
///
/// <para>Where the vector is read from: the materialized copy under
/// <c>.devbook/_tools/devbook-meta/</c> when the repository's devbook carries it
/// (contract 38 and later, refreshed by <c>devbook:update</c>), and until then the
/// verbatim copy under <c>tests/Fixtures/devbook-fingerprint/</c>, taken from
/// <c>JSdotNet/devbook@plan/branch-true-devbook</c>,
/// <c>plugins/devbook/tools/devbook-meta/chapter-fingerprint.vector.json</c>. The
/// devbook update that installs the vector retires the copy.</para>
/// </summary>
public sealed class ChapterFingerprintVectorTests
{
    private const string DeltaFolder = "/devbook-delta/";

    private static readonly JsonElement Vector = LoadVector();

    public static TheoryData<string> BlockCases() => Names("blocks");

    public static TheoryData<string> ChangeCases() => Names("changes");

    [Theory]
    [MemberData(nameof(BlockCases))]
    public void Every_block_fingerprints_as_the_vector_says(string name)
    {
        var item = Case("blocks", name);
        var path = item.GetProperty("path").GetString()!;
        var markdown = item.GetProperty("markdown").GetString()!;
        var address = item.GetProperty("address").GetString()!;
        var slug = address.Contains('#', StringComparison.Ordinal) ? address[(address.IndexOf('#', StringComparison.Ordinal) + 1)..] : null;
        var demos = item.TryGetProperty("demos", out var listed)
            ? listed.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal)
            : [];

        Assert.Equal(item.GetProperty("canonical").GetString(), DevbookFingerprint.BlockText(path, markdown, slug, demos.GetValueOrDefault));
        Assert.Equal(item.GetProperty("fingerprint").GetString(), DevbookFingerprint.Block(path, markdown, slug, demos.GetValueOrDefault));
    }

    [Theory]
    [MemberData(nameof(ChangeCases))]
    public void Every_change_fingerprints_as_the_vector_says(string name)
    {
        var item = Case("changes", name);
        var proposal = item.GetProperty("proposal").GetString()!;
        var deltas = item.GetProperty("deltas").EnumerateArray()
            .Select(delta => new DevbookChangeDelta(
                DeltaTarget(delta.GetProperty("path").GetString()!),
                delta.GetProperty("markdown").GetString()!))
            .ToList();

        Assert.Equal(item.GetProperty("canonical").GetString(), DevbookFingerprint.ChangeText(proposal, deltas));
        Assert.Equal(item.GetProperty("fingerprint").GetString(), DevbookFingerprint.Change(proposal, deltas));
    }

    [Fact]
    public void An_address_naming_no_chapter_has_no_fingerprint() =>
        Assert.Null(DevbookFingerprint.Block(".devbook/domain/ordering/domain.md", "# Ordering\n\n## Order\n", "payment"));

    [Fact]
    public void The_cases_that_must_agree_agree_and_the_ones_that_must_differ_differ()
    {
        foreach (var list in new[] { "blocks", "changes" })
        {
            foreach (var item in Vector.GetProperty(list).EnumerateArray())
            {
                var fingerprint = item.GetProperty("fingerprint").GetString();
                Assert.Equal(Digest(item.GetProperty("canonical").GetString()!), fingerprint);

                if (item.TryGetProperty("sameAs", out var sameAs))
                {
                    Assert.Equal(Case(list, sameAs.GetString()!).GetProperty("fingerprint").GetString(), fingerprint);
                }

                if (item.TryGetProperty("differsFrom", out var differsFrom))
                {
                    Assert.NotEqual(Case(list, differsFrom.GetString()!).GetProperty("fingerprint").GetString(), fingerprint);
                }
            }
        }
    }

    /// <summary>The value the contract names, computed here with nothing from the
    /// code under test: <c>sha256:</c> and the first eight hex characters.</summary>
    private static string Digest(string canonical) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..8];

    /// <summary>The devbook file a delta targets: its path under the change's
    /// <c>devbook-delta/</c>, re-rooted at <c>.devbook/</c>.</summary>
    private static string DeltaTarget(string changePath) =>
        ".devbook/" + changePath[(changePath.IndexOf(DeltaFolder, StringComparison.Ordinal) + DeltaFolder.Length)..];

    private static TheoryData<string> Names(string list)
    {
        var data = new TheoryData<string>();
        foreach (var item in Vector.GetProperty(list).EnumerateArray()) data.Add(item.GetProperty("name").GetString()!);
        return data;
    }

    private static JsonElement Case(string list, string name) =>
        Vector.GetProperty(list).EnumerateArray().Single(item => item.GetProperty("name").GetString() == name);

    private static JsonElement LoadVector()
    {
        var installed = Path.Combine(RepositoryRoot.Root.FullName, ".devbook", "_tools", "devbook-meta", "chapter-fingerprint.vector.json");
        var path = File.Exists(installed)
            ? installed
            : Path.Combine(RepositoryRoot.Root.FullName, "tests", "Fixtures", "devbook-fingerprint", "chapter-fingerprint.vector.json");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }
}
