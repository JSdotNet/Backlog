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
/// <para>The file declares the nested directories too — <c>.domain/inbox</c>,
/// <c>.arc42/adr</c> — because the generator resolves the whole outline out of
/// it. <see cref="ForFolder"/> answers about the folder's own directory, which is
/// all the three panes ask; <see cref="Read"/> hands back the whole declaration
/// for the one caller that draws every level of it, the menu rail. The returned
/// names lead with the directory's root document, which a caller for which the
/// root is not one of the things being ordered filters out.</para>
///
/// <para>Every failure returns an empty order rather than throwing: a missing,
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
    public static IReadOnlyList<string> ForFolder(string folderPath) =>
        Read(folderPath).ForDirectory(string.Empty);

    /// <summary>
    /// The whole declaration of <paramref name="folderPath"/>, every directory it
    /// names, or <see cref="KnowledgeFolderReadingOrder.Empty"/> when the folder
    /// declares none this reader can read.
    /// </summary>
    public static KnowledgeFolderReadingOrder Read(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return KnowledgeFolderReadingOrder.Empty;

        var orderPath = Path.Combine(folderPath, FileName);
        if (!File.Exists(orderPath)) return KnowledgeFolderReadingOrder.Empty;

        try
        {
            using var stream = File.OpenRead(orderPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object) return KnowledgeFolderReadingOrder.Empty;

            if (!root.TryGetProperty("version", out var version)) return KnowledgeFolderReadingOrder.Empty;
            if (version.ValueKind != JsonValueKind.Number) return KnowledgeFolderReadingOrder.Empty;
            if (!version.TryGetInt32(out var declaredVersion)) return KnowledgeFolderReadingOrder.Empty;
            if (declaredVersion != SupportedVersion) return KnowledgeFolderReadingOrder.Empty;

            // The file names the scope it describes, so the lookup below never
            // has to infer one from the path this folder happens to sit at — a
            // knowledge folder is read from a clone, a worktree or a branch
            // snapshot, and only the file knows which scope it is.
            if (!root.TryGetProperty("scope", out var scope)) return KnowledgeFolderReadingOrder.Empty;
            if (scope.ValueKind != JsonValueKind.String) return KnowledgeFolderReadingOrder.Empty;
            if (scope.GetString() is not { Length: > 0 } key) return KnowledgeFolderReadingOrder.Empty;

            if (!root.TryGetProperty("directories", out var directories)) return KnowledgeFolderReadingOrder.Empty;
            if (directories.ValueKind != JsonValueKind.Object) return KnowledgeFolderReadingOrder.Empty;
            if (!directories.TryGetProperty(key, out _)) return KnowledgeFolderReadingOrder.Empty;

            var declared = new Dictionary<string, KnowledgeDeclaredDirectory>(StringComparer.OrdinalIgnoreCase);

            foreach (var directory in directories.EnumerateObject())
            {
                if (directory.Value.ValueKind != JsonValueKind.Object) continue;
                if (RelativeTo(key, directory.Name) is not { } relative) continue;

                declared[relative] = ReadDirectory(directory.Value);
            }

            return new KnowledgeFolderReadingOrder(declared);
        }
        catch (JsonException)
        {
            return KnowledgeFolderReadingOrder.Empty;
        }
        catch (IOException)
        {
            return KnowledgeFolderReadingOrder.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return KnowledgeFolderReadingOrder.Empty;
        }
    }

    /// <summary>
    /// A declared key as the folder's own callers see it: the scope's own key is
    /// the folder root, and a key beneath it keeps only the part below the scope.
    /// A key belonging to some other scope is not this folder's to order and is
    /// dropped.
    /// </summary>
    private static string? RelativeTo(string scope, string declaredKey)
    {
        if (string.Equals(declaredKey, scope, StringComparison.OrdinalIgnoreCase)) return string.Empty;

        var prefix = scope.EndsWith('/') ? scope : scope + "/";
        return declaredKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? declaredKey[prefix.Length..]
            : null;
    }

    private static KnowledgeDeclaredDirectory ReadDirectory(JsonElement declared)
    {
        // The root document always sorts first. A directory that declares none —
        // .arc42, whose numbered chapters need no declaration — declares no
        // order either, and gets the caller's alphabetical sort.
        var rootDocument = declared.TryGetProperty("root", out var root)
            && root.ValueKind == JsonValueKind.String
            && root.GetString() is { Length: > 0 } rootName
                ? rootName
                : null;

        var names = new List<string>();
        if (rootDocument is not null) names.Add(rootDocument);

        if (declared.TryGetProperty("order", out var order) && order.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in order.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String) continue;

                var value = entry.GetString();
                if (!string.IsNullOrWhiteSpace(value)) names.Add(value);
            }
        }

        return new KnowledgeDeclaredDirectory(rootDocument, names);
    }
}

/// <summary>
/// What one knowledge folder's <c>_reading-order.json</c> declares, keyed by the
/// directory it describes relative to the folder — <c>""</c> for the folder
/// itself, <c>inbox</c> for a bounded context, <c>adr/guidelines</c> for a
/// directory below that.
/// </summary>
internal sealed class KnowledgeFolderReadingOrder(IReadOnlyDictionary<string, KnowledgeDeclaredDirectory> directories)
{
    /// <summary>A folder that declares nothing this reader can use. Every
    /// directory of it is unordered, which is the caller's alphabetical
    /// sort.</summary>
    public static KnowledgeFolderReadingOrder Empty { get; } =
        new(new Dictionary<string, KnowledgeDeclaredDirectory>(StringComparer.OrdinalIgnoreCase));

    /// <summary>The entry names of one directory, root document first, or empty
    /// when it declares no order.</summary>
    public IReadOnlyList<string> ForDirectory(string relativeDirectory) =>
        directories.TryGetValue(relativeDirectory ?? string.Empty, out var declared) ? declared.Names : [];

    /// <summary>The name of a directory's root document — the chapter that
    /// introduces the rest — or <see langword="null"/> when it declares
    /// none.</summary>
    public string? RootDocumentIn(string relativeDirectory) =>
        directories.TryGetValue(relativeDirectory ?? string.Empty, out var declared) ? declared.RootDocument : null;
}

/// <summary>One directory's declaration: its root document, and every entry
/// beside it in reading order with the root leading.</summary>
internal sealed record KnowledgeDeclaredDirectory(string? RootDocument, IReadOnlyList<string> Names);
