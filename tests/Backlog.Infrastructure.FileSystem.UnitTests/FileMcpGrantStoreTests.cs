using System.Security.Cryptography;
using System.Text.Json.Nodes;

using Backlog.SharedKernel.Mcp;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The grants the desktop app keeps in <c>mcp-grants.json</c> (local ADR 0052 §3), each with
/// a secret sealed under the Windows user the way every stored credential is (local ADR 0030).
/// </summary>
public sealed class FileMcpGrantStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 9, 30, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcp-grant-store-tests-" + Guid.NewGuid().ToString("N"));

    private string GrantsPath => Path.Combine(_root, FileMcpGrantStore.FileName);

    private FileMcpGrantStore Store() => new(GrantsPath, new FakeTimeProvider(Now));

    [Fact]
    public void A_created_grant_is_found_and_listed_at_version_1()
    {
        var store = Store();

        var grant = store.Create("Claude Code (user)", ["tasks:read", "tasks:write"], "jobsc");

        Assert.Matches("^g_[a-z2-7]{16}$", grant.Id);
        Assert.Equal("Claude Code (user)", grant.Name);
        Assert.Equal(["tasks:read", "tasks:write"], grant.Scopes);
        Assert.Equal(1, grant.Version);
        Assert.Equal("jobsc", grant.CreatedBy);
        Assert.Equal(Now, grant.CreatedAt);
        Assert.False(grant.Revoked);

        Assert.Equal(grant.Id, store.Find(grant.Id)?.Id);
        Assert.Equal([grant.Id], store.List().Select(listed => listed.Id));
    }

    [Fact]
    public void Two_grants_get_two_ids()
    {
        var store = Store();

        var first = store.Create("one", [], "jobsc");
        var second = store.Create("two", [], "jobsc");

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal([first.Id, second.Id], store.List().Select(listed => listed.Id));
    }

    [Fact]
    public void Revoking_a_grant_marks_it_revoked_and_raises_its_version()
    {
        var store = Store();
        var grant = store.Create("Copilot", ["tasks:read"], "jobsc");

        var revoked = store.Revoke(grant.Id);

        Assert.NotNull(revoked);
        Assert.True(revoked.Revoked);
        Assert.Equal(2, revoked.Version);
        Assert.True(store.Find(grant.Id)?.Revoked);
        Assert.Equal(2, store.Find(grant.Id)?.Version);
    }

    [Fact]
    public void Setting_a_grant_s_scopes_stores_them_and_raises_its_version()
    {
        var store = Store();
        var grant = store.Create("Copilot", ["tasks:read"], "jobsc");

        var changed = store.SetScopes(grant.Id, ["tasks:read", "devbook:read"]);

        Assert.NotNull(changed);
        Assert.Equal(["tasks:read", "devbook:read"], changed.Scopes);
        Assert.Equal(2, changed.Version);
        Assert.False(changed.Revoked);
        Assert.Equal(["tasks:read", "devbook:read"], Store().Find(grant.Id)?.Scopes);
        Assert.Equal(2, Store().Find(grant.Id)?.Version);
    }

    [Fact]
    public void A_revoked_or_unknown_grant_takes_no_scopes()
    {
        var store = Store();
        var grant = store.Create("Copilot", ["tasks:read"], "jobsc");
        store.Revoke(grant.Id);
        var raised = 0;
        store.Changed += () => raised++;

        Assert.Null(store.SetScopes(grant.Id, ["tasks:write"]));
        Assert.Null(store.SetScopes("g_aaaaaaaaaaaaaaaa", ["tasks:write"]));

        Assert.Equal(["tasks:read"], store.Find(grant.Id)?.Scopes);
        Assert.Equal(2, store.Find(grant.Id)?.Version);
        Assert.Equal(0, raised);
    }

    [Theory]
    [InlineData("devbook:decide")]
    [InlineData("tasks:reed")]
    public void A_scope_no_grant_may_hold_is_refused_and_the_grant_is_left_as_it_was(string scope)
    {
        var store = Store();
        var grant = store.Create("Copilot", ["tasks:read"], "jobsc");
        var raised = 0;
        store.Changed += () => raised++;

        var refusal = Assert.Throws<ArgumentException>(() => store.SetScopes(grant.Id, ["tasks:read", scope]));

        Assert.Contains(scope, refusal.Message, StringComparison.Ordinal);
        Assert.Equal(["tasks:read"], Store().Find(grant.Id)?.Scopes);
        Assert.Equal(1, Store().Find(grant.Id)?.Version);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Setting_scopes_raises_Changed()
    {
        var store = Store();
        var grant = store.Create("one", [], "jobsc");
        var raised = 0;
        store.Changed += () => raised++;

        store.SetScopes(grant.Id, ["tasks:read"]);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void An_unknown_grant_is_not_found_and_revokes_nothing()
    {
        var store = Store();

        Assert.Null(store.Find("g_aaaaaaaaaaaaaaaa"));
        Assert.Null(store.Revoke("g_aaaaaaaaaaaaaaaa"));
        Assert.Empty(store.List());
        Assert.False(File.Exists(GrantsPath));
    }

    [Fact]
    public void A_fresh_store_reads_the_grants_another_one_wrote()
    {
        var written = Store().Create("question hook", ["notify:ask"], "jobsc");
        Store().Revoke(Store().Create("telemetry", ["telemetry:write"], "jobsc").Id);

        var read = Store().List();

        Assert.Equal(2, read.Count);
        Assert.Equal(written.Id, read[0].Id);
        Assert.Equal(["notify:ask"], read[0].Scopes);
        Assert.Equal(Now, read[0].CreatedAt);
        Assert.False(read[0].Revoked);
        Assert.True(read[1].Revoked);
        Assert.Equal(2, read[1].Version);
    }

    [Fact]
    public void Creating_and_revoking_raise_Changed()
    {
        var store = Store();
        var raised = 0;
        store.Changed += () => raised++;

        var grant = store.Create("one", [], "jobsc");
        store.Revoke(grant.Id);
        store.Revoke("g_aaaaaaaaaaaaaaaa");

        Assert.Equal(2, raised);
    }

    [Fact]
    public void A_grant_secret_is_32_random_bytes_sealed_under_this_user()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is Windows-only.");

        var store = Store();
        var grant = store.Create("one", [], "jobsc");

        var stored = StoredSecret(grant.Id);
        var secret = store.OpenSecret(grant.Id);

        Assert.StartsWith("dpapi:", stored, StringComparison.Ordinal);
        Assert.NotNull(secret);
        Assert.Equal(32, Base64UrlDecode(secret).Length);
        Assert.DoesNotContain(secret, File.ReadAllText(GrantsPath), StringComparison.Ordinal);
    }

    [Fact]
    public void A_record_written_without_a_secret_gets_one_sealed_on_first_read()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is Windows-only.");

        WriteHandSeeded(secret: null);

        var grant = Store().Find("g_handseeded000000");

        Assert.NotNull(grant);
        Assert.Equal(3, grant.Version);
        Assert.Equal(["tasks:read"], grant.Scopes);
        Assert.StartsWith("dpapi:", StoredSecret("g_handseeded000000"), StringComparison.Ordinal);
        Assert.Equal(32, Base64UrlDecode(Store().OpenSecret("g_handseeded000000")!).Length);
    }

    [Fact]
    public void A_plaintext_secret_is_sealed_on_first_read()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is Windows-only.");

        var plaintext = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        WriteHandSeeded(plaintext);

        _ = Store().List();

        Assert.StartsWith("dpapi:", StoredSecret("g_handseeded000000"), StringComparison.Ordinal);
        Assert.Equal(plaintext, Store().OpenSecret("g_handseeded000000"));
    }

    [Fact]
    public void An_envelope_this_user_cannot_open_reads_as_no_secret_and_is_left_alone()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is Windows-only.");

        const string Unopenable = "dpapi:dGhpcyBpcyBiYXNlNjQgYnV0IG5vdCBhIERQQVBJIGVudmVsb3Bl";
        WriteHandSeeded(Unopenable);

        var store = Store();

        Assert.NotNull(store.Find("g_handseeded000000"));
        Assert.Null(store.OpenSecret("g_handseeded000000"));
        Assert.Equal(Unopenable, StoredSecret("g_handseeded000000"));
    }

    [Fact]
    public void A_file_that_does_not_parse_reads_as_no_grants()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(GrantsPath, "{ not json");

        Assert.Empty(Store().List());
        Assert.Null(Store().Find("g_aaaaaaaaaaaaaaaa"));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"grants\":null}")]
    [InlineData("{\"grants\":[{\"id\":\"g_handseeded000000\",\"version\":\"2\"}]}")]
    [InlineData("{\"grants\":[{\"id\":\"g_handseeded000000\",\"version\":3,},]}")]
    public void A_file_that_does_not_parse_is_never_written_over(string unreadable)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(GrantsPath, unreadable);
        var store = Store();

        Assert.Throws<UnreadableMcpGrantFileException>(() => store.Create("one", [], "jobsc"));
        Assert.Throws<UnreadableMcpGrantFileException>(() => store.Revoke("g_handseeded000000"));
        Assert.Equal(unreadable, File.ReadAllText(GrantsPath));
    }

    [Fact]
    public void A_record_with_a_blank_id_is_kept_on_save_and_never_listed()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(GrantsPath, "{\"grants\":[{\"id\":\"\",\"name\":\"half-written\",\"secret\":\"dpapi:x\"}]}");
        var store = Store();

        var grant = store.Create("one", [], "jobsc");

        Assert.Equal([grant.Id], store.List().Select(listed => listed.Id));
        var names = JsonNode.Parse(File.ReadAllText(GrantsPath))!["grants"]!.AsArray().Select(record => (string?)record!["name"]);
        Assert.Equal(["half-written", "one"], names);
    }

    [Fact]
    public void Members_this_release_does_not_know_are_kept_on_save()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            GrantsPath,
            "{\"policy\":{\"maxGrants\":5},\"grants\":[{\"id\":\"g_handseeded000000\",\"name\":\"x\",\"secret\":\"dpapi:x\",\"rotatedAt\":\"2026-11-01\"}]}");

        Store().Revoke("g_handseeded000000");

        var file = JsonNode.Parse(File.ReadAllText(GrantsPath))!;
        Assert.Equal(5, (int?)file["policy"]?["maxGrants"]);
        Assert.Equal("2026-11-01", (string?)file["grants"]![0]!["rotatedAt"]);
        Assert.True((bool?)file["grants"]![0]!["revoked"]);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A record as a person writes it by hand for a check: no secret, or the one
    /// given, at version 3.</summary>
    private void WriteHandSeeded(string? secret)
    {
        var record = new JsonObject
        {
            ["id"] = "g_handseeded000000",
            ["name"] = "hand-seeded",
            ["scopes"] = new JsonArray("tasks:read"),
            ["version"] = 3,
            ["createdBy"] = "jobsc",
            ["createdAt"] = "2026-11-01T08:00:00+00:00",
            ["revoked"] = false
        };

        if (secret is not null) record["secret"] = secret;

        Directory.CreateDirectory(_root);
        File.WriteAllText(GrantsPath, new JsonObject { ["grants"] = new JsonArray(record) }.ToJsonString());
    }

    private string? StoredSecret(string id) =>
        JsonNode.Parse(File.ReadAllText(GrantsPath))!["grants"]!.AsArray()
            .Single(record => (string?)record!["id"] == id)!["secret"]?.GetValue<string>();

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
