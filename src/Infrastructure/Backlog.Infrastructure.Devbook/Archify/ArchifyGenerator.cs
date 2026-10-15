using System.ComponentModel;
using System.Diagnostics;

namespace Backlog.Infrastructure.Devbook.Archify;

/// <summary>One <c>deliver</c> run: which generator, which specification, at
/// which type and quality, into which file, and how long it may take.</summary>
public sealed record ArchifyDelivery(
    string Generator,
    string Type,
    string SpecificationPath,
    string OutputPath,
    string Quality,
    TimeSpan Timeout);

/// <summary>
/// Starts the Archify generator. The seam between deciding what to draw and
/// actually starting a process, so the cache in front of it can be tested with a
/// generator that counts how often it is asked.
/// </summary>
public interface IArchifyGeneratorRunner
{
    /// <summary>Runs <c>deliver</c>, which validates before it writes, and leaves
    /// the artifact at <see cref="ArchifyDelivery.OutputPath"/>. Returns why it
    /// failed, or null; never throws for a failure that is the machine's state
    /// rather than a bug.</summary>
    Task<string?> DeliverAsync(ArchifyDelivery delivery, CancellationToken cancellationToken = default);
}

/// <summary>
/// The generator run with Node, the same command a person would type:
/// <c>node bin/archify.mjs deliver &lt;type&gt; &lt;spec&gt; &lt;out&gt; --quality &lt;q&gt; --json</c>,
/// from the generator's own folder. The same launch
/// <c>ArchifyDeliveryRunDiagrams</c> makes for a delivery run's stages.
/// </summary>
/// <param name="executable">The Node executable, <c>node</c> on the PATH unless a
/// caller names another.</param>
public sealed class NodeArchifyGeneratorRunner(string executable = "node") : IArchifyGeneratorRunner
{
    public async Task<string?> DeliverAsync(ArchifyDelivery delivery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(Path.GetDirectoryName(delivery.Generator))!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in (string[])[delivery.Generator, "deliver", delivery.Type, delivery.SpecificationPath, delivery.OutputPath, "--quality", delivery.Quality, "--json"])
        {
            start.ArgumentList.Add(argument);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(delivery.Timeout);

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Win32Exception)
        {
            return "Node.js is not installed or not on the PATH, so this diagram is drawn without Archify.";
        }

        if (process is null) return "The Archify generator did not start.";

        using (process)
        {
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var error = process.StandardError.ReadToEndAsync(timeout.Token);

                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

                if (process.ExitCode != 0)
                {
                    var detail = (await error.ConfigureAwait(false)).Trim();
                    if (detail.Length == 0) detail = (await output.ConfigureAwait(false)).Trim();

                    return detail.Length == 0
                        ? $"The Archify generator failed with exit code {process.ExitCode}."
                        : $"The Archify generator rejected this specification: {FirstLine(detail)}";
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return $"The Archify generator took longer than {delivery.Timeout.TotalSeconds:0} seconds and was stopped.";
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }
        }

        return File.Exists(delivery.OutputPath) ? null : "The Archify generator finished without writing an artifact.";
    }

    /// <summary>The generator's <c>--json</c> report is one document; its first
    /// error line is what a reader needs, not the whole report.</summary>
    private static string FirstLine(string detail)
    {
        var line = detail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(entry => entry.StartsWith("\"error\"", StringComparison.Ordinal) || entry.StartsWith("- ", StringComparison.Ordinal))
            ?? detail.Split('\n')[0];

        return line.Length > 240 ? line[..240] + "…" : line;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
        }
    }
}
