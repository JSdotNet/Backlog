using System.Net;

using Backlog.Infrastructure.Sync.Sessions;
using Backlog.Modules.Sessions.Abstractions;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The push reads its sessions at full depth, and must go on doing so.
/// <para>
/// The session source can answer a timestamps-only reading — the same sessions with
/// nothing a transcript is opened for — and a record built from one would carry no
/// branch, no turn count and no work to every other machine. This pins the depth the
/// push asks for, beside the push's own tests rather than inside them, so the guard
/// is found under its own name.
/// </para>
/// </summary>
public sealed class SessionSyncReadingDepthTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_push_reads_its_sessions_at_full_depth()
    {
        var handler = new StubHttpMessageHandler((request, _) => request.Method == HttpMethod.Post
            ? StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}""")
            : StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"sessions":[],"since":"cursor-1","hasMore":false}"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };

        var source = new StubAgentSessionSource(AgentSessions.Local());
        var session = new SessionSyncSession(
            new SessionSyncClient(http),
            source,
            new StubSessionRepositoryAliases(),
            new InMemorySessionSyncStateStore(),
            new InMemoryReplicatedSessionStore(),
            new InMemoryDeviceCredentialStore(new DeviceCredential(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "Workshop PC",
                "a-registration-credential")),
            new FakeTimeProvider(Noon));

        await session.PushAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(source.Queries);
        Assert.All(source.Queries, query => Assert.Equal(AgentSessionDetail.Full, query.Detail));
    }
}
