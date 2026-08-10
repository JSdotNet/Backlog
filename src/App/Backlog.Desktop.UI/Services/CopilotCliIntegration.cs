using Backlog.Infrastructure.Copilot;
using Backlog.Modules.Backlog.DomainModels;

namespace Backlog.Desktop.UI.Services;

/// <summary>
/// Starts the GitHub Copilot CLI from app-owned work items. The launch request is
/// deliberately independent from any one screen so other desktop surfaces can
/// reuse the same prompt contract later.
/// </summary>
public sealed class CopilotCliIntegration(ICopilotCliLauncher launcher)
{
    public const string UsageAction = "copilot-cli";

    public static CopilotCliIntegration Unavailable { get; } = new(new UnavailableCopilotCliLauncher());

    public async Task StartFromEntryAsync(
        BacklogEntry entry,
        string? workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await launcher.LaunchAsync(
            new CopilotCliRequest(BuildEntryPrompt(entry), workingDirectory),
            cancellationToken);

        entry.RecordUsage(UsageAction);
    }

    internal static string BuildEntryPrompt(BacklogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return "Work on this Backlog item with GitHub Copilot CLI.\n\n"
               + "Use the item markdown as the task brief and preserve its intent.\n\n"
               + EntryTextParser.ToRawText(entry).TrimEnd();
    }
}
