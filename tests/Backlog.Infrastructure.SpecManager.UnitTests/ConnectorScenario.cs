using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Infrastructure.SpecManager.OAuth;
using Backlog.Modules.Tasks.Abstractions.Connectors;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// The connector over the stub installation, composed the way
/// <see cref="SpecManagerRegistration.AddSpecManager"/> composes it, with the clock
/// stopped at noon on 5 October 2026 and the browser a delegate the test supplies.
/// </summary>
internal sealed class ConnectorScenario : IDisposable
{
    public const string ClientId = "client-backlog-1";

    public ConnectorScenario()
    {
        Options = new SpecManagerOptions { BaseUrl = StubSpecManager.BaseUrl };
        SignIn = new SpecManagerSignIn(
            Server.CreateClient,
            Store,
            Options,
            Time,
            (uri, cancellationToken) => Browser(uri, cancellationToken));
        Client = new SpecManagerClient(Server.CreateClient, SignIn, Options);
        Connector = new SpecManagerConnector(
            Client,
            SignIn,
            Microsoft.Extensions.Options.Options.Create(Options),
            Time,
            Targets);
    }

    public StubSpecManager Server { get; } = StubSpecManager.WithRecordedResponses();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));

    public FlakyTokenStore Store { get; } = new();

    public ConnectedTargetList Targets { get; } = new(new ConnectedTarget(SpecManagerConnector.ConnectorId, StubSpecManager.Product));

    public SpecManagerOptions Options { get; }

    public SpecManagerSignIn SignIn { get; }

    public SpecManagerClient Client { get; }

    public SpecManagerConnector Connector { get; }

    /// <summary>What "opening the browser" does; nothing unless a test says.</summary>
    public Func<Uri, CancellationToken, Task> Browser { get; set; } = (_, _) => Task.CompletedTask;

    /// <summary>Puts a signed-in entry in the store, its access token valid for
    /// <paramref name="validFor"/> (an hour unless said).</summary>
    public void SignedIn(
        string accessToken = "sma_current",
        string refreshToken = "smr_current",
        TimeSpan? validFor = null,
        string? accountName = "Jip Jansen") =>
        Store.Save(Options.Root, new SpecManagerCredential(
            ClientId,
            SpecManagerSignIn.RegisteredRedirectUri,
            accessToken,
            Time.GetUtcNow() + (validFor ?? TimeSpan.FromHours(1)),
            refreshToken,
            accountName,
            Time.GetUtcNow().AddDays(-1)));

    /// <summary>Puts a registration with no sign-in in the store, as a sign-out
    /// leaves it.</summary>
    public void Registered() =>
        Store.Save(Options.Root, new SpecManagerCredential(ClientId, SpecManagerSignIn.RegisteredRedirectUri));

    /// <summary>Answers the product's backlog from fixtures: the archive from
    /// <paramref name="archived"/>, the rest from <paramref name="current"/>.</summary>
    public void Backlog(string current = "backlog.json", string archived = "backlog-archived-since.json") =>
        Server.Route(HttpMethod.Get, $"/api/producten/{StubSpecManager.Product}/backlog", (request, _) =>
            StubSpecManager.Respond(
                System.Net.HttpStatusCode.OK,
                Fixtures.Read(request.RequestUri!.Query.Contains("gearchiveerd=true", StringComparison.Ordinal) ? archived : current)));

    public async Task<IReadOnlyList<SourceItem>> FetchAsync(DateTimeOffset? since = null)
    {
        SignedIn();
        Backlog();
        return await Connector.FetchAsync(StubSpecManager.Product, since, TestContext.Current.CancellationToken);
    }

    public void Dispose() => SignIn.Dispose();
}

/// <summary>An in-memory token store whose saves can be made to fail, the way a
/// locked or full disk fails the DPAPI one.</summary>
internal sealed class FlakyTokenStore : ISpecManagerTokenStore
{
    private readonly InMemorySpecManagerTokenStore _inner = new();

    public bool FailSaves { get; set; }

    public int Saves { get; private set; }

    public SpecManagerCredential? Get(string baseUrl) => _inner.Get(baseUrl);

    public void Save(string baseUrl, SpecManagerCredential credential)
    {
        if (FailSaves) throw new IOException("The disk is full.");
        Saves++;
        _inner.Save(baseUrl, credential);
    }

    public void Remove(string baseUrl) => _inner.Remove(baseUrl);
}

/// <summary>The connected targets, held in a list.</summary>
internal sealed class ConnectedTargetList(params IEnumerable<ConnectedTarget> targets) : IConnectedTargets
{
    private readonly List<ConnectedTarget> _targets = [.. targets];

    public event Action? Changed;

    public IReadOnlyList<ConnectedTarget> List() => [.. _targets];

    public ConnectedTarget? Get(string connectorId, string target) =>
        _targets.FirstOrDefault(candidate => candidate.Is(connectorId, target));

    public string? Save(ConnectedTarget target)
    {
        _targets.RemoveAll(candidate => candidate.Is(target.ConnectorId, target.Target));
        _targets.Add(target);
        Changed?.Invoke();
        return null;
    }

    public string? Update(string connectorId, string target, Func<ConnectedTarget, ConnectedTarget> change)
    {
        var index = _targets.FindIndex(candidate => candidate.Is(connectorId, target));
        if (index < 0) return null;
        _targets[index] = change(_targets[index]);
        Changed?.Invoke();
        return null;
    }

    public string? Remove(string connectorId, string target)
    {
        _targets.RemoveAll(candidate => candidate.Is(connectorId, target));
        Changed?.Invoke();
        return null;
    }

    public void Clear() => _targets.Clear();
}
