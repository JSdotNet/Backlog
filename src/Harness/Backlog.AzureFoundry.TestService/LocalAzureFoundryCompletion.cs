using System.Text;
using System.Text.RegularExpressions;

namespace Backlog.AzureFoundry.TestService;

/// <summary>
/// The stand-in for a chat deployment: deterministic answers shaped by the
/// prompt alone, so a browser session can exercise every AI path without a
/// key. Two modes, told apart by the system message: a question about content
/// gets an echo of the question and the content, and a plan request gets a
/// plan that Tasks' import can read as it stands.
/// </summary>
public static partial class LocalAzureFoundryCompletion
{
    /// <summary>
    /// The first line of the desktop's plan prompt, the sentence a plan request
    /// is recognised by. The same literal lives in
    /// <c>Backlog.Infrastructure.AzureFoundry.AzureFoundryPlanPrompt.Marker</c>;
    /// it is repeated here rather than referenced because this harness has no
    /// project references by design, and <c>FoundryPlanPromptTests</c> in
    /// Backlog.ArchitectureTests holds the two copies equal.
    /// </summary>
    public const string PlanPromptMarker = "You write Backlog import plans.";

    /// <summary>The longest a request may ask the stand-in to hold its answer.
    /// Past the desktop client's whole budget, so the budget itself can be
    /// walked into from the browser; not unbounded, so a typo cannot park a
    /// request for an hour.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(180);

    /// <summary>
    /// How long the stand-in should take to answer: <c>delay:15s</c> anywhere
    /// in the user message asks for fifteen seconds, nothing asks for none.
    /// A real model takes its time, and the one path this harness could not
    /// exercise before was the slow answer — the desktop client's pipeline once
    /// cut every attempt at ten seconds and stopped the page at thirty, and no
    /// instant answer would ever have shown it.
    /// </summary>
    public static TimeSpan RequestedDelay(IReadOnlyList<AzureFoundryChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var userPrompt = messages.LastOrDefault(message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content;
        if (string.IsNullOrEmpty(userPrompt)) return TimeSpan.Zero;

        var match = DelayMarker().Match(userPrompt);
        if (!match.Success || !int.TryParse(match.Groups["seconds"].Value, out var seconds)) return TimeSpan.Zero;

        var requested = TimeSpan.FromSeconds(seconds);
        return requested > MaxDelay ? MaxDelay : requested;
    }

    [GeneratedRegex(@"\bdelay:(?<seconds>\d{1,3})s\b", RegexOptions.IgnoreCase)]
    private static partial Regex DelayMarker();

    public static string CreateAnswer(IReadOnlyList<AzureFoundryChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var systemPrompt = messages.FirstOrDefault(message => string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))?.Content;
        var userPrompt = messages.LastOrDefault(message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content;

        if (systemPrompt is not null && systemPrompt.StartsWith(PlanPromptMarker, StringComparison.Ordinal))
        {
            return CreatePlan(userPrompt ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            return "Local Azure Foundry test response: no question was provided.";
        }

        var question = ExtractSection(userPrompt, "Question:") ?? userPrompt.Trim();
        var content = ExtractSection(userPrompt, "Content:");
        var contentPreview = CreatePreview(content);

        return contentPreview.Length == 0
            ? $"Local Azure Foundry test response: {question}"
            : $"Local Azure Foundry test response: {question} Based on the supplied content, I found: {contentPreview}";
    }

    /// <summary>
    /// A canned plan in the entry text grammar: two entries per repository the
    /// item names — or one pair with no <c>repo:</c> token when it names none —
    /// the second waiting on the first through <c>after:</c>, every entry
    /// carrying the plan tag and its own <c>id:</c>. The labels it reads
    /// (<c>Item:</c>, <c>Plan tag:</c>, <c>Repositories:</c>) are the ones
    /// <c>AzureFoundryPlanPrompt.User</c> writes.
    /// <para>
    /// Ids are suffixed with the repository's position when there is more than
    /// one, because an id names one entry in a plan and Tasks' import refuses a
    /// document that uses one twice. With a single repository the ids are the
    /// plain <c>step-1</c> and <c>step-2</c>, which is the shape a person reads
    /// back in the browser most of the time.
    /// </para>
    /// </summary>
    private static string CreatePlan(string userPrompt)
    {
        var item = ExtractSection(userPrompt, "Item:") is { Length: > 0 } title ? title : "Inbox item";
        var planTag = ExtractSection(userPrompt, "Plan tag:") is { Length: > 0 } tag ? tag : "inbox-plan";
        // "(none)" is what the prompt writes for an empty list, so a bracketed
        // line is a placeholder rather than a repository.
        var repositories = (ExtractSection(userPrompt, "Repositories:") ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('('))
            .ToList();

        var plan = new StringBuilder();
        var pairs = Math.Max(1, repositories.Count);

        for (var index = 0; index < pairs; index++)
        {
            var repo = repositories.Count == 0 ? null : repositories[index];
            var suffix = repositories.Count > 1 ? $"-{index + 1}" : string.Empty;
            var first = $"step-1{suffix}";
            var second = $"step-2{suffix}";
            var repoToken = repo is null ? string.Empty : $" `repo:{repo}`";

            if (plan.Length > 0) plan.Append('\n');
            plan.Append($"# {item}: step one\n");
            plan.Append($"`prompt` `!draft` `#{planTag}` `id:{first}`{repoToken} `effort:2`\n\n");
            plan.Append($"Backlog plan item {first} of plan {planTag}.\n\n");
            plan.Append($"# {item}: step two\n");
            plan.Append($"`prompt` `!draft` `#{planTag}` `id:{second}` `after:{first}`{repoToken} `effort:3`\n\n");
            plan.Append($"Backlog plan item {second} of plan {planTag}.\n");
        }

        return plan.ToString();
    }

    private static string? ExtractSection(string prompt, string marker)
    {
        var markerIndex = prompt.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (markerIndex < 0)
        {
            return null;
        }

        var sectionStart = markerIndex + marker.Length;
        var nextMarkerIndex = prompt.IndexOf("\n\n", sectionStart, StringComparison.Ordinal);
        var section = nextMarkerIndex < 0
            ? prompt[sectionStart..]
            : prompt[sectionStart..nextMarkerIndex];

        return section.Trim();
    }

    private static string CreatePreview(string? content)
    {
        var normalized = string.Join(' ', (content ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 220 ? normalized : string.Concat(normalized.AsSpan(0, 217), "...");
    }
}
