using System.Net;

using Backlog.Infrastructure.SpecManager.Questions;
using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// The question source's parts over the stub installation: the connector's scenario —
/// signed in as Jip Jansen, the members fixture saying so — the ledger of notifications,
/// and a lookup that knows which items became linked tasks.
/// </summary>
internal sealed class QuestionScenario : IDisposable
{
    public const string QuestionsPath = $"/api/products/{StubSpecManager.Product}/backlog-questions";
    public const string MembersPath = $"/api/products/{StubSpecManager.Product}/backlog-members";

    /// <summary>Jip Jansen, whom the members fixture calls <c>isYou</c>.</summary>
    public const string Me = "30000000-0000-0000-0000-000000000001";

    public const string AssignedToMe = "40000000-0000-0000-0000-000000000001";
    public const string Unassigned = "40000000-0000-0000-0000-000000000002";
    public const string AssignedToKees = "40000000-0000-0000-0000-000000000003";
    public const string AnsweredByKees = "40000000-0000-0000-0000-000000000005";

    public QuestionScenario()
    {
        Connector.SignedIn();
        Connector.Server.Json(HttpMethod.Get, MembersPath, "members.json");
        Questions("questions-open.json", "questions-answered.json");

        Sync = new SpecManagerQuestionSync(
            Connector.Client,
            Connector.SignIn,
            Connector.Targets,
            Ledger,
            Ledger,
            Linked,
            Connector.Time,
            defaultInstallation: false);
    }

    public ConnectorScenario Connector { get; } = new();

    public NotificationLedger Ledger { get; } = new();

    public FakeLinkedWork Linked { get; } = new();

    public SpecManagerQuestionSync Sync { get; }

    public string Root => Connector.Options.Root;

    public Guid IdOf(string questionId) => SpecManagerQuestionIds.For(Root, questionId);

    /// <summary>Answers the product's questions from fixtures, by the state asked for.</summary>
    public void Questions(string open, string answered) =>
        Connector.Server.Route(HttpMethod.Get, QuestionsPath, (request, _) =>
            StubSpecManager.Respond(
                HttpStatusCode.OK,
                request.RequestUri!.Query.Contains("state=answered", StringComparison.Ordinal)
                    ? (answered.StartsWith('[') ? answered : Fixtures.Read(answered))
                    : (open.StartsWith('[') ? open : Fixtures.Read(open))));

    /// <summary>The product's only target, with "Only questions assigned to me" set as
    /// <paramref name="only"/>.</summary>
    public void OnlyAssignedToMe(bool only) =>
        Connector.Targets.Update(SpecManagerConnector.ConnectorId, StubSpecManager.Product, target => target with { OnlyQuestionsAssignedToMe = only });

    public void Dispose() => Connector.Dispose();
}

/// <summary>Which items became linked tasks, and the session each names.</summary>
internal sealed class FakeLinkedWork : ISpecManagerLinkedWorkLookup
{
    public Dictionary<string, SpecManagerLinkedWork> Items { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<SpecManagerLinkedWork?> FindAsync(string itemId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.TryGetValue(itemId, out var work) ? work : null);
}
