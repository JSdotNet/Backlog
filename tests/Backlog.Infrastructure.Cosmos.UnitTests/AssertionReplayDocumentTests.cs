// What is testable about the assertion replay store's documents without Cosmos: the id a
// spent assertion is addressed by, the JSON names the container is partitioned on, and the
// TTL rule. TTL reaping itself is deployed-only behaviour (the emulator does not honour it),
// so what is pinned is that every document carries a ttl and that the number is right.

using System.Text.Json;
using Backlog.Infrastructure.Cosmos.AssertionReplays;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Services;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

public class AssertionReplayDocumentTests
{
    private static readonly DeviceId Device = new(Guid.Parse("3b2e2c9f-3a3d-4b5c-8d4e-6f708192a3b4"));
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    /// <summary>SHA-256 hex of <c>"&lt;device id&gt;:&lt;assertion id&gt;"</c>: a fixed
    /// length whatever the device sent as its <c>jti</c>, and nothing of it readable.</summary>
    [Fact]
    public void A_spent_assertion_is_addressed_by_the_hash_of_its_device_and_id()
    {
        var document = AssertionReplayDocumentFactory.From(Device, "jti-1", Now.AddMinutes(4), Now);

        Assert.Equal("d71a2246630635ef3f1567bef67f99be36bf7687dc8cc209a05d81537044194b", document.Id);
    }

    [Fact]
    public void The_same_assertion_id_from_another_device_is_another_document()
    {
        var mine = AssertionReplayDocumentFactory.From(Device, "jti-1", Now.AddMinutes(4), Now);
        var theirs = AssertionReplayDocumentFactory.From(DeviceId.New(), "jti-1", Now.AddMinutes(4), Now);

        Assert.NotEqual(mine.Id, theirs.Id);
    }

    [Fact]
    public void The_document_serialises_with_the_names_the_container_is_partitioned_on()
    {
        var document = AssertionReplayDocumentFactory.From(Device, "jti-1", Now.AddMinutes(4), Now);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(document, ReplicaDocumentSerialization.Options));
        var root = json.RootElement;

        Assert.Equal(document.Id, root.GetProperty("id").GetString());
        Assert.Equal(Device.Value.ToString("D"), root.GetProperty("deviceId").GetString());
        Assert.True(root.TryGetProperty("acceptableUntil", out _));
        Assert.Equal(4 * 60 + AssertionReplayDocumentFactory.ReapGraceSeconds, root.GetProperty("ttl").GetInt32());
    }

    /// <summary>The longest an assertion can be taken: issued a clock skew ahead, living
    /// its whole lifetime, and taken a clock skew after it expired. The document outlives
    /// that by the grace and no more.</summary>
    [Fact]
    public void The_longest_acceptable_assertion_is_kept_for_its_window_and_the_grace()
    {
        var longest = SyncAssertion.MaxLifetime + EcdsaDeviceAssertionVerifier.ClockSkew + EcdsaDeviceAssertionVerifier.ClockSkew;

        var document = AssertionReplayDocumentFactory.From(Device, "jti-1", Now + longest, Now);

        Assert.Equal(9 * 60 + AssertionReplayDocumentFactory.ReapGraceSeconds, document.Ttl);
    }

    /// <summary>Cosmos reads a ttl of zero or below as "expire on write", which would
    /// forget the id the moment it was spent.</summary>
    [Fact]
    public void An_id_written_after_it_lapsed_still_gets_the_grace()
    {
        var document = AssertionReplayDocumentFactory.From(Device, "jti-1", Now.AddMinutes(-1), Now);

        Assert.Equal(AssertionReplayDocumentFactory.ReapGraceSeconds, document.Ttl);
    }
}
