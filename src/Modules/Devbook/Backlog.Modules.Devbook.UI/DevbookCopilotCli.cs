using Backlog.Infrastructure.Copilot;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Starts the GitHub Copilot CLI from a knowledge item, with the item's metadata
/// and summary as the brief.
/// <para>
/// Devbook builds its own prompt rather than borrowing Tasks'
/// launcher: the two contexts share the <see cref="ICopilotCliLauncher"/> adapter
/// and nothing above it.
/// </para>
/// </summary>
public sealed class DevbookCopilotCli(ICopilotCliLauncher launcher)
{
    public async Task StartAsync(
        DevbookActionItem item,
        string? workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        await launcher.LaunchAsync(
            new CopilotCliRequest(DevbookActionMetadata.BuildPrompt(item), workingDirectory),
            cancellationToken);
    }
}
