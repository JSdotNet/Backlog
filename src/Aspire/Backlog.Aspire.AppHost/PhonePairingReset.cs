using Aspire.Hosting.ApplicationModel;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backlog.Aspire.AppHost;

/// <summary>
/// Makes <c>mobile-web-harness</c> a phone that has never been paired.
///
/// <para>The harness has no way to do this itself: "Forget this device" lets the
/// credential go but keeps the outbox, which also holds the cached Inbox, the task
/// view and the notes. So the end-to-end suite used to stop the harness, delete
/// those files and start it again by hand. This is that routine as an AppHost
/// resource command (org ADR 0002), so a person in the dashboard, an agent through
/// <c>aspire resource mobile-web-harness forget-pairing</c>, and the suite all run
/// the same steps.</para>
///
/// <para>Everything it removes lives in the harness's own
/// <c>obj/local-development</c>, so unlike <see cref="LocalDataReset"/> it touches
/// this worktree alone and no other session feels it. It still removes named
/// files and the one staging folder rather than the whole directory, which also
/// holds the harness's other per-worktree settings.</para>
/// </summary>
internal static partial class PhonePairingReset
{
    public const string Resource = "mobile-web-harness";

    /// <summary>Where the harness project sits, relative to the AppHost — the path
    /// its registration names — and the folder it writes its state into beside it.</summary>
    private static readonly string[] HarnessLocalDevelopment =
        ["..", "..", "Harness", "Backlog.Mobile.WebHarness", "obj", "local-development"];

    /// <summary>The device credential, and the outbox database with its SQLite
    /// sidecars.</summary>
    private static readonly string[] StatePatterns = ["device-credential.json", "mobile-outbox.db*"];

    /// <summary>Talk notes recorded but not sent yet.</summary>
    private const string StagedTalkNotes = "talk-notes";

    public static string ResolveState(string appHostDirectory) =>
        Path.GetFullPath(Path.Combine([appHostDirectory, .. HarnessLocalDevelopment]));

    public static async Task<ExecuteCommandResult> RunAsync(ExecuteCommandContext context, string state)
    {
        var commands = context.Services.GetRequiredService<ResourceCommandService>();

        // Stopped first: the harness holds the outbox open and keeps the credential
        // in memory, so files removed under a running harness would be written back.
        var stopped = await commands.ExecuteCommandAsync(Resource, KnownResourceCommands.StopCommand, context.CancellationToken);
        if (!stopped.Success)
        {
            return CommandResults.Failure($"Could not stop {Resource}: {stopped.Message} Nothing was removed.");
        }

        var removed = new List<string>();

        try
        {
            var folder = new DirectoryInfo(state);
            if (folder.Exists)
            {
                foreach (var file in StatePatterns.SelectMany(pattern => folder.EnumerateFiles(pattern)).ToList())
                {
                    file.Delete();
                    removed.Add(file.Name);
                }

                var staged = new DirectoryInfo(Path.Combine(folder.FullName, StagedTalkNotes));
                if (staged.Exists)
                {
                    staged.Delete(recursive: true);
                    removed.Add(StagedTalkNotes);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CommandResults.Failure(
                $"Could not reset {state}: {ex.Message} {Resource} is stopped; start it again once the file is free.");
        }

        var started = await commands.ExecuteCommandAsync(Resource, KnownResourceCommands.StartCommand, context.CancellationToken);

        LogPairingForgotten(
            context.Logger,
            Resource,
            removed.Count == 0 ? "nothing" : string.Join(", ", removed));

        return started.Success
            ? CommandResults.Success(removed.Count == 0
                ? $"{Resource} was never paired; restarted it."
                : $"{Resource} is a phone that was never paired — removed {string.Join(", ", removed)}.")
            : CommandResults.Failure($"Removed the pairing, but {Resource} did not start again: {started.Message}");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Forgot the pairing of {Resource}: removed {Files}.")]
    private static partial void LogPairingForgotten(ILogger logger, string resource, string files);
}
