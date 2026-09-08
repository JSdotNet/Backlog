using System.Text.Json;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// The reading order of a knowledge folder, read from the committed
/// <c>_reading-order.json</c> at its root.
///
/// <para>A knowledge folder has an intended reading order that alphabetical sort
/// does not produce: <c>.tech</c> reads shared before desktop, <c>.domain</c>
/// reads inbox before capture. That order used to be written twice — once as an
/// <c>order</c> field in the root document's <c>meta</c> fence, and again in the
/// generated <c>_meta/index.json</c> the fence was derived into — and then, when
/// the fence went, once in the generated index alone. A directory listing is
/// authored, not derived, and ADR 0004 turns the generated half of that pair into
/// a database that is a build output. An authored fact cannot live in a build
/// output, so it moved here: one small committed file per knowledge folder,
/// naming the folder's root document and the order of everything beside it.</para>
///
/// <para>Only the folder's own directory is read, because that is the only level
/// the panes order for themselves — a bounded context's own documents are
/// sequenced by kind, not by this list. The file declares the nested directories
/// too, for the generator that resolves the whole outline; this reader ignores
/// them. The returned names lead with the root document, which a caller for which
/// the root is not one of the things being ordered filters out.</para>
///
/// <para>Every failure returns an empty list rather than throwing: a missing,
/// half-written or hand-mangled file, and — the case worth naming — a
/// <c>version</c> this reader does not know. An unrecognised version is the same
/// answer as no file at all, because guessing at a shape written by newer tooling
/// is how a pane comes to show a confidently wrong order. Callers fall back to
/// their own alphabetical sort, which is a duller pane and not a broken one, and
/// this runs while the user is looking at that pane.</para>
/// </summary>
internal static class KnowledgeReadingOrder
{
    /// <summary>The authored file, at the knowledge folder's root. Deliberately
    /// not under <c>_meta/</c>, which the derived-artifacts convention reserves
    /// for generated output, and underscore-prefixed so every <c>.md</c> walker
    /// in the corpus already skips it.</summary>
    private const string FileName = "_reading-order.json";

    /// <summary>The one <c>version</c> this reader understands.</summary>
    private const int SupportedVersion = 1;

    /// <summary>
    /// The top-level entry names of <paramref name="folderPath"/>, in reading
    /// order, or empty when the folder declares none this reader can read.
    /// </summary>
    public static IReadOnlyList<string> ForFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return [];

        var orderPath = Path.Combine(folderPath, FileName);
        if (!File.Exists(orderPath)) return [];

        try
        {
            using var stream = File.OpenRead(orderPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object) return [];

            if (!root.TryGetProperty("version", out var version)) return [];
            if (version.ValueKind != JsonValueKind.Number) return [];
            if (!version.TryGetInt32(out var declaredVersion)) return [];
            if (declaredVersion != SupportedVersion) return [];

            // The file names the scope it describes, so the lookup below never
            // has to infer one from the path this folder happens to sit at — a
            // knowledge folder is read from a clone, a worktree or a branch
            // snapshot, and only the file knows which scope it is.
            if (!root.TryGetProperty("scope", out var scope)) return [];
            if (scope.ValueKind != JsonValueKind.String) return [];
            if (scope.GetString() is not { Length: > 0 } key) return [];

            if (!root.TryGetProperty("directories", out var directories)) return [];
            if (directories.ValueKind != JsonValueKind.Object) return [];
            if (!directories.TryGetProperty(key, out var declared)) return [];
            if (declared.ValueKind != JsonValueKind.Object) return [];

            var names = new List<string>();

            // The root document always sorts first. A folder that declares none —
            // .arc42, whose numbered chapters need no declaration — declares no
            // order either, and gets the caller's alphabetical sort.
            if (declared.TryGetProperty("root", out var rootDocument)
                && rootDocument.ValueKind == JsonValueKind.String
                && rootDocument.GetString() is { Length: > 0 } rootName)
            {
                names.Add(rootName);
            }

            if (declared.TryGetProperty("order", out var order) && order.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in order.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.String) continue;

                    var value = entry.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) names.Add(value);
                }
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
