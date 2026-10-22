namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// How the Sessions context names the things a notification can be about, and the reason
/// each closes with when it ends (<c>.devbook/domain/notifications/domain.md#subject</c>).
/// <para>
/// Plain strings, because this project may not see the Notifications context's
/// abstractions (<c>ModuleBoundaryTests</c>): the adapters that do — the closers and the
/// probe in <c>Backlog.Infrastructure.Sessions</c>, the pull request closer in the desktop
/// shell — build the subject from these, and so does whatever publishes about a session, a
/// run or a pull request. One spelling, so a question published about a run is the one the
/// run's end closes.
/// </para>
/// </summary>
public static class SessionNotificationSubjects
{
    /// <summary>The subject context every Sessions subject is named under.</summary>
    public const string Context = "sessions";

    /// <summary>An agent session, by <see cref="AgentSession.Id"/>.</summary>
    public const string Session = "session";

    /// <summary>A delivery run, by its run id.</summary>
    public const string Run = "run";

    /// <summary>A pull request, by <see cref="PullRequestId"/>.</summary>
    public const string PullRequest = "pull-request";

    /// <summary>Why a notification about a session closes: it was archived or deleted.</summary>
    public const string SessionEndedReason = "Session archived";

    /// <summary>Why a notification about a run closes: it finished or was abandoned.</summary>
    public const string RunEndedReason = "Run finished";

    /// <summary>Why a notification about a pull request closes: it merged or closed.</summary>
    public const string PullRequestEndedReason = "Pull request merged";

    /// <summary>A pull request's subject id: <c>owner/name#number</c>, the repository as
    /// GitHub spells it in lower case so two spellings of one repository are one subject.</summary>
    public static string PullRequestId(string repository, int number)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);

        return $"{repository.Trim().ToLowerInvariant()}#{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>The repository and number a <see cref="PullRequestId"/> names, or null for
    /// an id that is not one.</summary>
    public static (string Repository, int Number)? ParsePullRequestId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        var hash = id.LastIndexOf('#');
        if (hash <= 0 || !id[..hash].Contains('/')) return null;

        return int.TryParse(id[(hash + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) && number > 0
            ? (id[..hash], number)
            : null;
    }
}
