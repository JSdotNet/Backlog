using System.Runtime.Versioning;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The folder the two development harnesses hand a pairing code through: the
/// phone asks, the desktop answers with a code in the same OS-protected envelope
/// a stored credential gets (ADR 0030), and the phone takes it — once.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiPairingCodeHandoffTests : IDisposable
{
    private const string Code = "K7MN9PQR";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pairing-handoff-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void A_code_the_phone_asked_for_reaches_it_once_and_withdraws_the_request()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The handoff is a DPAPI envelope.");

        var phone = new DpapiPairingCodeHandoff(_folder);
        var desktop = new DpapiPairingCodeHandoff(_folder);

        Assert.False(desktop.CodeRequested);
        phone.RequestCode();
        Assert.True(desktop.CodeRequested);

        Assert.False(phone.CodeHandedOver);
        desktop.HandOver(Code);

        Assert.False(desktop.CodeRequested);
        Assert.True(phone.CodeHandedOver);
        Assert.Equal(Code, phone.TakeCode());
        Assert.False(phone.CodeHandedOver);
        Assert.Null(phone.TakeCode());
        Assert.Empty(Directory.EnumerateFiles(_folder));
    }

    /// <summary>The code on disk is an envelope, never the code in the clear.</summary>
    [Fact]
    public void The_code_is_never_written_in_the_clear()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The handoff is a DPAPI envelope.");

        var handoff = new DpapiPairingCodeHandoff(_folder);
        handoff.RequestCode();
        handoff.HandOver(Code);

        var written = File.ReadAllText(Assert.Single(Directory.EnumerateFiles(_folder)));
        Assert.DoesNotContain(Code, written, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Code)), written);
    }

    /// <summary>A fresh request discards a code left from an earlier one, which may
    /// belong to an owner the desktop has since replaced.</summary>
    [Fact]
    public void A_fresh_request_discards_a_code_left_behind()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The handoff is a DPAPI envelope.");

        var handoff = new DpapiPairingCodeHandoff(_folder);
        handoff.RequestCode();
        handoff.HandOver(Code);

        handoff.RequestCode();

        Assert.Null(handoff.TakeCode());
        Assert.True(handoff.CodeRequested);
    }

    /// <summary>A request the desktop gave up on is withdrawn, so only a fresh
    /// one is served.</summary>
    [Fact]
    public void A_request_given_up_on_is_withdrawn()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The handoff is a DPAPI envelope.");

        var handoff = new DpapiPairingCodeHandoff(_folder);
        handoff.RequestCode();

        handoff.WithdrawRequest();

        Assert.False(handoff.CodeRequested);
        Assert.Empty(Directory.EnumerateFiles(_folder));
    }

    /// <summary>An envelope this user cannot open — another profile's, or a file
    /// that is not one — is no code, and is cleared so the next request starts
    /// clean.</summary>
    [Fact]
    public void An_unreadable_code_is_no_code()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The handoff is a DPAPI envelope.");

        var handoff = new DpapiPairingCodeHandoff(_folder);
        handoff.RequestCode();
        handoff.HandOver(Code);
        File.WriteAllText(Assert.Single(Directory.EnumerateFiles(_folder)), "not an envelope");

        Assert.Null(handoff.TakeCode());
        Assert.Empty(Directory.EnumerateFiles(_folder));
    }
}
