using System.Text.Json;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// The reading order of a knowledge folder, read from the <c>_meta/index.json</c>
/// the knowledge-meta generator commits beside it.
///
/// <para>A knowledge folder has an intended reading order that alphabetical sort
/// does not produce: <c>.tech</c> reads shared before desktop, <c>.domain</c>
/// reads inbox before capture. That order used to be written twice — once as an
/// <c>order</c> field in the root document's <c>meta</c> fence, and again in the
/// generated index the fence was derived into. It is now declared once, here, in
/// the index: it is a directory listing rather than metadata about a chapter, and
/// the panes were already reading the folder that the index describes.</para>
///
/// <para>Only the top level of a folder is exposed, because that is the only level
/// the panes order for themselves — a bounded context's own documents are
/// sequenced by kind, not by this list. The returned names include the root
/// document, which the index always lists first; a caller for which the root is
/// not one of the things being ordered filters it out.</para>
///
/// <para>Every failure returns an empty list rather than throwing. A missing,
/// half-written or hand-mangled index means the caller falls back to its own
/// alphabetical order, which is a duller pane and not a broken one — and this
/// runs while the user is looking at that pane.</para>
/// </summary>
internal static class KnowledgeReadingOrder
{
    /// <summary>
    /// The top-level entry names of <paramref name="folderPath"/>, in reading
    /// order, or empty when the folder has no readable index.
    /// </summary>
    public static IReadOnlyList<string> ForFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return [];

        var indexPath = Path.Combine(folderPath, "_meta", "index.json");
        if (!File.Exists(indexPath)) return [];

        try
        {
            using var stream = File.OpenRead(indexPath);
            using var document = JsonDocument.Parse(stream);

            if (document.RootElement.ValueKind != JsonValueKind.Object) return [];
            if (!document.RootElement.TryGetProperty("entries", out var entries)) return [];
            if (entries.ValueKind != JsonValueKind.Array) return [];

            var names = new List<string>();
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                if (!entry.TryGetProperty("name", out var name)) continue;
                if (name.ValueKind != JsonValueKind.String) continue;

                var value = name.GetString();
                if (!string.IsNullOrWhiteSpace(value)) names.Add(value);
            }

            return names;
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
