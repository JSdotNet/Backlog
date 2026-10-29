using Backlog.Desktop.Services;

namespace Backlog.Desktop;

public partial class MainPage : ContentPage
{
    private readonly string _contentRoot;

    /// <summary>The main window's page: the shell, at <c>/</c>.</summary>
    public MainPage()
        : this(startPath: null)
    {
    }

    /// <summary>
    /// A window's page (local ADR 0042 §4): <paramref name="startPath"/> is the route
    /// its <c>BlazorWebView</c> opens on — <c>/window/{surfaceId}</c> for a popped-out
    /// window — and <paramref name="windowKey"/> the name the root component hands
    /// that route, so the page can close and retitle its own window.
    /// </summary>
    public MainPage(string? startPath, string? windowKey = null)
    {
        InitializeComponent();

        if (startPath is not null) blazorWebView.StartPath = startPath;
        if (windowKey is not null)
        {
            // The one root component MainPage.xaml declares, Routes, set before the
            // view loads: a string is a parameter a root component may carry.
            blazorWebView.RootComponents[0].Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [nameof(UI.Shell.Routes.WindowKey)] = windowKey,
            };
        }

        // MAUI reads app files from the host page's folder under the install
        // directory. AppContext.BaseDirectory is that directory both unpackaged
        // (Debug) and packaged (MSIX), so one root serves both.
        _contentRoot = Path.Combine(AppContext.BaseDirectory, Path.GetDirectoryName(blazorWebView.HostPage) ?? string.Empty);

        // MAUI types app files from its own extension table, which has no .mjs,
        // so the vendored Mermaid would go out as octet-stream and never load.
        // This hook runs before MAUI builds a response, and serves .mjs alone.
        // See ModuleScriptResponses for when to remove it.
        blazorWebView.WebResourceRequested += OnWebResourceRequested;
    }

    private void OnWebResourceRequested(object? sender, WebViewWebResourceRequestedEventArgs e)
    {
        if (!ModuleScriptResponses.TryResolve(ModuleScriptResponses.AppOrigin, e.Uri.AbsoluteUri, _contentRoot, out var filePath)
            || !File.Exists(filePath))
        {
            // Not ours, or not there: MAUI answers it exactly as it always has,
            // its 404 included.
            return;
        }

        e.Handled = true;

        // Opened here rather than in the read task: MAUI fixes the status before
        // the content arrives and sets no response at all if the task faults,
        // so an open that fails must become its status code now.
        FileStream file;
        try
        {
            file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            e.SetResponse(404, "Not Found");
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            e.SetResponse(500, "Internal Server Error");
            return;
        }

        e.SetResponse(200, "OK", ModuleScriptResponses.Headers, CopyToMemoryAsync(file));
    }

    // Copied into memory, as MAUI does with its own responses, so no file handle
    // outlives the request. Never faults: a faulted task leaves the request with
    // no response, so a read that fails midway sends what was read and the
    // module's own import reports it.
    private static async Task<Stream?> CopyToMemoryAsync(FileStream file)
    {
        var content = new MemoryStream();
        await using (file)
        {
            try
            {
                await file.CopyToAsync(content).ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
        }

        content.Position = 0;
        return content;
    }
}
