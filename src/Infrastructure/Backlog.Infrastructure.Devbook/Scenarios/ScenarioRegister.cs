using System.Text.Json;

namespace Backlog.Infrastructure.Devbook.Scenarios;

/// <summary>
/// The scenario register devbook commits as <c>_meta/scenarios.json</c> (BL1): every
/// <c>type: scenario</c> page in a scope, by stem and path, so a reader lists the
/// journeys without parsing the corpus.
/// <para>
/// Read at its own <c>schemaVersion: 1</c> and nothing else: a register at another
/// version is read as no register, and the caller falls back to finding the pages
/// itself — the same rung the outline's <c>_meta/index.json</c> has under the devbook
/// database. What the register carries beyond the list — setup, labels, parts — is
/// re-read from the page, because the signature is computed from the page text and
/// the page on the branch being shown is the one that has to be current.
/// </para>
/// </summary>
public sealed record ScenarioRegister(IReadOnlyList<ScenarioRegisterEntry> Scenarios)
{
    public const int SchemaVersion = 1;

    /// <summary>The register's file name inside a <c>_meta/</c> folder.</summary>
    public const string FileName = "scenarios.json";

    public static ScenarioRegister? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static ScenarioRegister? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var number)
                || number != SchemaVersion)
            {
                return null;
            }

            var entries = new List<ScenarioRegisterEntry>();
            if (root.TryGetProperty("scenarios", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in list.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object) continue;
                    if (Text(entry, "path") is not { Length: > 0 } path) continue;
                    entries.Add(new ScenarioRegisterEntry(
                        Text(entry, "stem") ?? ScenarioPageParser.StemOf(path),
                        path.Replace('\\', '/'),
                        Text(entry, "title"),
                        Text(entry, "status")));
                }
            }

            return new ScenarioRegister(entries);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <param name="Path">Repository-relative, as the register writes it:
/// <c>.devbook/domain/work/set-up-and-fill-the-backlog.md</c>.</param>
public sealed record ScenarioRegisterEntry(string Stem, string Path, string? Title, string? Status);
