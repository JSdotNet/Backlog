using System.Text.Json.Nodes;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.Sessions;

/// <summary>A pull request a run recorded on one of its stages: the link it recorded, and
/// the repository and number the address names.</summary>
internal sealed record RecordedPullRequest(DeliveryStageLink Link, string Repository, int Number);

/// <summary>
/// The pull requests a run recorded, and whether they all ended — one reading for every
/// place that asks: the gate before it asks its Personal Validation question
/// (<see cref="ValidationQuestions"/>), and the Sessions probe the startup pass and
/// <see cref="RunSubjectWatch"/> ask afterwards (<see cref="SessionSubjectProbe"/>). The same
/// set either way — every GitHub pull request address in any stage's links, as
/// <see cref="ReviewLinks"/> parses it — so the gate never asks a question the watch would
/// close at once, nor the other way round.
/// </summary>
internal static class RunPullRequests
{
    /// <summary>The pull requests in a run file's stage links, in stage order.</summary>
    internal static IReadOnlyList<RecordedPullRequest> Of(JsonObject run) =>
        From((run["stages"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .SelectMany(stage => (stage["links"] as JsonArray ?? []).OfType<JsonObject>())
            .Select(link => new DeliveryStageLink(Text(link, "label"), Text(link, "url"), Text(link, "description"))));

    /// <summary>The pull requests in a read run's stage links, in stage order.</summary>
    internal static IReadOnlyList<RecordedPullRequest> Of(DeliveryRun run) =>
        From(run.Stages.SelectMany(stage => stage.Links));

    /// <summary>One per repository and number, the first link that names it — which carries
    /// the label the run gave it there.</summary>
    private static List<RecordedPullRequest> From(IEnumerable<DeliveryStageLink> links)
    {
        var found = new List<RecordedPullRequest>();
        var seen = new HashSet<(string, int)>();

        foreach (var link in links)
        {
            if (ReviewLinks.PullRequestOf(link.Url) is not { } named) continue;
            if (!seen.Add((named.Repository.ToLowerInvariant(), named.Number))) continue;

            found.Add(new RecordedPullRequest(link, named.Repository, named.Number));
        }

        return found;
    }

    /// <summary>
    /// Why the run's pull requests ended, once every one reads merged or closed — "Pull
    /// request merged" when any merged, else "Pull request closed" — or null when there are
    /// none, or any reads open, a draft, or as nothing. A read that fails throws, and one
    /// that outlasts <paramref name="cancellationToken"/> is abandoned with an
    /// <see cref="OperationCanceledException"/> even when the reader ignores the token.
    /// </summary>
    internal static async Task<string?> EndAsync(
        IReadOnlyList<RecordedPullRequest> pulls,
        ISessionPullRequestStates states,
        CancellationToken cancellationToken)
    {
        if (pulls.Count == 0) return null;

        var merged = false;

        foreach (var pull in pulls)
        {
            var state = await states.StateAsync(pull.Repository, pull.Number, cancellationToken)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            if (EndOf(state) is null) return null;

            merged |= state is SessionPullRequestState.Merged;
        }

        return merged ? SessionNotificationSubjects.PullRequestMergedReason : SessionNotificationSubjects.PullRequestClosedReason;
    }

    /// <summary>The reason a notification about a pull request in <paramref name="state"/>
    /// closes with, or null while it is open, a draft, or unread.</summary>
    internal static string? EndOf(SessionPullRequestState? state) => state switch
    {
        SessionPullRequestState.Merged => SessionNotificationSubjects.PullRequestMergedReason,
        SessionPullRequestState.Closed => SessionNotificationSubjects.PullRequestClosedReason,
        _ => null
    };

    private static string? Text(JsonObject owner, string name) =>
        owner[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
