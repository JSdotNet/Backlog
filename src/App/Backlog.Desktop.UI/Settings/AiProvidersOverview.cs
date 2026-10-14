using Backlog.Modules.Dashboard.Abstractions.Services;

// The component's published namespace, not the folder's: see the note at the top
// of Settings.razor for why the two must differ.
namespace Backlog.Desktop.UI.Shell;

/// <summary>The providers the AI providers page lists, in the order it lists them.</summary>
public enum AiProvider
{
    Claude,
    GitHub,
    AzureFoundry
}

/// <summary>
/// One provider as the AI providers page sees it: whether it is set up, how many
/// organizations it reads usage for, and when that usage was last read on this
/// device.
/// </summary>
/// <param name="Organizations">The Claude organizations holding a key, or the GitHub
/// identities usage is read as. Unused for Azure Foundry, which is one resource.</param>
/// <param name="LastRead">From <see cref="IUsageReadStatus"/>; null when never read.</param>
public sealed record AiProviderState(
    AiProvider Provider,
    bool Configured,
    int Organizations = 0,
    UsageReadStatus? LastRead = null);

/// <summary>The line under a tile's figure: when usage was last read, or why the
/// last read failed.</summary>
public sealed record AiProviderReadLine(string Text, bool Failed);

/// <summary>
/// What the AI providers page says about its providers before any card: the
/// headline sentence, each tile's figure and the line under it. Words only, so the
/// page and its tests agree on them without rendering.
/// </summary>
public static class AiProvidersOverview
{
    /// <summary>The headline when no provider on the page is set up.</summary>
    public const string NoneConfigured = "No AI provider is set up yet.";

    /// <summary>The footnote on Azure Foundry's tile while it is not set up.</summary>
    public const string AssistantNeedsFoundry = "The AI assistant needs it";

    /// <summary>The figure on any tile whose provider is not set up.</summary>
    public const string NotSetUp = "Not set up";

    /// <summary>Azure Foundry's figure once it is set up.</summary>
    public const string Ready = "Ready";

    private static readonly string[] Words = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];

    /// <summary>
    /// The page's headline: the configured providers joined and followed by
    /// <c>report usage</c>, then the rest as not set up -
    /// <c>Two Claude organizations and GitHub report usage; Azure Foundry is not set up.</c>
    /// </summary>
    public static string Headline(IReadOnlyList<AiProviderState> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var configured = providers.Where(provider => provider.Configured).ToList();
        var unconfigured = providers.Where(provider => !provider.Configured).ToList();

        if (configured.Count == 0) return NoneConfigured;

        var plural = configured.Count > 1 || configured[0] is { Provider: AiProvider.Claude, Organizations: > 1 };
        var sentence = $"{Join([.. configured.Select(Subject)])} {(plural ? "report" : "reports")} usage";

        if (unconfigured.Count > 0)
        {
            sentence += $"; {Join([.. unconfigured.Select(provider => Name(provider.Provider))])} {(unconfigured.Count > 1 ? "are" : "is")} not set up";
        }

        return Capitalize(sentence) + ".";
    }

    /// <summary>The figure on a provider's tile.</summary>
    public static string Figure(AiProviderState provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (!provider.Configured) return NotSetUp;

        return provider.Provider == AiProvider.AzureFoundry
            ? Ready
            : Organizations(provider.Organizations);
    }

    /// <summary>The tile's label.</summary>
    public static string Label(AiProvider provider) => provider switch
    {
        AiProvider.Claude => "Claude usage",
        AiProvider.GitHub => "GitHub Copilot usage",
        AiProvider.AzureFoundry => "Azure Foundry",
        _ => provider.ToString()
    };

    /// <summary>The line under the figure, or null when usage was never read.</summary>
    public static AiProviderReadLine? ReadLine(UsageReadStatus? status, DateTimeOffset now)
    {
        if (status is null) return null;

        if (status.LastReadFailed)
        {
            return new AiProviderReadLine($"Last read failed: {status.FailureReason ?? "no reason given"}", Failed: true);
        }

        return status.SucceededAt is { } succeeded
            ? new AiProviderReadLine($"Read {Ago(succeeded, now)}", Failed: false)
            : null;
    }

    /// <summary>
    /// One status for a provider read from several sources - Claude, one per
    /// organization. A failure wins while any organization's latest read failed,
    /// and then the most recent failure is the one shown; otherwise the most recent
    /// success. Null when none was ever read.
    /// </summary>
    public static UsageReadStatus? Combine(IEnumerable<UsageReadStatus?> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);

        var read = statuses.OfType<UsageReadStatus>().ToList();
        if (read.Count == 0) return null;

        var failed = read.Where(status => status.LastReadFailed).MaxBy(status => status.FailedAt);
        if (failed is not null) return failed;

        return read.Where(status => status.SucceededAt is not null).MaxBy(status => status.SucceededAt);
    }

    /// <summary>How long ago, the way the In progress screen says it.</summary>
    public static string Ago(DateTimeOffset moment, DateTimeOffset now)
    {
        var elapsed = now - moment;

        if (elapsed < TimeSpan.FromMinutes(1)) return "just now";
        if (elapsed < TimeSpan.FromHours(1)) return $"{(int)elapsed.TotalMinutes} min ago";
        if (elapsed < TimeSpan.FromDays(1)) return $"{(int)elapsed.TotalHours} h ago";

        var days = (int)elapsed.TotalDays;
        return days == 1 ? "yesterday" : $"{days} days ago";
    }

    /// <summary>A count as a word up to nine, as digits past it.</summary>
    public static string Number(int count) =>
        count is >= 0 and < 10 ? Words[count] : count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Organizations(int count) => count == 1 ? "1 organization" : $"{count} organizations";

    private static string Name(AiProvider provider) => provider switch
    {
        AiProvider.Claude => "Claude",
        AiProvider.GitHub => "GitHub",
        AiProvider.AzureFoundry => "Azure Foundry",
        _ => provider.ToString()
    };

    private static string Subject(AiProviderState provider) => provider.Provider == AiProvider.Claude
        ? $"{Number(provider.Organizations)} Claude {(provider.Organizations == 1 ? "organization" : "organizations")}"
        : Name(provider.Provider);

    private static string Join(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => string.Empty,
        1 => parts[0],
        _ => $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}"
    };

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
