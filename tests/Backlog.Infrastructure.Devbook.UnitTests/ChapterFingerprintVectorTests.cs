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
public sealed class ChapterFingerprintVectorTests : IDisposable
{
    private static readonly JsonElement Vector = LoadVector();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "chapter-fingerprint-tests-" + Guid.NewGuid().ToString("N"));

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
                DevbookFingerprint.DeltaTarget(delta.GetProperty("path").GetString()!)!,
                delta.GetProperty("markdown").GetString()!))
            .ToList();

        Assert.Equal(item.GetProperty("canonical").GetString(), DevbookFingerprint.ChangeText(proposal, deltas));
        Assert.Equal(item.GetProperty("fingerprint").GetString(), DevbookFingerprint.Change(proposal, deltas));
    }

    /// <summary>The reader a host uses: the change folder on disk, its proposal
    /// and every delta under <c>devbook-delta/</c>, gives the value the vector
    /// gives for the same files.</summary>
    [Theory]
    [MemberData(nameof(ChangeCases))]
    public void A_change_folder_on_disk_reads_to_the_vectors_fingerprint(string name)
    {
        var item = Case("changes", name);
        var address = item.GetProperty("address").GetString()!;
        Write($"{address}/proposal.md", item.GetProperty("proposal").GetString()!);
        foreach (var delta in item.GetProperty("deltas").EnumerateArray())
        {
            Write(delta.GetProperty("path").GetString()!, delta.GetProperty("markdown").GetString()!);
        }

        Assert.Equal(item.GetProperty("fingerprint").GetString(), DevbookFingerprint.ReadChange(_root, address["openspec/changes/".Length..]));
    }

    [Fact]
    public void A_change_folder_with_no_proposal_has_no_fingerprint() =>
        Assert.Null(DevbookFingerprint.ReadChange(_root, "pay-by-card"));

    /// <summary>
    /// A file saved with a UTF-8 byte order mark keeps it as U+FEFF, as devbook's
    /// <c>readFile(path, "utf8")</c> does. Its first line then starts with U+FEFF,
    /// not <c>#</c>, so devbook reads no heading there; stripping the mark would
    /// read one and hash other text. The expected value is what
    /// <c>node chapter-hash.mjs openspec/changes/bom-case</c> printed for these
    /// exact bytes (without the marks it prints <c>sha256:0e41ce87</c>).
    /// </summary>
    [Fact]
    public void A_change_saved_with_a_byte_order_mark_reads_as_devbook_reads_it()
    {
        byte[] mark = [0xEF, 0xBB, 0xBF];
        WriteBytes("openspec/changes/bom-case/proposal.md", [.. mark, .. Encoding.UTF8.GetBytes("# Proposal\n\nWhy this change.\n")]);
        WriteBytes("openspec/changes/bom-case/devbook-delta/arc42/05-blocks.md", [.. mark, .. Encoding.UTF8.GetBytes("# Building blocks\n\nA block.\n")]);

        Assert.Equal("sha256:34707b9d", DevbookFingerprint.ReadChange(_root, "bom-case"));
    }

    /// <summary>The name is a folder under <c>openspec/changes/</c>, and nothing
    /// else is read: a path, a parent step or the archive is refused before the
    /// disk is.</summary>
    [Theory]
    [InlineData("../outside")]
    [InlineData("nested/pay-by-card")]
    [InlineData(@"nested\pay-by-card")]
    [InlineData("..")]
    [InlineData("archive")]
    public void A_name_that_is_no_change_folder_is_refused(string name)
    {
        Write("openspec/outside/proposal.md", "# Outside\n");

        Assert.Throws<ArgumentException>(() => DevbookFingerprint.ReadChange(_root, name));
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

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder left behind is not a test failure.
        }
    }

    private void Write(string relativePath, string text)
    {
        var path = Path.Combine([_root, .. relativePath.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private void WriteBytes(string relativePath, byte[] bytes)
    {
        var path = Path.Combine([_root, .. relativePath.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>The value the contract names, computed here with nothing from the
    /// code under test: <c>sha256:</c> and the first eight hex characters.</summary>
    private static string Digest(string canonical) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..8];

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
