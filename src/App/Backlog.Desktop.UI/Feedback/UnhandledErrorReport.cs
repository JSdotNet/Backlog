using System.Globalization;

// The namespace deliberately does not match the folder, for the reason
// FeedbackReporter.cs beside this file gives.
namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// Writes an unhandled exception up as the draft of a bug report.
/// <para>
/// Separate from the error screen that shows it because the text is the part
/// worth pinning: whoever reads the filed issue was not at the machine, and the
/// exception's own text is the only evidence that reaches them. The reader still
/// sees the draft before it is sent — the dialog opens with it, nothing is
/// filed on their behalf — so the details also leave a line for what they were
/// doing, which is the one thing the exception cannot say.
/// </para>
/// </summary>
internal static class UnhandledErrorReport
{
    /// <summary>The dialog's own title limit, restated so a prefilled title never
    /// arrives longer than one the reader could have typed.</summary>
    internal const int TitleMaxLength = 120;

    /// <summary>Where the exception text is cut. GitHub takes far more, but an
    /// issue that is mostly a repeated inner stack says less than one that stops
    /// and says it stopped.</summary>
    internal const int ExceptionMaxLength = 8000;

    private const string Ellipsis = "…";

    public static FeedbackReportDraft Draft(Exception exception, string location, string version, DateTimeOffset when)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new FeedbackReportDraft(Title(exception), Details(exception, location, version, when));
    }

    private static string Title(Exception exception)
    {
        var title = $"Unhandled error: {exception.GetType().Name}: {FirstLine(exception.Message)}";

        return title.Length <= TitleMaxLength
            ? title
            : title[..(TitleMaxLength - Ellipsis.Length)] + Ellipsis;
    }

    private static string Details(Exception exception, string location, string version, DateTimeOffset when)
    {
        var text = exception.ToString();
        if (text.Length > ExceptionMaxLength)
        {
            text = text[..ExceptionMaxLength] + Environment.NewLine + Ellipsis + " (cut here)";
        }

        // Four backticks rather than three, so a message that itself quotes a
        // fenced block does not end the fence early.
        return $"""
            An unhandled error stopped the page. Reported from the app's error screen.

            - Where: `{location}`
            - Version: `{version}`
            - When: {when.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)}

            What I was doing:

            _(describe the steps that led here)_

            Exception:

            ````
            {text}
            ````
            """;
    }

    private static string FirstLine(string message)
    {
        var line = message.AsSpan().Trim();
        var newline = line.IndexOfAny('\r', '\n');
        return (newline < 0 ? line : line[..newline]).ToString();
    }
}
