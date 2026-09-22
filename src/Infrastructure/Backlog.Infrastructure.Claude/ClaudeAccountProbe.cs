using System.Net;
using System.Text.Json;

namespace Backlog.Infrastructure.Claude;

/// <summary>How one step of an account check came out.</summary>
public enum ClaudeCheckOutcome
{
    Passed,
    Failed,

    /// <summary>Not asked: the field behind it is blank, or an earlier step
    /// failed in a way that would only be repeated.</summary>
    Skipped
}

/// <summary>One line of an account check: what was asked, and what Anthropic
/// said.</summary>
public sealed record ClaudeAccountCheckStep(string Subject, ClaudeCheckOutcome Outcome, string Detail);

/// <summary>
/// What "Test this account" found, one line per question. Passed when nothing
/// failed - a skipped step is a blank field, which the card's own checklist
/// already points at, not a fault.
/// </summary>
public sealed record ClaudeAccountCheck(IReadOnlyList<ClaudeAccountCheckStep> Steps)
{
    public bool Passed => Steps.All(step => step.Outcome != ClaudeCheckOutcome.Failed);
}

/// <summary>
/// Asks Anthropic whether one account is set up to report anything.
/// <para>
/// Its own type rather than a method on <see cref="IClaudeUsageClient"/> because
/// it is asked from the Settings card and nowhere else, and because the answer is
/// a list of sentences for a person rather than a report for the dashboard. The
/// transport's own availability check only says a key string is present; this
/// is the call that finds out whether Anthropic agrees.
/// </para>
/// </summary>
public interface IClaudeAccountProbe
{
    Task<ClaudeAccountCheck> CheckAsync(ClaudeAccount account, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IClaudeAccountProbe"/>
public sealed class ClaudeAccountProbe(IClaudeTransport transport) : IClaudeAccountProbe
{
    private const string KeySubject = "Admin API key";
    private const string ActorSubject = "Your Claude account";
    private const string WorkspaceSubject = "Workspace";

    /// <summary>
    /// Three questions in the order the card asks for the fields. The key first,
    /// because the other two are asked with it: a refused key would only be refused
    /// twice more, so they are skipped rather than reported as three failures with
    /// one cause.
    /// </summary>
    public async Task<ClaudeAccountCheck> CheckAsync(ClaudeAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (!account.IsConfigured)
        {
            return new ClaudeAccountCheck(
            [
                new ClaudeAccountCheckStep(KeySubject, ClaudeCheckOutcome.Failed, "No key pasted yet."),
                NotAsked(ActorSubject, account),
                NotAsked(WorkspaceSubject, account)
            ]);
        }

        var organization = await OpenOrganizationAsync(account, cancellationToken).ConfigureAwait(false);
        if (organization.Name is null)
        {
            return new ClaudeAccountCheck(
            [
                organization.Step,
                new ClaudeAccountCheckStep(ActorSubject, ClaudeCheckOutcome.Skipped, "Not asked - the key was refused."),
                new ClaudeAccountCheckStep(WorkspaceSubject, ClaudeCheckOutcome.Skipped, "Not asked - the key was refused.")
            ]);
        }

        var steps = new List<ClaudeAccountCheckStep>(3) { organization.Step };

        steps.Add(string.IsNullOrWhiteSpace(account.Actor)
            ? NotAsked(ActorSubject, account)
            : await FindMemberAsync(account, organization.Name, cancellationToken).ConfigureAwait(false));

        steps.Add(string.IsNullOrWhiteSpace(account.WorkspaceId)
            ? NotAsked(WorkspaceSubject, account)
            : await FindWorkspaceAsync(account, organization.Name, cancellationToken).ConfigureAwait(false));

        return new ClaudeAccountCheck(steps);
    }

    private async Task<(ClaudeAccountCheckStep Step, string? Name)> OpenOrganizationAsync(ClaudeAccount account, CancellationToken cancellationToken)
    {
        try
        {
            var me = await transport.SendAsync(account, HttpMethod.Get, "v1/organizations/me", cancellationToken).ConfigureAwait(false);
            var name = Text(me, "name") ?? Text(me, "id") ?? "an organization";

            return (new ClaudeAccountCheckStep(KeySubject, ClaudeCheckOutcome.Passed, $"Opens the organization {name}."), name);
        }
        catch (Exception ex) when (ex is ClaudeException or ClaudeNotConfiguredException)
        {
            return (new ClaudeAccountCheckStep(KeySubject, ClaudeCheckOutcome.Failed, ex.Message), null);
        }
    }

    /// <summary>
    /// The member list, filtered by email - and then searched for the email, because
    /// a filter an endpoint ignores hands back everybody, and the wrong somebody must
    /// not pass for you. Bounded paging, the way the usage client bounds its own.
    /// </summary>
    private async Task<ClaudeAccountCheckStep> FindMemberAsync(ClaudeAccount account, string organization, CancellationToken cancellationToken)
    {
        var actor = account.Actor!.Trim();
        var path = $"v1/organizations/users?email={Uri.EscapeDataString(actor)}&limit=100";

        try
        {
            for (var page = 0; page < MaxPages; page++)
            {
                var response = await transport.SendAsync(account, HttpMethod.Get, path, cancellationToken).ConfigureAwait(false);

                if (response.ValueKind == JsonValueKind.Object
                    && response.TryGetProperty("data", out var data)
                    && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var member in data.EnumerateArray())
                    {
                        if (string.Equals(Text(member, "email"), actor, StringComparison.OrdinalIgnoreCase))
                        {
                            var role = Text(member, "role");
                            return new ClaudeAccountCheckStep(
                                ActorSubject,
                                ClaudeCheckOutcome.Passed,
                                role is null
                                    ? $"{actor} is a member of {organization}."
                                    : $"{actor} is a member of {organization} ({role}).");
                        }
                    }
                }

                var lastId = Text(response, "last_id");
                if (!Boolean(response, "has_more") || lastId is null) break;

                path = $"v1/organizations/users?email={Uri.EscapeDataString(actor)}&limit=100&after_id={Uri.EscapeDataString(lastId)}";
            }

            return new ClaudeAccountCheckStep(
                ActorSubject,
                ClaudeCheckOutcome.Failed,
                $"Nobody in {organization} has the email {actor}. The dashboard would report nothing for it.");
        }
        catch (ClaudeException ex)
        {
            return new ClaudeAccountCheckStep(ActorSubject, ClaudeCheckOutcome.Failed, ex.Message);
        }
    }

    private async Task<ClaudeAccountCheckStep> FindWorkspaceAsync(ClaudeAccount account, string organization, CancellationToken cancellationToken)
    {
        var id = account.WorkspaceId!.Trim();

        try
        {
            var workspace = await transport
                .SendAsync(account, HttpMethod.Get, $"v1/organizations/workspaces/{Uri.EscapeDataString(id)}", cancellationToken)
                .ConfigureAwait(false);

            var name = Text(workspace, "name") ?? id;
            return new ClaudeAccountCheckStep(WorkspaceSubject, ClaudeCheckOutcome.Passed, $"Reports the workspace {name}.");
        }
        catch (ClaudeException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            // The transport's own sentence for a 404 is about reports, which is what
            // every other call it carries asks for. Here a 404 has one meaning.
            return new ClaudeAccountCheckStep(
                WorkspaceSubject,
                ClaudeCheckOutcome.Failed,
                $"Anthropic has no workspace with the id {id} in {organization}.");
        }
        catch (ClaudeException ex)
        {
            return new ClaudeAccountCheckStep(WorkspaceSubject, ClaudeCheckOutcome.Failed, ex.Message);
        }
    }

    private static ClaudeAccountCheckStep NotAsked(string subject, ClaudeAccount account) => subject switch
    {
        ActorSubject => new ClaudeAccountCheckStep(subject, ClaudeCheckOutcome.Skipped, "No account named yet."),
        _ => new ClaudeAccountCheckStep(subject, ClaudeCheckOutcome.Skipped, "No workspace set - the whole organization is reported.")
    };

    /// <summary>An organization's member list is short, but the loop is bounded
    /// rather than trusted to terminate, the way the usage client's paging is.</summary>
    private const int MaxPages = 20;

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static bool Boolean(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.True;
}
