using System.Globalization;
using Microsoft.Playwright;

namespace Backlog.EndToEndTests;

/// <summary>
/// One Brain scenario's setting: a browser on the desktop harness, and a Brain
/// folder of the scenario's own under the temp folder — outside every repository,
/// and never the person's.
///
/// <para>The harness's Brain setting is this worktree's, but somebody may have
/// pointed it at a folder of their own. The folder the card held is remembered
/// before the scenario touches it and typed back in when the scenario is disposed,
/// pass or fail; the scenario's folder is then deleted. Nothing is ever written
/// into the remembered folder.</para>
/// </summary>
internal sealed class BrainScenario : IAsyncDisposable
{
    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;
    private readonly string _remembered;

    private BrainScenario(IPlaywright playwright, IBrowser browser, AspireAppHost appHost, DesktopHarness desktop, string remembered, string folder, Evidence evidence, string token)
    {
        _playwright = playwright;
        _browser = browser;
        AppHost = appHost;
        Desktop = desktop;
        _remembered = remembered;
        Folder = folder;
        Evidence = evidence;
        Token = token;
    }

    public AspireAppHost AppHost { get; }

    /// <summary>The desktop page; replaced by <see cref="ReopenAsync"/>.</summary>
    public DesktopHarness Desktop { get; private set; }

    /// <summary>The scenario's Brain folder, created empty.</summary>
    public string Folder { get; }

    public Evidence Evidence { get; }

    /// <summary>Stamped on every title the scenario writes into the shared
    /// workspace, so it only ever looks for its own rows.</summary>
    public string Token { get; }

    public static async Task<BrainScenario> OpenAsync(string scenario, CancellationToken cancellationToken)
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("BACKLOG_E2E") == "1",
            "Browser end-to-end tests run against a running AppHost; set BACKLOG_E2E=1 to run them (tests/Backlog.EndToEndTests/README.md).");

        var appHost = AspireAppHost.ForThisWorktree();
        await appHost.WaitHealthyAsync(DesktopHarness.Resource, TimeSpan.FromMinutes(10), cancellationToken);

        var token = DateTime.Now.ToString("MMdd-HHmmss", CultureInfo.InvariantCulture);
        var folder = Path.Combine(Path.GetTempPath(), "backlog-e2e-brain", $"{scenario}-{token}-{Guid.NewGuid().ToString("n")[..8]}");
        Directory.CreateDirectory(folder);

        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = Environment.GetEnvironmentVariable("BACKLOG_E2E_HEADED") != "1",
            Channel = Environment.GetEnvironmentVariable("BACKLOG_E2E_BROWSER_CHANNEL") is { Length: > 0 } channel ? channel : null,
        });

        var desktop = await DesktopHarness.OpenAsync(browser, appHost, cancellationToken);
        var remembered = await desktop.BrainFolderAsync();
        TestContext.Current.SendDiagnosticMessage($"Brain folder before the run: '{remembered}'; this scenario's: {folder}");

        return new BrainScenario(playwright, browser, appHost, desktop, remembered, folder, Evidence.For($"brain-{scenario}"), token);
    }

    /// <summary>Points the harness at the scenario's folder.</summary>
    public async Task UseFolderAsync()
    {
        await Desktop.OpenBrainCardAsync();
        await Desktop.SetBrainFolderAsync(Folder);
    }

    /// <summary>Writes a file into the scenario's folder, creating its folder.</summary>
    public void Write(string relativePath, string content)
    {
        var path = Path.Combine(Folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>The Markdown files in one area folder of the scenario's Brain.</summary>
    public IReadOnlyList<string> NotesIn(string area) =>
        Directory.Exists(Path.Combine(Folder, area))
            ? Directory.GetFiles(Path.Combine(Folder, area), "*.md").Order(StringComparer.Ordinal).ToList()
            : [];

    /// <summary>A fresh page on the harness, after it was restarted.</summary>
    public async Task ReopenAsync(CancellationToken cancellationToken)
    {
        await Desktop.Page.Context.CloseAsync();
        Desktop = await DesktopHarness.OpenAsync(_browser, AppHost, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // The test's own token: a failed step does not cancel it, so this still
            // runs after one.
            var desktop = await DesktopHarness.OpenAsync(_browser, AppHost, TestContext.Current.CancellationToken);
            await desktop.OpenBrainCardAsync();
            await desktop.SetBrainFolderAsync(_remembered);
        }
        catch (Exception exception)
        {
            TestContext.Current.SendDiagnosticMessage(
                $"Could not set the Brain folder back to '{_remembered}': {exception.Message}. Type it into Settings › Storage by hand.");
        }

        await _browser.DisposeAsync();
        _playwright.Dispose();

        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The watch may still hold it a moment; it is under the temp folder.
        }
    }
}
