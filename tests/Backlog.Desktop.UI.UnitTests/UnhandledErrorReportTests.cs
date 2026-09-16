using Backlog.Desktop.UI.Shell;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The text an unhandled exception turns into when the error screen hands it to
/// the Report issue dialog. Pinned on its own because it is the only part of a
/// crash that reaches whoever reads the filed issue.
/// </summary>
public sealed class UnhandledErrorReportTests
{
    private static readonly DateTimeOffset When = new(2026, 9, 15, 10, 30, 0, TimeSpan.FromHours(2));

    [Fact]
    public void The_title_names_the_exception_and_its_first_line()
    {
        var draft = UnhandledErrorReport.Draft(
            new InvalidOperationException("Nothing to save.\nSecond line nobody needs in a title."),
            "/settings",
            "1.2.3",
            When);

        Assert.Equal("Unhandled error: InvalidOperationException: Nothing to save.", draft.Title);
    }

    [Fact]
    public void The_title_never_exceeds_what_the_dialog_accepts()
    {
        var draft = UnhandledErrorReport.Draft(
            new InvalidOperationException(new string('x', 500)),
            "/",
            "1.2.3",
            When);

        Assert.Equal(UnhandledErrorReport.TitleMaxLength, draft.Title.Length);
        Assert.EndsWith("…", draft.Title);
    }

    [Fact]
    public void The_details_carry_where_when_which_build_and_the_exception_itself()
    {
        Exception thrown;
        try
        {
            throw new InvalidOperationException("The page threw.");
        }
        catch (InvalidOperationException ex)
        {
            thrown = ex;
        }

        var draft = UnhandledErrorReport.Draft(thrown, "/settings", "1.2.3", When);

        Assert.Contains("- Where: `/settings`", draft.Details);
        Assert.Contains("- Version: `1.2.3`", draft.Details);
        // Written in UTC, whatever offset the machine was on.
        Assert.Contains("- When: 2026-09-15 08:30:00 UTC", draft.Details);
        // The whole exception, stack included — the reader was not at the machine.
        Assert.Contains("System.InvalidOperationException: The page threw.", draft.Details);
        Assert.Contains(nameof(The_details_carry_where_when_which_build_and_the_exception_itself), draft.Details);
        // And room for the one thing the exception cannot say.
        Assert.Contains("What I was doing:", draft.Details);
    }

    [Fact]
    public void The_exception_text_is_fenced_wider_than_any_fence_it_might_contain()
    {
        var draft = UnhandledErrorReport.Draft(
            new InvalidOperationException("Message with a ``` fence inside it."),
            "/",
            "1.2.3",
            When);

        Assert.Equal(2, draft.Details.Split("````").Length - 1);
    }

    [Fact]
    public void A_runaway_exception_is_cut_and_says_so()
    {
        var draft = UnhandledErrorReport.Draft(
            new InvalidOperationException(new string('y', UnhandledErrorReport.ExceptionMaxLength * 2)),
            "/",
            "1.2.3",
            When);

        Assert.Contains("(cut here)", draft.Details);
        Assert.True(draft.Details.Length < UnhandledErrorReport.ExceptionMaxLength + 1000);
    }
}
