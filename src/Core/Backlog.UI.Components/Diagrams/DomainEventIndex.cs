using System.Text.RegularExpressions;
using Backlog.UI.Components.Devbook;

namespace Backlog.UI.Components.Diagrams;

/// <summary>What a context listens to from another: a row of its dependency
/// tables in which the other context is the supplier of a published language.</summary>
/// <param name="Context">The supplying context, as the row names it.</param>
/// <param name="Name">The event or contract the row names first in code — <c>ItemTriaged</c> —
/// or null when the mechanism names none of its own.</param>
/// <param name="How">The row's integration mechanism, as plain text.</param>
/// <param name="Why">What the row says the relationship is for, as plain text.</param>
public sealed record DomainEventSubscription(string Context, string? Name, string How, string Why);

/// <summary>One payload field of an event: <c>- `task_id` - task identifier.</c></summary>
public sealed record DomainEventField(string Name, string Meaning);

/// <summary>A consumer as the <em>Consumers</em> list names it: the context, and
/// what the rest of the line says it does with the event.</summary>
public sealed record DomainEventConsumer(string Name, string? What);

/// <summary>
/// The Events tab's index and detail, read out of the chapters as written
/// (local ADR 0037 §5): when an event fires, who consumes it, its payload as
/// rows, and what the context listens to from its dependency tables.
/// </summary>
public static partial class DomainEventIndex
{
    /// <summary>When the event is raised: the summary after its "Published when" —
    /// <em>a Task changes Task Status</em> — or the summary itself.</summary>
    public static string When(DomainEventContract contract)
    {
        var summary = DomainUnits.Plain(contract.Summary).Trim().TrimEnd('.');
        var match = WhenLead().Match(summary);
        return match.Success ? summary[match.Length..].Trim() : summary;
    }

    /// <summary>Each bullet of the <em>Consumers</em> list, its wrapped lines
    /// joined: the name up to its first dash, or a comma before a lower-case word,
    /// and the rest. Empty when the section is prose rather than a list.</summary>
    public static IReadOnlyList<DomainEventConsumer> Consumers(DomainEventContract contract)
    {
        if (contract.Consumers is null) return [];

        var bullets = new List<string>();
        foreach (var raw in contract.Consumers.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal)) bullets.Add(line[2..]);
            else if (line.Length > 0 && bullets.Count > 0) bullets[^1] += " " + line;
        }

        var result = new List<DomainEventConsumer>();
        foreach (var bullet in bullets)
        {
            var text = DomainUnits.Plain(bullet).Trim().TrimEnd('.');
            var split = ConsumerSplit().Match(text);
            if (!split.Success)
            {
                result.Add(new DomainEventConsumer(text, null));
                continue;
            }

            var what = text[(split.Index + split.Length)..].Trim();
            result.Add(new DomainEventConsumer(text[..split.Index].Trim(), what.Length > 0 ? what : null));
        }

        return result;
    }

    /// <summary>The <em>Payload</em> list as rows, or null when a line of it is not
    /// a backticked name and its meaning — then the chapter's list shows as written.</summary>
    public static IReadOnlyList<DomainEventField>? Payload(DomainEventContract contract)
    {
        if (contract.Payload is null) return null;

        // A bullet's wrapped lines are part of it.
        var bullets = new List<string>();
        foreach (var raw in contract.Payload.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal) || bullets.Count == 0) bullets.Add(line);
            else bullets[^1] += " " + line;
        }

        var result = new List<DomainEventField>();
        foreach (var bullet in bullets)
        {
            var match = PayloadLine().Match(bullet);
            if (!match.Success) return null;
            result.Add(new DomainEventField(match.Groups["name"].Value, match.Groups["meaning"].Value.Trim()));
        }

        return result.Count == 0 ? null : result;
    }

    /// <summary>
    /// What the context listens to: every row of <c>dependencies.md</c>'s tables whose
    /// pattern names another context as the supplier of a published language —
    /// <c>OHS + Published Language (Inbox = supplier)</c> — in the order written.
    /// </summary>
    /// <param name="context">The context's name as its map writes it.</param>
    /// <param name="slug">The context's folder, so a table that shortens the name —
    /// <c>(Monitoring = supplier)</c> in <c>monitoring/</c> — is still read as its own.</param>
    public static IReadOnlyList<DomainEventSubscription> Listens(string? dependencies, string context, string? slug = null)
    {
        if (string.IsNullOrWhiteSpace(dependencies)) return [];

        var result = new List<DomainEventSubscription>();
        string[]? header = null;
        foreach (var raw in dependencies.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('|'))
            {
                header = null;
                continue;
            }

            var cells = Cells(line);
            if (header is null)
            {
                header = [.. cells.Select(c => c.ToLowerInvariant())];
                continue;
            }

            if (cells.All(c => Rule().IsMatch(c))) continue;

            var pattern = Cell(header, cells, "pattern");
            var supplier = pattern is null ? null : Supplier().Match(pattern);
            if (supplier is not { Success: true } || !pattern!.Contains("Published Language", StringComparison.OrdinalIgnoreCase)) continue;

            var name = supplier.Groups["name"].Value.Trim();
            if (IsSelf(name, context, slug)) continue;

            var how = Cell(header, cells, "mechanism") ?? string.Empty;
            var why = Cell(header, cells, "why") ?? Cell(header, cells, "relies") ?? string.Empty;
            var published = Publishes().Match(how);
            result.Add(new DomainEventSubscription(
                name,
                published.Success ? published.Groups["name"].Value : null,
                DomainUnits.Plain(how).Trim(),
                DomainUnits.Plain(why).Trim()));
        }

        return result;
    }

    private static bool IsSelf(string name, string context, string? slug) =>
        string.Equals(name, context, StringComparison.OrdinalIgnoreCase)
        || context.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase)
        || (slug is { Length: > 0 } && string.Equals(DomainUnits.Slug(name), slug, StringComparison.OrdinalIgnoreCase));

    private static string? Cell(string[] header, IReadOnlyList<string> cells, string column)
    {
        var index = Array.FindIndex(header, h => h.Contains(column, StringComparison.Ordinal));
        return index >= 0 && index < cells.Count && cells[index].Length > 0 ? cells[index] : null;
    }

    private static List<string> Cells(string line)
    {
        var inner = line.Trim().Trim('|');
        return [.. inner.Split('|').Select(c => c.Trim())];
    }

    [GeneratedRegex(@"^(Published|Raised|Emitted)(\s+by\s+\S+)?\s+(when|after|once)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex WhenLead();

    [GeneratedRegex(@"\s+[—–-](?:\s+|$)|,\s+(?=[a-z])")]
    private static partial Regex ConsumerSplit();

    [GeneratedRegex(@"^[-*]\s+`(?<name>[^`]+)`\s*[—–:-]\s*(?<meaning>.+)$")]
    private static partial Regex PayloadLine();

    [GeneratedRegex(@"\((?<name>[^()=]+?)\s*=\s*supplier\)", RegexOptions.IgnoreCase)]
    private static partial Regex Supplier();

    [GeneratedRegex(@"^Publishes\s+`(?<name>[^`]+)`", RegexOptions.IgnoreCase)]
    private static partial Regex Publishes();

    [GeneratedRegex(@"^:?-{3,}:?$")]
    private static partial Regex Rule();
}
