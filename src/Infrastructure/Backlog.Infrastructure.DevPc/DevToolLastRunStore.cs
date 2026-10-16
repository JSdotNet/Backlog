using System.Text.Json;
using System.Text.Json.Serialization;
using Backlog.Modules.DevPc.Abstractions;

namespace Backlog.Infrastructure.DevPc;

/// <summary>
/// Reads and writes the one file that holds this machine's last tool run,
/// <see cref="DevToolConfigurationPaths.LastRunPath"/>, beside the per-PC tool
/// choices.
///
/// <para>One record, overwritten whole. There is deliberately no history here:
/// the pane shows the last run and nothing before it, and keeping more would be
/// a trend nobody asked for growing in a synced folder.</para>
/// </summary>
internal static class DevToolLastRunStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    /// <summary>
    /// The last run, or <see langword="null"/> when there is none to read.
    ///
    /// <para>A file that is missing, empty, not JSON, or missing what a run has to
    /// carry all read as none. The record is hand-reachable in a synced folder, and
    /// a half-written or hand-mangled one is a card with nothing to say rather than
    /// a pane that cannot open.</para>
    /// </summary>
    public static async Task<DevToolLastRun?> ReadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var stream = File.OpenRead(path);
            await using (stream.ConfigureAwait(false))
            {
                var run = await JsonSerializer.DeserializeAsync<DevToolLastRun>(stream, JsonOptions, ct).ConfigureAwait(false);

                return IsWhole(run) ? run : null;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Replaces the last run with <paramref name="run"/>.
    ///
    /// <para>Through a temp file moved into place, the way the catalog is written:
    /// a write that fails part-way must leave the previous run, not a file that is
    /// neither and that the next read throws away.</para>
    ///
    /// <para>The transcript is redacted on the way in, by
    /// <see cref="DevToolOutput.RedactTranscript"/>: every value in
    /// <paramref name="secrets"/> and every secret shape a CLI is known to print.
    /// Here rather than at each caller, because this is the one door the record
    /// reaches the disk through — and the folder it lands in can be a synced
    /// one.</para>
    /// </summary>
    /// <param name="secrets">Values to mask by value — this machine's MCP token
    /// when it has one.</param>
    public static async Task WriteAsync(string path, DevToolLastRun run, IEnumerable<string?> secrets, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(secrets);

        var redacted = run with { Transcript = DevToolOutput.RedactTranscript(run.Transcript, secrets) };

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);

        var tempPath = path + ".tmp";
        var stream = File.Create(tempPath);
        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, redacted, JsonOptions, ct).ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    /// <inheritdoc cref="WriteAsync(string, DevToolLastRun, IEnumerable{string?}, CancellationToken)"/>
    /// <remarks>No value is known to be secret, so only the shapes are masked.</remarks>
    public static Task WriteAsync(string path, DevToolLastRun run, CancellationToken ct = default) =>
        WriteAsync(path, run, [], ct);

    /// <summary>Whether a deserialized record carries everything a run has. A
    /// positional record fills a missing property with its default rather than
    /// refusing, so the parts nothing could stand in for are checked here.</summary>
    private static bool IsWhole(DevToolLastRun? run) =>
        run is { Steps: not null, Transcript: not null }
        && run.Steps.All(step => step is { Name: not null })
        && Enum.IsDefined(run.Kind)
        && Enum.IsDefined(run.Outcome);
}
