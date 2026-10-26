using System.Text.RegularExpressions;

using Backlog.Infrastructure.Devbook.Code;
using Backlog.SharedKernel;
using Backlog.UI.Components.Diagrams.C4;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The Code level of the C4 zoom tab, read for the Devbook (local ADR 0039 §3): which
/// components have code folders, their types read from the source by
/// <see cref="CSharpCodeReader"/>, and the decision records linked to them.
/// <para>
/// The catalog says where the code is — a building block's <c>code</c> field, spelled
/// from the repository root — and the reader says what is there. This puts the two
/// into the <see cref="C4CodeLevel"/> the component library draws, which knows neither.
/// </para>
/// </summary>
public sealed partial class C4DevbookCode(CSharpCodeReader reader, IFolderEditorLauncher launcher)
{
    /// <summary>
    /// What the zoom tab is handed for one catalog: a component has code when its
    /// building block names folders and the folder sits on a repository to read them
    /// from.
    /// </summary>
    /// <param name="openRecord">Opens a decision record's chapter; null leaves the
    /// records as text.</param>
    public C4ZoomCode For(C4Catalog catalog, Func<C4CodeRecord, Task>? openRecord = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return new C4ZoomCode(
            elementId => catalog.RepositoryRoot is not null && catalog.CodeOf(elementId).Count > 0,
            (elementId, token) => LoadAsync(catalog, elementId, token))
        {
            OpenInEditor = catalog.RepositoryRoot is { } root ? path => OpenAsync(root, path) : null,
            OpenRecord = openRecord
        };
    }

    /// <summary>Reads one component's Code level from the source as it is now. On the
    /// pool, because a parse walks every file in the folders.</summary>
    public async Task<C4CodeLevel> LoadAsync(C4Catalog catalog, string componentId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);

        var name = catalog.Workspaces.Select(entry => entry.Workspace.Element(componentId)?.Name).FirstOrDefault(found => found is not null) ?? componentId;
        var folders = catalog.CodeOf(componentId);
        var decisions = Decisions(catalog, componentId);

        if (catalog.RepositoryRoot is not { } root)
        {
            return new C4CodeLevel(name, folders, [], [], decisions)
            {
                Problem = "This architecture folder sits on no repository, so there is no source to read."
            };
        }

        var models = new List<CodeModel>();
        var problems = new List<string>();

        await Task.Run(() =>
        {
            foreach (var folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    models.Add(reader.Read(root, folder));
                }
                catch (ArgumentException exception)
                {
                    problems.Add(exception.Message);
                }
            }
        }, cancellationToken).ConfigureAwait(false);

        var level = Map(name, folders, models, decisions);
        return problems.Count == 0 ? level : level with { Problem = string.Join(" ", problems) };
    }

    /// <summary>Opens a repository-relative file in the editor, or says why not.</summary>
    private async Task<string?> OpenAsync(string root, string path)
    {
        // Asked of the relative path rather than by prefix, so a repository at a drive's
        // root and the platform's own case rule both come out right.
        var full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(root, full);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return $"{path} is not inside the repository.";
        }

        try
        {
            await launcher.OpenFileAsync(full).ConfigureAwait(false);
            return null;
        }
        catch (FolderEditorLaunchException exception)
        {
            return exception.Message;
        }
    }

    // ---- what the component library draws ------------------------------------------

    /// <summary>The types of every folder as one level: a type two folders both
    /// declare is drawn once, as the first read it.</summary>
    public static C4CodeLevel Map(string component, IReadOnlyList<string> folders, IEnumerable<CodeModel> models, IReadOnlyList<C4CodeRecord> decisions)
    {
        ArgumentNullException.ThrowIfNull(models);

        var types = new List<C4CodeType>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var uses = new List<C4CodeUse>();
        var seenUses = new HashSet<C4CodeUse>();

        foreach (var model in models)
        {
            foreach (var type in model.Types)
            {
                if (seen.Add(type.Id)) types.Add(Map(type));
            }

            foreach (var use in model.Uses)
            {
                var drawn = new C4CodeUse(use.From, use.To, use.Kind == CodeUseKind.Inherits);
                if (seenUses.Add(drawn)) uses.Add(drawn);
            }
        }

        return new C4CodeLevel(component, folders, types, uses, decisions);
    }

    public static C4CodeType Map(CodeType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return new C4CodeType(
            type.Id,
            type.Name,
            KindOf(type),
            Stereotype(type),
            type.FilePaths,
            [.. type.PublicMembers.Select(Line)],
            [.. type.Members.Select(Line)]);
    }

    /// <summary>The type as a reader says it, with its modifiers: "static class",
    /// "internal sealed record". Public goes without saying.</summary>
    public static string KindOf(CodeType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var words = new List<string>();
        if (type.Accessibility != CodeAccessibility.Public) words.Add(Word(type.Accessibility));
        words.AddRange(type.Modifiers.Where(modifier => !AccessModifiers.Contains(modifier)));
        words.Add(type.Kind switch
        {
            CodeTypeKind.Struct => "struct",
            CodeTypeKind.Interface => "interface",
            CodeTypeKind.Record => "record",
            CodeTypeKind.RecordStruct => "record struct",
            CodeTypeKind.Enum => "enum",
            CodeTypeKind.Delegate => "delegate",
            _ => "class"
        });

        return string.Join(' ', words);
    }

    /// <summary>The line over the name: «sealed class» : IDisposable.</summary>
    public static string Stereotype(CodeType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var bases = type.BaseList.Count == 0 ? string.Empty : " : " + string.Join(", ", type.BaseList);
        return $"«{KindOf(type)}»{bases}";
    }

    /// <summary>One member line, its visibility in UML's marks: + public, # protected,
    /// ~ internal, - private.</summary>
    public static string Line(CodeMember member)
    {
        ArgumentNullException.ThrowIfNull(member);

        var mark = member.Accessibility switch
        {
            CodeAccessibility.Public => '+',
            CodeAccessibility.Internal => '~',
            CodeAccessibility.Private => '-',
            _ => '#'
        };

        return $"{mark} {member.Signature}";
    }

    /// <summary>The decision records linked to the component, each once, in the order
    /// the chapters were read.</summary>
    public static IReadOnlyList<C4CodeRecord> Decisions(C4Catalog catalog, string componentId)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return [.. catalog.RecordsOf(componentId)
            .Where(link => link.Record.Kind == C4RecordKind.Decision)
            .Select(link => link.Record)
            .DistinctBy(record => C4ViewEntry.FileOf(record.Path), StringComparer.OrdinalIgnoreCase)
            .Select(Record)];
    }

    /// <summary>A record as the panel lists it: "0004" beside "Knowledge index is a
    /// generated database", the number read off the file name or the title.</summary>
    public static C4CodeRecord Record(C4LinkedRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var file = Path.GetFileName(record.Path.Replace('\\', '/').Split('#')[0]);
        var fromFile = FileNumber().Match(file);
        var prefix = TitlePrefix().Match(record.Title);

        var number = fromFile.Success ? fromFile.Groups[1].Value : prefix.Success ? prefix.Groups[1].Value : string.Empty;
        var title = prefix.Success ? record.Title[prefix.Length..].Trim() : record.Title;

        return new C4CodeRecord(number, title, record.Path);
    }

    private static readonly HashSet<string> AccessModifiers = new(StringComparer.Ordinal) { "public", "internal", "protected", "private", "file" };

    private static string Word(CodeAccessibility accessibility) => accessibility switch
    {
        CodeAccessibility.ProtectedInternal => "protected internal",
        CodeAccessibility.PrivateProtected => "private protected",
        _ => accessibility.ToString().ToLowerInvariant()
    };

    [GeneratedRegex(@"^(\d{3,5})-")]
    private static partial Regex FileNumber();

    [GeneratedRegex(@"^\s*(?:ADR|Decision)?\s*(\d{3,5})\s*[:.\-–—]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex TitlePrefix();
}
