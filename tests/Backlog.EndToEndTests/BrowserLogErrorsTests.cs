namespace Backlog.EndToEndTests;

/// <summary>
/// What the run's browser-log check counts as an error in a frontend's
/// <c>&lt;frontend&gt;-browser-logs</c> console stream, read with
/// <c>aspire logs --format Json</c>. The lines are the ones
/// <c>Aspire.Hosting.Browsers</c> writes per tracked-browser event (its
/// <c>BrowserEventLogger</c>): <c>[session] [console.level] …</c>,
/// <c>[session] [exception] …</c>, <c>[session] [log.level] …</c> and
/// <c>[session] [network.type] METHOD url …</c>.
///
/// <para>Plain facts: they parse text and start nothing, so they run without
/// <c>BACKLOG_E2E</c>.</para>
/// </summary>
public sealed class BrowserLogErrorsTests
{
    private const string Resource = "desktop-web-harness-browser-logs";

    [Fact]
    public void A_console_error_is_an_error()
    {
        var errors = AspireAppHost.BrowserErrors(Logs(("[s1] [console.error] Uncaught render failure", false)));

        Assert.Equal(["[s1] [console.error] Uncaught render failure"], errors.Select(error => error.Content));
    }

    [Fact]
    public void A_failed_console_assert_is_an_error()
    {
        var errors = AspireAppHost.BrowserErrors(Logs(("[s1] [console.assert] Assertion failed", false)));

        Assert.Single(errors);
    }

    [Fact]
    public void An_uncaught_exception_is_an_error()
    {
        var errors = AspireAppHost.BrowserErrors(Logs(("[s1] [exception] TypeError: x is undefined (http://localhost:5001/app.js:3:7)", false)));

        Assert.Single(errors);
    }

    [Fact]
    public void A_browser_error_log_entry_is_an_error()
    {
        var errors = AspireAppHost.BrowserErrors(Logs(("[s1] [log.error] Refused to load the script (http://localhost:5001/)", false)));

        Assert.Single(errors);
    }

    [Fact]
    public void A_failed_request_is_an_error()
    {
        var errors = AspireAppHost.BrowserErrors(Logs(("[s1] [network.fetch] GET http://localhost:5001/api/tasks failed: net::ERR_CONNECTION_REFUSED (4 ms)", false)));

        Assert.Single(errors);
    }

    [Fact]
    public void A_canceled_request_is_not_an_error()
    {
        var errors = AspireAppHost.BrowserErrors(Logs(("[s1] [network.document] GET http://localhost:5001/inbox failed: net::ERR_ABORTED (12 ms, canceled)", false)));

        Assert.Empty(errors);
    }

    [Fact]
    public void Information_warnings_and_completed_requests_are_not_errors()
    {
        var errors = AspireAppHost.BrowserErrors(Logs(
            ("[s1] Tracking browser console logs for 'http://localhost:5001/'.", false),
            ("[s1] [console.log] Blazor started", false),
            ("[s1] [console.warn] Deprecated API", false),
            ("[s1] [log.warning] Third-party cookie blocked", false),
            ("[s1] [network.fetch] GET http://localhost:5001/api/tasks -> 500 Internal Server Error (8 ms, 120 B)", false)));

        Assert.Empty(errors);
    }

    [Fact]
    public void A_line_the_resource_logged_at_error_is_an_error()
    {
        var errors = AspireAppHost.BrowserErrors(Logs(("[s1] Unable to reconnect tracked browser debug connection.", true)));

        Assert.Single(errors);
    }

    [Fact]
    public void An_empty_stream_has_no_errors()
    {
        Assert.Empty(AspireAppHost.BrowserErrors("""{ "logs": [] }"""));
    }

    [Fact]
    public void Lines_up_to_the_baseline_are_not_counted()
    {
        var errors = AspireAppHost.BrowserErrors(
            Logs(("[s1] [console.error] before the run", false), ("[s1] [console.error] during the run", false)),
            afterLine: 1);

        Assert.Equal(["[s1] [console.error] during the run"], errors.Select(error => error.Content));
    }

    [Fact]
    public void Each_error_names_its_resource_and_line()
    {
        var error = Assert.Single(AspireAppHost.BrowserErrors(Logs(("[s1] [console.log] ok", false), ("[s1] [console.error] boom", false))));

        Assert.Equal((Resource, 2), (error.Resource, error.Line));
    }

    [Fact]
    public void A_declared_error_of_that_resource_is_expected_and_others_are_not()
    {
        var errors = AspireAppHost.BrowserErrors(Logs(
            ("[s1] [console.error] sync is offline on purpose", false),
            ("[s1] [console.error] something else broke", false)));
        ExpectedBrowserErrors[] declared =
        [
            new(Resource, entry => entry.Content.Contains("offline on purpose", StringComparison.Ordinal), "the test stops sync"),
            new("mobile-web-harness-browser-logs", _ => true, "another frontend's declaration"),
        ];

        var unexpected = E2EAppHost.UnexpectedBrowserErrors(errors, declared);

        Assert.Equal(["[s1] [console.error] something else broke"], unexpected.Select(error => error.Content));
    }

    private static string Logs(params (string Content, bool IsError)[] lines) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            logs = lines.Select(line => new { resourceName = Resource, content = line.Content, isError = line.IsError }),
        });
}
