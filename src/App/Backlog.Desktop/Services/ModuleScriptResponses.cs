using System.Diagnostics.CodeAnalysis;

namespace Backlog.Desktop.Services;

/// <summary>
/// Decides which of the head's own requests are module scripts it must serve
/// itself, and with which headers.
/// <para>
/// MAUI's Windows BlazorWebView reads app files from disk and types them from a
/// private copy of the extension table, and that copy has no <c>.mjs</c> entry:
/// a module script goes out as <c>application/octet-stream</c>, which WebView2
/// refuses to run, so the vendored Mermaid never loads. ASP.NET Core's own table
/// maps <c>.mjs</c> to <c>text/javascript</c>, which is why the web harnesses and
/// the Android head never showed it. Remove this, and its hook in
/// <c>MainPage</c>, once a MAUI release maps <c>.mjs</c>.
/// </para>
/// <para>
/// It names only <c>.mjs</c>, so every other file keeps MAUI's answer, and it
/// takes no MAUI or WebView2 type, so a test project can compile it from source.
/// </para>
/// </summary>
internal static class ModuleScriptResponses
{
    private const string ModuleExtension = ".mjs";

    /// <summary>
    /// The origin MAUI's Windows BlazorWebView serves the app from. MAUI keeps
    /// its own copy internal; it becomes <c>https://0.0.0.0/</c> under the
    /// AppContext switch <c>BlazorWebView.AppHostAddressAlways0000</c>, so
    /// whoever sets that switch changes this too. Until then a mismatch only
    /// declines, and MAUI's octet-stream answer returns.
    /// </summary>
    public static Uri AppOrigin { get; } = new("https://0.0.0.1/");

    /// <summary>
    /// The type ASP.NET Core gives <c>.mjs</c>, and MAUI's own no-store policy,
    /// so a served module differs from MAUI's response in its type alone.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Content-Type"] = "text/javascript",
        ["Cache-Control"] = "no-cache, max-age=0, must-revalidate, no-store",
    };

    /// <summary>
    /// Maps a request to the <c>.mjs</c> file under <paramref name="contentRoot"/>
    /// it names, when it comes from <paramref name="appOrigin"/> and stays inside
    /// that root. Says nothing about whether the file exists.
    /// </summary>
    public static bool TryResolve(Uri appOrigin, string requestUri, string contentRoot, [NotNullWhen(true)] out string? filePath)
    {
        ArgumentNullException.ThrowIfNull(appOrigin);
        ArgumentException.ThrowIfNullOrEmpty(contentRoot);

        filePath = null;

        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var uri)
            || Uri.Compare(appOrigin, uri, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
        {
            return false;
        }

        // AbsolutePath carries no query, so a retry's ?retry=N names the same
        // file. Unescaped once, as MAUI does, so an encoded name finds its file.
        var relativePath = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if (!relativePath.EndsWith(ModuleExtension, StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        // An encoded ../ or \ survives the URI's own normalisation, and a drive
        // letter makes Combine discard the root, so only the resolved path is
        // trusted, and only when it is still inside the root.
        // A path that names no file (an encoded NUL, say) is declined rather
        // than thrown: MAUI raises the hook inside an async-void lambda, so a
        // throw here would crash the app on any link a chapter happens to hold.
        string root;
        string candidate;
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(contentRoot)) + Path.DirectorySeparatorChar;
            candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        filePath = candidate;
        return true;
    }
}
