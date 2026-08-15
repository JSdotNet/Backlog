using Backlog.Infrastructure.Copilot;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// Starts the GitHub Copilot CLI from a knowledge item, with the item's metadata
/// and summary as the brief.
/// <para>
/// Second Brain builds its own prompt rather than borrowing Backlog Management's
/// launcher: the two contexts share the <see cref="ICopilotCliLauncher"/> adapter
/// and nothing above it.
/// </para>
/// </summary>
public sealed class KnowledgeCopilotCli(ICopilotCliLauncher launcher)
{
    public async Task StartAsync(
        KnowledgeActionItem item,
        string? workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        await launcher.LaunchAsync(
            new CopilotCliRequest(KnowledgeActionMetadata.BuildPrompt(item), workingDirectory),
            cancellationToken);
    }
}
