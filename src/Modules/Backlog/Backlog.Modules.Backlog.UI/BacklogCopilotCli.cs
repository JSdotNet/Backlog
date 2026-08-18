using Backlog.Infrastructure.Copilot;

namespace Backlog.Desktop.UI.BacklogManagement;

/// <summary>
/// How a backlog entry reads as a task brief for the GitHub Copilot CLI.
/// <para>
/// What the two contexts share is the launcher —
/// <see cref="ICopilotCliLauncher"/>, in <c>Backlog.Infrastructure.Copilot</c>,
/// which takes a prompt and a working directory and starts a process. What they
/// do not share is the prompt: an entry's brief is its markdown, and a knowledge
/// item's is its metadata and summary. So each context writes its own, and
/// <c>Knowledge/KnowledgeCopilotCli</c> is this class's opposite number rather
/// than a caller of it.
/// </para>
/// </summary>
public sealed class BacklogCopilotCli(ICopilotCliLauncher launcher)
{
    public const string UsageAction = "copilot-cli";

    public static BacklogCopilotCli Unavailable { get; } = new(new UnavailableCopilotCliLauncher());

    /// <summary>Hands the entry's markdown to the CLI as the task brief. The
    /// text is passed in rather than an entry: the launcher's whole job is to
    /// start a process with a prompt, and recording that the entry was used is a
    /// separate decision the caller makes through the module.</summary>
    public Task StartFromEntryAsync(
        string rawText,
        string? workingDirectory,
        CancellationToken cancellationToken = default) =>
        launcher.LaunchAsync(
            new CopilotCliRequest(BuildEntryPrompt(rawText), workingDirectory),
            cancellationToken);

    internal static string BuildEntryPrompt(string rawText) =>
        "Work on this Backlog item with GitHub Copilot CLI.\n\n"
        + "Use the item markdown as the task brief and preserve its intent.\n\n"
        + (rawText ?? string.Empty).TrimEnd();
}
