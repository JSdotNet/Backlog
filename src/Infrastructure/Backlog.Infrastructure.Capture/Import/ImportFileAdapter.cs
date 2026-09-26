using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Ports;

namespace Backlog.Infrastructure.Capture.Import;

/// <summary>
/// Answers <see cref="ICaptureSourceAdapter"/> for an import: reads the
/// manifest file each target names and hands every readable item to the run
/// (local ADR 0017).
/// <para>
/// The run does the rest, exactly as it does for a feed. It derives each id
/// with <c>CaptureIds.For(import, …)</c> from the external id this adapter
/// gives — <c>{tool}:{external_id}</c>, the tool first because two tools may
/// spell their ids alike — so importing the same export twice adds nothing and
/// nothing has to remember that it was imported.
/// </para>
/// <para>
/// A file that cannot be opened and an item that cannot be read are notes,
/// the way a feed that could not be fetched is one: the reader of the run's
/// line learns which item to fix, and every other item still comes in. The
/// facts an item carries beyond its text — a content kind, tags, a person, a
/// list — are passed on untouched for the receiving side to apply.
/// </para>
/// </summary>
internal sealed class ImportFileAdapter : ICaptureSourceAdapter
{
    public CaptureSourceKind Kind => CaptureSourceKind.Import;

    public async Task<CaptureSourceFindings> RunAsync(MonitoredSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var entries = new List<CapturedEntry>();
        var notes = new List<string>();
        string? tool = null;

        foreach (var path in source.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = Path.GetFileName(path);
            string text;

            try
            {
                text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                notes.Add(ex is FileNotFoundException or DirectoryNotFoundException
                    ? $"{name} is not there any more, so nothing was imported from it"
                    : $"{name} could not be read: {ex.Message}");
                continue;
            }

            var reading = ImportManifestReader.Read(text);
            notes.AddRange(reading.Problems);

            if (reading.Tool is null) continue;

            tool ??= reading.Tool;
            entries.AddRange(reading.Items.Select(item => Entry(reading.Tool, item)));
        }

        return new CaptureSourceFindings(entries, notes)
        {
            Label = tool is null ? null : $"{CaptureSourceKinds.Label(Kind)} ({tool})"
        };
    }

    private static CapturedEntry Entry(string tool, ImportManifestItem item) =>
        new(
            $"{tool}:{item.ExternalId}",
            item.Title,
            item.Url,
            item.Notes,
            item.CapturedAt,
            new CaptureFacts(item.Kind, item.Tags, item.Person, item.List));
}
