using System.Globalization;
using Microsoft.Playwright;

namespace Backlog.EndToEndTests;

/// <summary>
/// One scenario-evidence test's setting: a browser on the desktop harness and a
/// repository of the test's own, registered through Settings › Repositories.
///
/// <para>The repository is <c>tests/Fixtures/scenario-repository</c> copied to a
/// folder under the temp folder — outside every repository, so no git checkout is
/// mistaken for it — holding one journey, <c>shop/scenarios.md#place-an-order</c>, and
/// its committed run: the first part passed, the second failed on step 1, with a label
/// screenshot and a failure screenshot. It is registered under an alias and an
/// <c>owner/name</c> nobody else uses, and removed again on dispose, pass or fail,
/// through the same page. A row a crashed earlier run left behind is removed before
/// it is added again. The registry is never written by hand: it is shared by every
/// worktree's harness and travels to paired devices.</para>
/// </summary>
internal sealed class ScenarioRepository : IAsyncDisposable
{
    public const string Alias = "scenarios-e2e";

    public const string FullName = "JSdotNet/backlog-scenarios-e2e";

    /// <summary>The scenarios file the fixture's one journey is written in.</summary>
    public const string PagePath = ".devbook/domain/shop/scenarios.md";

    /// <summary>The journey's anchor: its run folder's name and what its evidence is
    /// filed under.</summary>
    public const string Stem = "place-an-order";

    public const string PassedPart = "the-cart-is-filled";

    public const string FailedPart = "the-order-is-placed";

    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;

    private ScenarioRepository(IPlaywright playwright, IBrowser browser, AspireAppHost appHost, DesktopHarness desktop, string folder, Evidence evidence, string token)
    {
        _playwright = playwright;
        _browser = browser;
        AppHost = appHost;
        Desktop = desktop;
        Folder = folder;
        Evidence = evidence;
        Token = token;
    }

    public AspireAppHost AppHost { get; }

    public DesktopHarness Desktop { get; }

    /// <summary>The registered clone: the fixture, copied.</summary>
    public string Folder { get; }

    public Evidence Evidence { get; }

    /// <summary>Stamped on every title the test writes into the shared workspace.</summary>
    public string Token { get; }

    public static async Task<ScenarioRepository> OpenAsync(string scenario, CancellationToken cancellationToken)
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("BACKLOG_E2E") == "1",
            "Browser end-to-end tests run against a running AppHost; set BACKLOG_E2E=1 to run them (tests/Backlog.EndToEndTests/README.md).");

        var appHost = AspireAppHost.ForThisWorktree();
        await appHost.WaitHealthyAsync(DesktopHarness.Resource, TimeSpan.FromMinutes(10), cancellationToken);

        var token = DateTime.Now.ToString("MMdd-HHmmss", CultureInfo.InvariantCulture);
        var folder = Path.Combine(Path.GetTempPath(), "backlog-e2e-scenarios", $"{scenario}-{token}-{Guid.NewGuid().ToString("n")[..8]}");
        Copy(RepositoryRoot.Directory("tests", "Fixtures", "scenario-repository"), folder);

        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = Environment.GetEnvironmentVariable("BACKLOG_E2E_HEADED") != "1",
            Channel = Environment.GetEnvironmentVariable("BACKLOG_E2E_BROWSER_CHANNEL") is { Length: > 0 } channel ? channel : null,
        });

        var desktop = await DesktopHarness.OpenAsync(browser, appHost, cancellationToken);
        var opened = new ScenarioRepository(playwright, browser, appHost, desktop, folder, Evidence.For($"scenarios-{scenario}"), token);

        try
        {
            await desktop.RemoveRepositoryAsync(Alias);
            await desktop.AddRepositoryAsync(Alias, FullName, folder);
            TestContext.Current.SendDiagnosticMessage($"{FullName} registered as {Alias} at {folder}");
        }
        catch
        {
            await opened.DisposeAsync();
            throw;
        }

        return opened;
    }

    /// <summary>The page with the repository's scope chip alone pressed, so the
    /// Tasks view, the Devbook pane and the Sessions list read this repository.</summary>
    public async Task ScopeToRepositoryAsync()
    {
        await Desktop.ShowAllTasksAsync();
        await Desktop.ClearScopeAsync();
        await Desktop.PressScopeChipAsync(Desktop.RepositoryChip(Alias));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // The test's own token: a failed step does not cancel it.
            var desktop = await DesktopHarness.OpenAsync(_browser, AppHost, TestContext.Current.CancellationToken);
            await desktop.RemoveRepositoryAsync(Alias);
        }
        catch (Exception exception)
        {
            TestContext.Current.SendDiagnosticMessage(
                $"Could not remove {Alias} ({FullName}): {exception.Message}. Remove it in Settings › Repositories by hand.");
        }

        await _browser.DisposeAsync();
        _playwright.Dispose();

        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A watch may still hold it a moment; it is under the temp folder.
        }
    }

    private static void Copy(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
