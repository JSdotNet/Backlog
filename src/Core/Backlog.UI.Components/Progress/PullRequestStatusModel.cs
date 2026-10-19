using System.Globalization;

namespace Backlog.UI.Components.Progress;

/// <summary>The checks a pull request's head carries, counted by outcome.</summary>
/// <param name="Passed">Checks that finished and passed.</param>
/// <param name="Failed">Checks that finished and failed.</param>
/// <param name="Pending">Checks still queued or running.</param>
public sealed record PullRequestChecks(int Passed, int Failed, int Pending)
{
    /// <summary>Every check, whatever its outcome.</summary>
    public int Total => Passed + Failed + Pending;
}

/// <summary>The approvals a pull request has, against what its base branch asks.</summary>
/// <param name="Received">Approvals given.</param>
/// <param name="Required">Approvals the base branch's rules ask for; null or nought when no
/// rule asks for any, null when the rules cannot be read.</param>
/// <param name="RequiredRead">Whether the read asked the base branch's rules at all. A read
/// that did not — the Pull requests pane's list read — passes false, and the mark says how
/// many approvals there are with no verdict, never "none required".</param>
public sealed record PullRequestApprovals(int Received, int? Required, bool RequiredRead = true);

/// <summary>Which of the checks mark's variants a pull request draws
/// (<c>.devbook/design/color-scheme.md#pull-request-status-marks</c>).</summary>
public enum PullRequestChecksVariant
{
    /// <summary>No checks were read, or none ran: the mark is left out.</summary>
    None,

    /// <summary>Every check finished and passed.</summary>
    Passed,

    /// <summary>Nothing failed yet and at least one check is still running.</summary>
    Running,

    /// <summary>At least one check failed.</summary>
    Failing
}

/// <summary>Which of the approvals mark's variants a pull request draws.</summary>
public enum PullRequestApprovalsVariant
{
    /// <summary>Approvals were not read: the mark is left out.</summary>
    None,

    /// <summary>The approvals received reach the required total.</summary>
    Met,

    /// <summary>Fewer approvals than the base branch asks for.</summary>
    Short,

    /// <summary>No rule asks for an approval: the received count stands alone.</summary>
    NoneRequired,

    /// <summary>The rules were not read: the received count stands alone, with no verdict
    /// on whether it is enough.</summary>
    Received
}

/// <summary>
/// One pull request as its status line draws it
/// (<c>.devbook/domain/progress/features.md#pull-request-status</c>), in plain values: the
/// library knows neither Progress nor GitHub, so each host turns its own pull request
/// into this. The variant rules live here so every row that draws the line agrees.
/// </summary>
/// <param name="Number">The pull request's number, drawn as <c>#n</c>.</param>
/// <param name="State">The <c>gh</c> badge slug: <c>open</c>, <c>draft</c>, <c>merged</c>,
/// <c>closed</c> or <c>unknown</c>.</param>
/// <param name="Checks">The checks, or null when they were not read.</param>
/// <param name="Approvals">The approvals, or null when they were not read.</param>
/// <param name="Title">The pull request's title, for the pill's tooltip; optional.</param>
/// <param name="Url">Where the pill leads; null draws it as plain text.</param>
public sealed record PullRequestStatusModel(
    int Number,
    string State,
    PullRequestChecks? Checks = null,
    PullRequestApprovals? Approvals = null,
    string? Title = null,
    string? Url = null)
{
    /// <summary>The <c>gh</c> badge slugs a state may take; anything else draws as
    /// <c>unknown</c>.</summary>
    public static IReadOnlyList<string> States { get; } = ["open", "draft", "merged", "closed", "unknown"];

    /// <summary>The badge slug the pill wears.</summary>
    public string Slug
    {
        get
        {
            var state = (State ?? string.Empty).Trim().ToLowerInvariant();
            return States.Contains(state) ? state : "unknown";
        }
    }

    /// <summary>A merged pull request reads "merged" in place of both marks.</summary>
    public bool IsMerged => Slug == "merged";

    /// <summary>Passed when every check passed, failing once one failed, running
    /// otherwise; none when there is nothing to count.</summary>
    public PullRequestChecksVariant ChecksVariant => Checks switch
    {
        null => PullRequestChecksVariant.None,
        { Total: <= 0 } => PullRequestChecksVariant.None,
        { Failed: > 0 } => PullRequestChecksVariant.Failing,
        { Pending: 0 } => PullRequestChecksVariant.Passed,
        _ => PullRequestChecksVariant.Running
    };

    /// <summary>Met when the approvals received reach the required total, short below it,
    /// none-required when no rule asks for one — a required total of nought included, so a
    /// rule that asks for no review never reads as a verdict; none when approvals were not
    /// read.</summary>
    public PullRequestApprovalsVariant ApprovalsVariant => Approvals switch
    {
        null => PullRequestApprovalsVariant.None,
        { RequiredRead: false } => PullRequestApprovalsVariant.Received,
        { Required: null or <= 0 } => PullRequestApprovalsVariant.NoneRequired,
        { Received: var received, Required: var required } when received >= required => PullRequestApprovalsVariant.Met,
        _ => PullRequestApprovalsVariant.Short
    };

    /// <summary>The visible count, <c>passed/total</c>; empty without a checks mark.</summary>
    public string ChecksCount =>
        ChecksVariant is PullRequestChecksVariant.None ? string.Empty : $"{Checks!.Passed}/{Checks.Total}";

    /// <summary>What a screen reader hears for the checks mark, "9 of 14 checks running".</summary>
    public string ChecksSpoken => ChecksVariant switch
    {
        PullRequestChecksVariant.Passed => $"{Checks!.Passed} of {Checks.Total} checks passed",
        PullRequestChecksVariant.Running => $"{Checks!.Passed} of {Checks.Total} checks running",
        PullRequestChecksVariant.Failing => $"{Checks!.Passed} of {Checks.Total} checks failing",
        _ => string.Empty
    };

    /// <summary>The visible count, <c>received/required</c>, or the received count alone
    /// where no rule asks for an approval; empty without an approvals mark.</summary>
    public string ApprovalsCount => ApprovalsVariant switch
    {
        PullRequestApprovalsVariant.None => string.Empty,
        PullRequestApprovalsVariant.NoneRequired or PullRequestApprovalsVariant.Received =>
            Approvals!.Received.ToString(CultureInfo.InvariantCulture),
        _ => $"{Approvals!.Received}/{Approvals.Required}"
    };

    /// <summary>"1 approval", "2 approvals": the received count in words.</summary>
    private string Received =>
        $"{Approvals!.Received} {(Approvals.Received == 1 ? "approval" : "approvals")}";

    /// <summary>What a screen reader hears for the approvals mark, "1 of 2 approvals".</summary>
    public string ApprovalsSpoken => ApprovalsVariant switch
    {
        PullRequestApprovalsVariant.None => string.Empty,
        PullRequestApprovalsVariant.NoneRequired => $"{Received}, none required",
        PullRequestApprovalsVariant.Received => Received,
        _ => $"{Approvals!.Received} of {Approvals.Required} approvals"
    };
}
