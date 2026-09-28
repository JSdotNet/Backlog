using Microsoft.Playwright;

namespace Backlog.EndToEndTests;

/// <summary>
/// Where a run leaves what a reviewer opens: one viewport screenshot per
/// checkpoint, numbered in the order they were taken, and the logs beside them.
///
/// <para>Viewport, never full page. A full-page capture removes the scrollbar,
/// which widens the page's container past the phone breakpoint, so the picture
/// would show a layout no phone ever renders.</para>
///
/// <para>The folder is under <c>.qa-workspace/</c>, which git ignores apart from
/// the committed fixtures, and <c>BACKLOG_E2E_EVIDENCE</c> moves it.</para>
/// </summary>
internal sealed class Evidence
{
    private int _next = 1;

    private Evidence(string folder) => Folder = folder;

    public string Folder { get; }

    public static Evidence For(string scenario)
    {
        var root = Environment.GetEnvironmentVariable("BACKLOG_E2E_EVIDENCE") is { Length: > 0 } configured
            ? configured
            : Path.Combine(RepositoryRoot.Root.FullName, ".qa-workspace", "e2e");
        var folder = Path.Combine(root, scenario, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        return new Evidence(folder);
    }

    public async Task ScreenshotAsync(IPage page, string checkpoint)
    {
        var path = Path.Combine(Folder, $"{_next++:00}-{checkpoint}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false });
        TestContext.Current.SendDiagnosticMessage($"screenshot: {path}");
    }

    public async Task WriteAsync(string name, string content)
    {
        var path = Path.Combine(Folder, name);
        await File.WriteAllTextAsync(path, content);
        TestContext.Current.SendDiagnosticMessage($"evidence: {path}");
    }
}
