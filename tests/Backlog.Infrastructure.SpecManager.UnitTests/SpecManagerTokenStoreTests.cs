using System.Runtime.Versioning;
using System.Text;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// The refresh token is a long-lived secret, so what is asserted is not that it
/// round trips — a plain file would do that — but that it is not readable where it
/// is kept, and that nothing is written anywhere else.
/// <para>
/// The DPAPI tests guard on <see cref="OperatingSystem.IsWindows"/>, as
/// <c>DpapiDeviceCredentialStoreTests</c> do.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SpecManagerTokenStoreTests : IDisposable
{
    private const string BaseUrl = "https://spec.test";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "backlog-spec-manager-token-tests",
        Guid.NewGuid().ToString("n"));

    private string StorePath => Path.Combine(_root, "spec-manager-credentials.json");

    private static readonly SpecManagerCredential Credential = new(
        "client-backlog-1",
        "http://127.0.0.1/callback",
        "sma_access-secret-for-this-test",
        new DateTimeOffset(2026, 10, 5, 13, 0, 0, TimeSpan.Zero),
        "smr_refresh-secret-for-this-test",
        "Jip Jansen",
        new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));

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

    [Fact]
    public void The_tokens_survive_a_restart_and_are_never_written_in_the_clear()
    {
        if (!OperatingSystem.IsWindows()) return;

        new DpapiSpecManagerTokenStore(StorePath).Save(BaseUrl, Credential);

        // A second store over the same file is what a restart looks like.
        Assert.Equal(Credential, new DpapiSpecManagerTokenStore(StorePath).Get(BaseUrl));

        var bytes = File.ReadAllBytes(StorePath);
        foreach (var secret in new[] { Credential.AccessToken!, Credential.RefreshToken! })
        {
            Assert.False(Contains(bytes, Encoding.UTF8.GetBytes(secret)), "A token is in the file as UTF-8.");
            Assert.False(Contains(bytes, Encoding.Unicode.GetBytes(secret)), "A token is in the file as UTF-16.");
        }

        Assert.True(Contains(bytes, bytes[..8]));
    }

    [Fact]
    public void Nothing_is_written_outside_the_store_path()
    {
        if (!OperatingSystem.IsWindows()) return;

        var store = new DpapiSpecManagerTokenStore(StorePath);
        store.Save(BaseUrl, Credential);
        store.Save(BaseUrl, Credential with { RefreshToken = "smr_rotated" });
        store.Save("https://other.test", Credential);

        Assert.Equal([StorePath], Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Each_installation_is_kept_apart_and_removing_one_leaves_the_other()
    {
        if (!OperatingSystem.IsWindows()) return;

        var store = new DpapiSpecManagerTokenStore(StorePath);
        store.Save(BaseUrl, Credential);
        store.Save("https://other.test", Credential with { ClientId = "other-client" });

        store.Remove(BaseUrl);

        var reopened = new DpapiSpecManagerTokenStore(StorePath);
        Assert.Null(reopened.Get(BaseUrl));
        Assert.Equal("other-client", reopened.Get("https://other.test")?.ClientId);
    }

    [Fact]
    public void A_file_this_user_cannot_open_reads_as_nothing_kept()
    {
        if (!OperatingSystem.IsWindows()) return;

        Directory.CreateDirectory(_root);
        File.WriteAllText(StorePath, "not an envelope");

        Assert.Null(new DpapiSpecManagerTokenStore(StorePath).Get(BaseUrl));
    }

    [Fact]
    public void The_default_file_sits_beside_the_per_user_GitHub_settings()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Backlog", "spec-manager-credentials.json"),
            DpapiSpecManagerTokenStore.DefaultPath);
    }

    [Fact]
    public void A_credential_written_out_as_text_names_no_token()
    {
        var text = Credential.ToString();

        Assert.DoesNotContain("sma_", text, StringComparison.Ordinal);
        Assert.DoesNotContain("smr_", text, StringComparison.Ordinal);
        Assert.Contains("client-backlog-1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_in_memory_store_keeps_an_entry_per_installation()
    {
        var store = new InMemorySpecManagerTokenStore();
        store.Save(BaseUrl, Credential);

        Assert.Equal(Credential, store.Get(BaseUrl));
        Assert.Null(store.Get("https://other.test"));

        store.Remove(BaseUrl);
        Assert.Null(store.Get(BaseUrl));
    }

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;
}
