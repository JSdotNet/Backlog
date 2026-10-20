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

    /// <summary>Each line of the <em>Consumers</em> list: the name up to its first
    /// comma or dash, and the rest.</summary>
    public static IReadOnlyList<DomainEventConsumer> Consumers(DomainEventContract contract)
    {
        if (contract.Consumers is null) return [];

        var result = new List<DomainEventConsumer>();
        foreach (var raw in contract.Consumers.Split('\n'))
        {
            var line = raw.Trim();
            if (!(line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))) continue;

            var text = DomainUnits.Plain(line[2..]).Trim().TrimEnd('.');
            var split = ConsumerSplit().Match(text);
            result.Add(split.Success
                ? new DomainEventConsumer(text[..split.Index].Trim(), text[(split.Index + split.Length)..].Trim())
                : new DomainEventConsumer(text, null));
        }

        return result;
    }

    /// <summary>The <em>Payload</em> list as rows, or null when a line of it is not
    /// a backticked name and its meaning — then the chapter's list shows as written.</summary>
    public static IReadOnlyList<DomainEventField>? Payload(DomainEventContract contract)
    {
        if (contract.Payload is null) return null;

        var result = new List<DomainEventField>();
        foreach (var raw in contract.Payload.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var match = PayloadLine().Match(line);
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
    public static IReadOnlyList<DomainEventSubscription> Listens(string? dependencies, string context)
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
            if (string.Equals(name, context, StringComparison.OrdinalIgnoreCase)) continue;

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

    [GeneratedRegex(@"^(Published|Raised|Emitted)\s+(when|after|once)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex WhenLead();

    [GeneratedRegex(@",\s+|\s+[—–-]\s+")]
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
