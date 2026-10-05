using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Backlog.Infrastructure.Capture.Import;

/// <summary>One item a manifest carries, read and checked.</summary>
/// <param name="ExternalId">The tool's id for the item, or the fallback derived
/// from its title and <c>captured_at</c> when the item gave none.</param>
/// <param name="Title">The <c>#</c> line, trimmed.</param>
/// <param name="CapturedAt">When the item was made in the tool.</param>
/// <param name="Url">The original link, or null.</param>
/// <param name="Kind">The content kind the item states, as written, or null.</param>
/// <param name="Tags">Tag names, bare. Empty for none.</param>
/// <param name="Person">Who shared it, or null.</param>
/// <param name="List">The Inbox List it is to be filed in, by name, or null.</param>
/// <param name="Notes">Everything under the fence, as Markdown, or null.</param>
internal sealed record ImportManifestItem(
    string ExternalId,
    string Title,
    DateTimeOffset CapturedAt,
    string? Url,
    string? Kind,
    IReadOnlyList<string> Tags,
    string? Person,
    string? List,
    string? Notes);

/// <summary>What a manifest turned out to hold.</summary>
/// <param name="Tool">The front matter's <c>tool</c>, or null when the front
/// matter could not be read — in which case there are no items either.</param>
/// <param name="Items">Every item that could be read.</param>
/// <param name="Problems">One note per thing that could not be, each naming the
/// item and saying what to change.</param>
internal sealed record ImportManifestReading(
    string? Tool,
    IReadOnlyList<ImportManifestItem> Items,
    IReadOnlyList<string> Problems);

/// <summary>
/// Reads an inbox import manifest — Markdown with front matter, local ADR 0017;
/// every key's rule is in the generating skill's grammar,
/// <c>plugins/backlog-tools/skills/import-inbox/assets/inbox-import-manifest.md</c>.
/// <para>
/// Never throws for what is in the file. A manifest is often edited by hand
/// before it is imported, so a broken one is the expected case rather than the
/// exceptional one: an item that cannot be read is skipped and named, the rest
/// still come in, and each problem is written as a proofreading note — which
/// item, what is wrong, and what to write instead. Only the front matter can
/// refuse the whole file, because without its <c>tool</c> no item has an
/// identity.
/// </para>
/// <para>
/// Strict where identity is at stake — the title, <c>captured_at</c>, the
/// fence that holds them — and lenient where it is not: a key it does not know
/// is ignored, a blank line inside the fence is skipped, and a <c>url</c> that
/// is not a web address costs the item its link rather than the item.
/// </para>
/// <para>
/// This is not the entry-text grammar and borrows nothing from it (local ADR
/// 0002): the shape is a title, a <c>meta</c> fence and a body, and the keys
/// are capture facts.
/// </para>
/// </summary>
internal static partial class ImportManifestReader
{
    /// <summary>The only manifest version there is.</summary>
    public const string Schema = "1";

    private const string Example = "2026-09-20T08:14:00Z";

    public static ImportManifestReading Read(string text)
    {
        var lines = (text ?? string.Empty).TrimStart('﻿').ReplaceLineEndings("\n").Split('\n');
        var index = 0;

        while (index < lines.Length && lines[index].Trim().Length == 0) index++;

        if (index >= lines.Length)
        {
            return Refused("The file is empty — a manifest starts with a --- line, then schema: 1 and a tool: line, then --- again");
        }

        if (lines[index].Trim() != "---")
        {
            return Refused("The file does not start with front matter — its first line should be ---, then schema: 1 and a tool: line, then --- again");
        }

        var close = Array.FindIndex(lines, index + 1, line => line.Trim() == "---");
        if (close < 0)
        {
            return Refused("The front matter is never closed — add a line with just --- after its tool: line");
        }

        var front = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines[(index + 1)..close])
        {
            if (KeyValue().Match(line) is { Success: true } pair) front[pair.Groups["key"].Value] = pair.Groups["value"].Value.Trim();
        }

        if (!front.TryGetValue("schema", out var schema) || schema.Length == 0)
        {
            return Refused($"The front matter has no schema — add the line schema: {Schema}");
        }

        if (schema != Schema)
        {
            return Refused($"The front matter says schema: {schema}, and this version of Backlog reads schema {Schema} only");
        }

        if (!front.TryGetValue("tool", out var tool) || tool.Length == 0)
        {
            return Refused("The front matter names no tool — add a line like tool: microsoft-todo");
        }

        if (!ToolSlug().IsMatch(tool))
        {
            return Refused($"The front matter's tool \"{tool}\" should be a slug like microsoft-todo — lower-case letters, digits and hyphens");
        }

        var items = new List<ImportManifestItem>();
        var problems = new List<string>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var number = 0;

        for (var at = close + 1; at < lines.Length;)
        {
            if (!IsTitle(lines[at]))
            {
                at++;
                continue;
            }

            var end = at + 1;
            while (end < lines.Length && !IsTitle(lines[end])) end++;

            number++;
            ReadItem(lines, at, end, number, items, problems, seen);
            at = end;
        }

        return new ImportManifestReading(tool, items, problems);
    }

    /// <summary>The id an item without its own is delivered under: the first
    /// sixteen hex digits of the SHA-256 of its title and <c>captured_at</c>,
    /// as written. The generating skill writes the same fallback, so an item
    /// the skill gave one and an item a person wrote without one arrive as the
    /// same capture. As stable as the title: a renamed item comes in again.</summary>
    public static string FallbackId(string title, string capturedAt)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{title}\n{capturedAt}"));
        return "title-sha256:" + Convert.ToHexStringLower(hash)[..16];
    }

    private static void ReadItem(
        string[] lines,
        int start,
        int end,
        int number,
        List<ImportManifestItem> items,
        List<string> problems,
        Dictionary<string, int> seen)
    {
        var title = lines[start].TrimStart()[1..].Trim();
        var name = title.Length == 0 ? $"Item {number}" : $"Item {number} \"{title}\"";

        if (title.Length == 0)
        {
            problems.Add($"{name} has no title — write one after the #");
            return;
        }

        var open = start + 1;
        while (open < end && lines[open].Trim().Length == 0) open++;

        if (open >= end || lines[open].Trim() != "```meta")
        {
            problems.Add($"{name} has no meta block — put a ```meta line right under the title, with external_id and captured_at inside, then a ``` line");
            return;
        }

        var shut = open + 1;
        while (shut < end && lines[shut].Trim() != "```") shut++;

        if (shut >= end)
        {
            problems.Add($"{name}: its meta block is never closed — add a line with just ``` after its last key");
            return;
        }

        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines[(open + 1)..shut])
        {
            if (line.Trim().Length == 0) continue;

            var pair = KeyValue().Match(line);
            if (!pair.Success)
            {
                problems.Add($"{name}: the line \"{line.Trim()}\" in its meta block is not a key: value pair");
                return;
            }

            var key = pair.Groups["key"].Value;
            if (!meta.TryAdd(key, pair.Groups["value"].Value.Trim()))
            {
                problems.Add($"{name} gives {key.ToLowerInvariant()} twice — keep one");
                return;
            }
        }

        if (!meta.TryGetValue("captured_at", out var capturedText) || capturedText.Length == 0)
        {
            problems.Add($"{name} has no captured_at — add one like captured_at: {Example}");
            return;
        }

        if (!DateTimeOffset.TryParse(capturedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var capturedAt))
        {
            problems.Add($"{name}: captured_at \"{capturedText}\" is not a date and time — write it like {Example}");
            return;
        }

        var externalId = Value(meta, "external_id") ?? FallbackId(title, capturedText);

        if (seen.TryGetValue(externalId, out var first))
        {
            problems.Add($"{name} has the same external_id as item {first}, so only item {first} was imported");
            return;
        }

        seen[externalId] = number;

        var url = Value(meta, "url");
        if (url is not null && !IsWebAddress(url))
        {
            problems.Add($"{name}: url \"{url}\" is not a web address, so it came in without its link — a url starts with https://");
            url = null;
        }

        var tags = (Value(meta, "tags") ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.TrimStart('#').Trim())
            .Where(tag => tag.Length > 0)
            .ToList();

        var notes = string.Join('\n', lines[(shut + 1)..end]).Trim('\n', ' ', '\t');

        items.Add(new ImportManifestItem(
            externalId,
            title,
            capturedAt.ToUniversalTime(),
            url,
            Value(meta, "kind"),
            tags,
            Value(meta, "person"),
            Value(meta, "list"),
            notes.Length == 0 ? null : notes));
    }

    private static ImportManifestReading Refused(string problem) => new(null, [], [problem]);

    /// <summary>A line that starts an item: a <c>#</c> and a space, or a bare
    /// <c>#</c>. A <c>##</c> heading is notes.</summary>
    private static bool IsTitle(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed == "#" || trimmed.StartsWith("# ", StringComparison.Ordinal);
    }

    private static string? Value(Dictionary<string, string> meta, string key) =>
        meta.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    private static bool IsWebAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    [GeneratedRegex(@"^\s*(?<key>[A-Za-z][A-Za-z0-9_]*)\s*:(?<value>.*)$")]
    private static partial Regex KeyValue();

    [GeneratedRegex(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex ToolSlug();
}
