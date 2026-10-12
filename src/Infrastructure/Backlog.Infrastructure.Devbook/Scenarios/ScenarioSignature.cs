using System.Security.Cryptography;
using System.Text;

namespace Backlog.Infrastructure.Devbook.Scenarios;

/// <summary>
/// The scenario page signature: what a derived spec records in its header and a run
/// in <c>run.json</c>, so a reader can tell a run that proves today's page from one
/// that proves an older text.
/// <para>
/// A port of devbook's <c>signature.mjs</c>. The canonical text, one line each, joined
/// by <c>\n</c> with no trailing newline: <c>start</c>, <c>actor</c>, <c>data</c>
/// (each <c>name@hash</c>) when set; <c>profile</c> always (absent reads as
/// <c>default</c>); <c>flags</c>, <c>settings</c> when set; a <c>shot</c> line per
/// screenshot point above the first part; then per part a <c>part</c> line and, in
/// document order, a <c>step</c> line per step and a <c>shot</c> line per point.
/// sha256 over that text in UTF-8, first 8 hex. Captions, the lead and prose do not
/// count. <c>scenario-page.vector.json</c> is the shared vector every implementation
/// — devbook's, spec-manager's and this one — is tested against.
/// </para>
/// </summary>
public static class ScenarioSignature
{
    /// <summary>What a data set hashes to when its folder does not exist.</summary>
    public const string MissingData = "missing";

    public static string CanonicalText(ScenarioPage page, Func<string, string>? dataHash = null)
    {
        dataHash ??= static _ => MissingData;
        var setup = page.Setup;
        var lines = new List<string>();

        void List(string key, IReadOnlyList<string> values)
        {
            if (values.Count > 0) lines.Add($"{key}: {string.Join(", ", values.Select(ScenarioPageParser.Collapse))}");
        }

        if (!string.IsNullOrEmpty(setup.Start)) lines.Add($"start: {ScenarioPageParser.Collapse(setup.Start)}");
        List("actor", setup.Actor);
        List("data", [.. setup.Data.Select(name => $"{ScenarioPageParser.Collapse(name)}@{dataHash(ScenarioPageParser.Collapse(name))}")]);
        lines.Add($"profile: {ScenarioPageParser.Collapse(setup.Profile ?? "default")}");
        List("flags", setup.Flags);
        List("settings", setup.Settings);

        foreach (var shot in page.Shots.Where(shot => shot.Part is null)) lines.Add($"shot: {shot.Label}");
        foreach (var part in page.Parts)
        {
            lines.Add($"part: {ScenarioPageParser.Collapse(part.Title)}");
            var entries = part.Steps
                .Select(step => (step.Line, Text: $"step: {ScenarioPageParser.Collapse(step.Keyword is not null ? $"{step.Keyword} {step.Text}" : step.Text)}"))
                .Concat(part.Shots.Select(shot => (shot.Line, Text: $"shot: {shot.Label}")))
                .OrderBy(entry => entry.Line);
            lines.AddRange(entries.Select(entry => entry.Text));
        }

        return string.Join("\n", lines);
    }

    /// <summary>The first 8 hex of sha256 over a string's UTF-8.</summary>
    public static string ShortHash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8];

    /// <summary>A parsed page's signature.</summary>
    public static string Of(ScenarioPage page, Func<string, string>? dataHash = null) =>
        ShortHash(CanonicalText(page, dataHash));

    /// <summary>
    /// A data set's content hash: for each file in ordinal POSIX path order, the path, a
    /// newline, the content and a newline, through sha256, first 8 hex. Text content has
    /// CRLF read as LF; content holding a NUL byte is hashed as the bytes it is.
    /// </summary>
    public static string HashFiles(IReadOnlyDictionary<string, byte[]> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var relative in files.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            var bytes = files[relative];
            hash.AppendData(Encoding.UTF8.GetBytes($"{relative}\n"));
            hash.AppendData(Array.IndexOf(bytes, (byte)0) >= 0
                ? bytes
                : Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal)));
            hash.AppendData("\n"u8);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset())[..8];
    }

    /// <summary>A data set folder's hash per <see cref="HashFiles"/>, or
    /// <see cref="MissingData"/> when the folder does not exist.</summary>
    public static string HashDataSet(string folder)
    {
        if (!Directory.Exists(folder)) return MissingData;

        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(
                file => Path.GetRelativePath(folder, file).Replace('\\', '/'),
                File.ReadAllBytes,
                StringComparer.Ordinal);
        return HashFiles(files);
    }

    /// <summary>A page's signature with its data sets hashed from
    /// <c>&lt;repositoryRoot&gt;/&lt;scenarioFolder&gt;/data/&lt;name&gt;/</c>.</summary>
    public static string OfPage(ScenarioPage page, string repositoryRoot, string scenarioFolder)
    {
        var hashes = page.Setup.Data
            .Select(ScenarioPageParser.Collapse)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(name => name, name => IsDataSetName(name) ? HashDataSet(Path.Combine(repositoryRoot, scenarioFolder, "data", name)) : MissingData, StringComparer.Ordinal);
        return Of(page, name => hashes.GetValueOrDefault(name, MissingData));
    }

    /// <summary>
    /// Whether a <c>data</c> entry names a folder under <c>data/</c> and nothing else.
    /// A page is text anybody can push, and an entry written <c>../..</c> or as a
    /// rooted path would have the reader walk and load whatever folder it names; one
    /// that is not a plain name is hashed as missing instead — the run cannot have
    /// imported it either.
    /// </summary>
    public static bool IsDataSetName(string name) =>
        name.Length > 0
        && name.IndexOf(':', StringComparison.Ordinal) < 0
        && !name.StartsWith('/')
        && !name.StartsWith('\\')
        && !Path.IsPathRooted(name)
        // A nested set (`seed/items`) is a folder under data/ as parse.mjs reads it;
        // only a segment that climbs out, or stands still, is refused.
        && name.Split('/', '\\').All(segment => segment.Length > 0 && segment != "." && segment != "..");
}
