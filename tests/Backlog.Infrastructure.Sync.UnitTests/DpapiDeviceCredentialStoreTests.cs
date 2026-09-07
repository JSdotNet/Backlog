using System.Runtime.Versioning;
using System.Text;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The store ADR 0005 calls "the OS credential store". The acceptance criterion
/// behind it is not "the credential round trips" — a plaintext file would do
/// that — it is that the credential is not readable in the file, which is why
/// the first test reads the bytes rather than the record.
/// <para>
/// Every test guards on <see cref="OperatingSystem.IsWindows"/>. DPAPI is the
/// Windows half of the port and the suite runs on Windows, but a guard is
/// cheaper than a red run on the day it does not.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiDeviceCredentialStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "backlog-device-credential-tests",
        Guid.NewGuid().ToString("n"));

    private string CredentialPath => Path.Combine(_root, "device-credential.json");

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
    public void The_credential_survives_a_restart_and_is_never_written_in_the_clear()
    {
        if (!OperatingSystem.IsWindows()) return;

        var credential = new DeviceCredential(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Workshop PC",
            "Zm9yLXRoZS1zYWtlLW9mLXRoaXMtdGVzdC1vbmx5LXNlY3JldA");

        var store = new DpapiDeviceCredentialStore(CredentialPath);
        store.Save(credential);

        // A second store over the same path is what a restart looks like.
        Assert.Equal(credential, new DpapiDeviceCredentialStore(CredentialPath).Current);

        // AC 1: the secret is not sitting in the file. Both encodings are checked
        // because "not found" only means something if the search could have found
        // it — a UTF-16 write would pass a UTF-8-only scan for the wrong reason.
        var bytes = File.ReadAllBytes(CredentialPath);
        Assert.False(Contains(bytes, Encoding.UTF8.GetBytes(credential.Credential)), "The credential is in the file as UTF-8.");
        Assert.False(Contains(bytes, Encoding.Unicode.GetBytes(credential.Credential)), "The credential is in the file as UTF-16.");
        Assert.False(Contains(bytes, Encoding.UTF8.GetBytes(credential.DeviceId.ToString("D"))), "The device id is in the file in the clear.");

        // And the scan itself works: the envelope's own bytes are found by it.
        Assert.True(Contains(bytes, bytes[..8]));
    }

    [Fact]
    public void Clearing_forgets_it_here_and_on_disk()
    {
        if (!OperatingSystem.IsWindows()) return;

        var store = new DpapiDeviceCredentialStore(CredentialPath);
        store.Save(new DeviceCredential(Guid.NewGuid(), Guid.NewGuid(), "Workshop PC", "a-secret"));

        store.Clear();

        Assert.Null(store.Current);
        Assert.False(File.Exists(CredentialPath));
        Assert.Null(new DpapiDeviceCredentialStore(CredentialPath).Current);
    }

    [Fact]
    public void Saving_and_clearing_both_say_so()
    {
        if (!OperatingSystem.IsWindows()) return;

        var store = new DpapiDeviceCredentialStore(CredentialPath);
        var changes = 0;
        store.Changed += () => changes++;

        store.Save(new DeviceCredential(Guid.NewGuid(), Guid.NewGuid(), "Workshop PC", "a-secret"));
        store.Clear();

        Assert.Equal(2, changes);
    }

    /// <summary>
    /// A credential that cannot be written is not half-kept. The store used to
    /// swallow the write failure, keep the credential in memory and raise
    /// <c>Changed</c> anyway, which left the app paired until it next started
    /// and unpaired afterwards with nothing on screen having said so. It throws
    /// instead, and the pairing client turns that into a failure a person reads.
    /// <para>
    /// A directory standing where the file should be is the cheapest way to make
    /// the write fail without touching ACLs.
    /// </para>
    /// </summary>
    [Fact]
    public void A_credential_that_cannot_be_written_is_not_kept_and_nothing_says_it_changed()
    {
        if (!OperatingSystem.IsWindows()) return;

        var blocked = Path.Combine(_root, "device-credential.json");
        Directory.CreateDirectory(blocked);

        var store = new DpapiDeviceCredentialStore(blocked);
        var changes = 0;
        store.Changed += () => changes++;

        var thrown = Record.Exception(
            () => store.Save(new DeviceCredential(Guid.NewGuid(), Guid.NewGuid(), "Workshop PC", "a-secret")));

        Assert.NotNull(thrown);
        Assert.True(
            thrown is IOException or UnauthorizedAccessException,
            $"A write that cannot land should surface as an IO failure, not as {thrown.GetType().Name}.");
        Assert.Null(store.Current);
        Assert.Equal(0, changes);
    }

    /// <summary>
    /// An envelope this user cannot open reads as "no credential", not as a
    /// crash. A file copied from another profile, a half-written one and a
    /// leftover from an earlier format all arrive here, and pairing again is the
    /// only recovery from any of them.
    /// </summary>
    [Theory]
    [InlineData("this is not base64 at all !!")]
    [InlineData("dGhpcyBpcyBiYXNlNjQgYnV0IG5vdCBhIERQQVBJIGVudmVsb3Bl")]
    [InlineData("")]
    public void A_file_that_cannot_be_opened_reads_as_no_credential(string contents)
    {
        if (!OperatingSystem.IsWindows()) return;

        Directory.CreateDirectory(_root);
        File.WriteAllText(CredentialPath, contents);

        Assert.Null(new DpapiDeviceCredentialStore(CredentialPath).Current);
    }

    [Fact]
    public void An_unwritten_store_starts_unpaired_and_says_where_it_would_write()
    {
        if (!OperatingSystem.IsWindows()) return;

        var store = new DpapiDeviceCredentialStore(CredentialPath);

        Assert.Null(store.Current);
        Assert.Equal(CredentialPath, store.StorePath);
        Assert.True(Directory.Exists(_root), "The store creates its folder rather than failing on first save.");
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length) return false;

        for (var start = 0; start <= haystack.Length - needle.Length; start++)
        {
            if (haystack.AsSpan(start, needle.Length).SequenceEqual(needle)) return true;
        }

        return false;
    }
}
