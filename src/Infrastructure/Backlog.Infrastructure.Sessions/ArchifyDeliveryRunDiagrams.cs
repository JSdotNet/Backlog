using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// Runs the vendored Archify generator over a run's specification with Node.
/// <para>
/// A run changes while it runs, so its artifact cannot be authored once and committed
/// the way a chapter's is. What makes generating it on demand affordable is that it is
/// cheap — a quarter of a second for eleven stages — and that most calls are for a run
/// that has not moved since the last one. So the artifact is filed under a hash of its
/// specification, on disk and in memory, and the generator runs only for a
/// specification nobody has rendered yet.
/// </para>
/// <para>
/// Only successes are kept. A failure is usually the machine's state — Node not
/// installed yet, a timeout under load — and remembering it would keep answering "no"
/// after the reason went away.
/// </para>
/// </summary>
internal sealed class ArchifyDeliveryRunDiagrams : IDeliveryRunDiagrams
{
    /// <summary>How many artifacts are kept, in memory and on disk. Each is roughly
    /// 700 KB; a reader with a handful of runs open is the case this serves.</summary>
    private const int Keep = 16;

    private readonly string? _generator;
    private readonly string _cacheDirectory;
    private readonly TimeSpan _timeout;
    private readonly ConcurrentDictionary<string, DeliveryRunDiagram> _rendered = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _order = new();

    /// <param name="generator">Archify's <c>bin/archify.mjs</c>, or null where none
    /// was found — every render then answers with that.</param>
    /// <param name="cacheDirectory">Where specifications and artifacts are written.</param>
    /// <param name="timeout">How long one generator run may take before it is
    /// stopped.</param>
    public ArchifyDeliveryRunDiagrams(string? generator, string cacheDirectory, TimeSpan timeout)
    {
        _generator = generator;
        _cacheDirectory = cacheDirectory;
        _timeout = timeout;
    }

    public async Task<DeliveryRunDiagram> RenderAsync(string specification, CancellationToken cancellationToken = default)
    {
        if (_generator is null || !File.Exists(_generator))
        {
            return DeliveryRunDiagram.Failed("The Archify generator is not part of this installation, so the stages are drawn without it.");
        }

        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(specification)))[..16];

        if (_rendered.TryGetValue(key, out var known)) return known;

        var artifact = Path.Combine(_cacheDirectory, $"run-{key}.html");

        try
        {
            Directory.CreateDirectory(_cacheDirectory);

            if (!File.Exists(artifact))
            {
                var failure = await GenerateAsync(specification, key, artifact, cancellationToken).ConfigureAwait(false);
                if (failure is not null) return DeliveryRunDiagram.Failed(failure);
            }

            var diagram = new DeliveryRunDiagram(await File.ReadAllTextAsync(artifact, cancellationToken).ConfigureAwait(false), artifact, null);
            Remember(key, diagram);

            return diagram;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DeliveryRunDiagram.Failed($"The Archify artifact could not be written: {ex.Message}");
        }
    }

    /// <summary>One generator run: the specification to a file, <c>deliver</c> over
    /// it — which validates before it writes — and the artifact moved into place only
    /// once it is whole, so a reader never loads half of one. Returns why it failed,
    /// or null.</summary>
    private async Task<string?> GenerateAsync(string specification, string key, string artifact, CancellationToken cancellationToken)
    {
        var spec = Path.Combine(_cacheDirectory, $"run-{key}.architecture.json");
        var partial = Path.Combine(_cacheDirectory, $"run-{key}.{Guid.NewGuid():N}.partial.html");

        await File.WriteAllTextAsync(spec, specification, cancellationToken).ConfigureAwait(false);

        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = Path.GetDirectoryName(Path.GetDirectoryName(_generator))!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in (string[])[_generator!, "deliver", "architecture", spec, partial, "--quality", "standard", "--json"])
        {
            start.ArgumentList.Add(argument);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Win32Exception)
        {
            return "Node.js is not installed or not on the PATH, so the stages are drawn without Archify.";
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

                    TryDelete(partial);
                    return detail.Length == 0
                        ? $"The Archify generator failed with exit code {process.ExitCode}."
                        : $"The Archify generator rejected this run: {FirstLine(detail)}";
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                TryDelete(partial);
                return $"The Archify generator took longer than {_timeout.TotalSeconds:0} seconds and was stopped.";
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                TryDelete(partial);
                throw;
            }
        }

        File.Move(partial, artifact, overwrite: true);
        Prune();

        return null;
    }

    private void Remember(string key, DeliveryRunDiagram diagram)
    {
        if (!_rendered.TryAdd(key, diagram)) return;

        _order.Enqueue(key);

        while (_order.Count > Keep && _order.TryDequeue(out var oldest))
        {
            _rendered.TryRemove(oldest, out _);
        }
    }

    /// <summary>Keeps the newest artifacts on disk and drops the rest with their
    /// specifications. The folder is a cache: anything missing is regenerated.</summary>
    private void Prune()
    {
        try
        {
            var stale = new DirectoryInfo(_cacheDirectory)
                .GetFiles("run-*.html")
                .Where(file => !file.Name.EndsWith(".partial.html", StringComparison.Ordinal))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Skip(Keep);

            foreach (var file in stale)
            {
                TryDelete(file.FullName);
                TryDelete(Path.ChangeExtension(file.FullName, ".architecture.json"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A cache that could not be tidied is still a working cache.
        }
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

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Where the generator is: bundled beside the application first, which is how an
    /// installed desktop app carries it, and otherwise <c>tools/archify</c> in the
    /// repository the application was built from — the case for the web harness and
    /// every debug build, which run from a <c>bin</c> folder inside a clone. Null
    /// when neither exists.
    /// </summary>
    public static string? Locate(string baseDirectory)
    {
        var bundled = Path.Combine(baseDirectory, "archify", "bin", "archify.mjs");
        if (File.Exists(bundled)) return bundled;

        for (var directory = new DirectoryInfo(baseDirectory); directory is not null; directory = directory.Parent)
        {
            var vendored = Path.Combine(directory.FullName, "tools", "archify", "bin", "archify.mjs");
            if (File.Exists(vendored)) return vendored;
        }

        return null;
    }
}
