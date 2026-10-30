namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// Which chapters are workflow subjects, so the panels that draw a state and the
/// MCP tools that set one agree on it.
/// </summary>
public static class DevbookSubjects
{
    private static readonly string[] IndexFiles = ["README.md", "index.md"];

    /// <summary>A kind's name in the contract — <c>proposal</c>, <c>decision</c>,
    /// <c>debt</c> — which is also how every file and wire format Backlog writes
    /// spells it, so a state reads the same in the store's file, on the wire and
    /// over MCP.</summary>
    public static string KindName(DevbookSubjectKind kind) => kind switch
    {
        DevbookSubjectKind.Proposal => "proposal",
        DevbookSubjectKind.Decision => "decision",
        DevbookSubjectKind.Debt => "debt",
    };

    /// <summary>The kind a contract name stands for. Exact and lower-case, as the
    /// contract writes it: a name it does not write is no kind.</summary>
    public static bool TryParseKind(string? name, out DevbookSubjectKind kind)
    {
        foreach (var candidate in Enum.GetValues<DevbookSubjectKind>())
        {
            if (string.Equals(KindName(candidate), name, StringComparison.Ordinal))
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }

    /// <summary>
    /// The subject kind a chapter path is, or null for an ordinary chapter.
    /// <para>
    /// A decision record is a Markdown file directly in <c>arc42/adr/</c>, a debt
    /// record one directly in <c>arc42/tdr/</c>, in either layout — the
    /// <c>.devbook/arc42/…</c> one and the legacy root <c>.arc42/…</c>, as
    /// <c>Arc42DevbookCatalog.IsDecisionRecord</c> reads them. Directly, because
    /// <c>adr/guidelines/</c> holds the team's coding guidelines, which are
    /// numbered like records but are in force once merged rather than decided in a
    /// pull request; and the folder's README or index is the list of records, not
    /// one. A proposal is no chapter path at all — it is a change folder — so it
    /// never comes back from here.
    /// </para>
    /// </summary>
    public static DevbookSubjectKind? Classify(string? chapterPath)
    {
        var normalized = DevbookChapterKey.Normalize(chapterPath);
        var devbook = DevbookFolderSetting.DevbookRoot + "/";
        var within = normalized.StartsWith(devbook, StringComparison.OrdinalIgnoreCase)
            ? "." + normalized[devbook.Length..]
            : normalized;

        return RecordIn(within, ".arc42/adr/") ? DevbookSubjectKind.Decision
            : RecordIn(within, ".arc42/tdr/") ? DevbookSubjectKind.Debt
            : null;
    }

    private static bool RecordIn(string path, string folder)
    {
        if (!path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return false;

        var name = path[folder.Length..];

        return name.Length > 0
            && !name.Contains('/', StringComparison.Ordinal)
            && name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            && !IndexFiles.Contains(name, StringComparer.OrdinalIgnoreCase);
    }
}
