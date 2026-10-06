namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// The demos half of the convention: which <c>*.demo.html</c> a page owns by its
/// name, and what a chapter's <c>demo</c> field points at.
///
/// <para>A demo is the one HTML file a bounded context holds — the agreed,
/// clickable picture of what a person sees (devbook's <c>devbook-domain.md</c>).
/// It is not a chapter: it has no <c>meta</c> block and no reading position, so
/// nothing here touches <see cref="Order(string?, int, IEnumerable{string})"/>. It
/// pairs two ways. By name, the way an invariants subpage pairs: <c>demo.html</c>
/// is the context's own and counts with <c>context.md</c>, and
/// <c>&lt;page&gt;.demo.html</c> is <c>&lt;page&gt;.md</c>'s, beside it. And by
/// field: any chapter in any folder names the places in a demo that show it, as
/// <c>demo: [&lt;path&gt;#&lt;id&gt;, …]</c> (<c>devbook-chapter-metadata.md</c>).</para>
///
/// <para><c>tools/devbook/build-database.mjs</c> states the same two rules for the
/// Node writer — it may run an older generator that knows nothing of demos — and
/// <c>DevbookBuilderParityTests</c> holds the two to each other.</para>
/// </summary>
public static partial class DevbookReadingConvention
{
    /// <summary>The suffix every demo carries.</summary>
    public const string DemoSuffix = ".demo.html";

    /// <summary>The context's own demo, counted with <see cref="ContextDemoPage"/>.</summary>
    public const string ContextDemo = "demo.html";

    /// <summary>The page <see cref="ContextDemo"/> pairs with.</summary>
    public const string ContextDemoPage = "context.md";

    /// <summary>Whether a file name is a demo's.</summary>
    public static bool IsDemo(string name) => DemoPage(name) is not null;

    /// <summary>
    /// The page a demo pairs with by its name — <c>context.md</c> for
    /// <c>demo.html</c>, <c>features.checkout.md</c> for
    /// <c>features.checkout.demo.html</c> — or <see langword="null"/> when the name
    /// is not a demo's. Whether that page exists is the caller's question: a
    /// demo whose page is gone pairs with nothing by name, and only a chapter's
    /// <c>demo</c> field can still claim it.
    /// </summary>
    public static string? DemoPage(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var segment = name.Replace('\\', '/');
        segment = segment[(segment.LastIndexOf('/') + 1)..];

        if (segment == ContextDemo) return ContextDemoPage;
        if (!segment.EndsWith(DemoSuffix, StringComparison.Ordinal)) return null;

        var stem = segment[..^DemoSuffix.Length];
        return stem.Length == 0 ? null : stem + ".md";
    }

    /// <summary>The demos among <paramref name="names"/> that pair with
    /// <paramref name="pageName"/> by name, in ordinal order.</summary>
    public static IReadOnlyList<string> DemosOf(string pageName, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(pageName);
        ArgumentNullException.ThrowIfNull(names);

        var demos = names.Where(name => DemoPage(name) == pageName).ToList();
        demos.Sort(StringComparer.Ordinal);
        return demos;
    }

    /// <summary>
    /// A <c>demo</c> field's value as written in a <c>meta</c> block —
    /// <c>[a, b]</c>, a bare <c>a</c>, or the <c>a, b</c> a panel flattens a list
    /// to — as the places it names.
    /// </summary>
    public static IReadOnlyList<DevbookDemoReference> DemoField(string? value)
    {
        if (value is null) return [];

        var inner = value.Trim();
        if (inner.StartsWith('[') && inner.EndsWith(']')) inner = inner[1..^1];

        return DemoReferences(inner.Split(',').Select(entry => StripQuotes(entry.Trim())));
    }

    /// <summary>
    /// A <c>demo</c> field's entries, as the metadata parse leaves them, as the
    /// places they name.
    ///
    /// <para>The parse splits a list on every comma outside quotes, and an address
    /// whose <c>flags</c> names two keys holds one: the rule says to quote it, and
    /// an unquoted one is split there. So an entry whose path is not a demo's continues the
    /// one before it, and is joined back on with the comma it lost. An entry that
    /// starts no address and follows none is not one and is dropped.</para>
    /// </summary>
    public static IReadOnlyList<DevbookDemoReference> DemoReferences(IEnumerable<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var joined = new List<string>();
        foreach (var raw in entries)
        {
            var entry = raw.Trim();
            if (entry.Length == 0) continue;

            if (StartsDemoAddress(entry) || joined.Count == 0) joined.Add(entry);
            else joined[^1] += "," + entry;
        }

        var references = new List<DevbookDemoReference>(joined.Count);
        foreach (var entry in joined)
        {
            var address = StripQuotes(entry);
            var hash = address.IndexOf('#', StringComparison.Ordinal);
            var path = (hash < 0 ? address : address[..hash]).Trim().Replace('\\', '/');
            while (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];

            if (DemoPage(path) is null) continue;

            var fragment = hash < 0 ? null : address[(hash + 1)..].Trim();
            references.Add(new DevbookDemoReference(path, string.IsNullOrEmpty(fragment) ? null : fragment));
        }

        return references;
    }

    private static bool StartsDemoAddress(string entry)
    {
        var text = entry.TrimStart('"', '\'');
        var hash = text.IndexOf('#', StringComparison.Ordinal);
        return DemoPage((hash < 0 ? text : text[..hash]).Trim()) is not null;
    }

    private static string StripQuotes(string value) =>
        value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;
}

/// <summary>
/// One place a chapter's <c>demo</c> field names.
/// </summary>
/// <param name="Path">The demo's repository path, <c>.devbook/domain/ordering/features.demo.html</c>.</param>
/// <param name="Address">What follows the <c>#</c> — a screen, a screen's anchor
/// and panel state, or <c>walkthrough/&lt;id&gt;</c> (devbook's
/// <c>devbook.demo.address@1</c>) — or <see langword="null"/> for the demo as a
/// whole.</param>
public sealed record DevbookDemoReference(string Path, string? Address);
