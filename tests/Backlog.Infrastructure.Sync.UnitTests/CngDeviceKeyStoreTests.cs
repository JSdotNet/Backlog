using System.Security.Cryptography;

using Microsoft.Extensions.Logging;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The order <see cref="CngDeviceKeyStore"/> looks for its key in (local ADR 0058 §2),
/// over stand-in providers so every failure a TPM can give is reachable on a machine
/// that has none. The rule each case holds: a TPM key that exists is never shadowed
/// by a software key made beside it, and software is used only when the TPM is
/// genuinely not there.
/// </summary>
public sealed class CngDeviceKeyStoreTests
{
    private const string KeyName = "Backlog.Sync.DeviceKey.Test";

    private static readonly int NteExists = unchecked((int)0x8009000F);
    private static readonly int NteNotSupported = unchecked((int)0x80090029);
    private static readonly int TpmDisabled = unchecked((int)0x80280006);
    private static readonly int TpmLockout = unchecked((int)0x80280921);

    [Fact]
    public void A_key_in_the_TPM_is_opened_there()
    {
        if (!OperatingSystem.IsWindows()) return;

        var platform = new FakeKeyProvider { HasKey = true };
        var software = new FakeKeyProvider { HasKey = true };
        using var store = new CngDeviceKeyStore(KeyName, platform, software);

        Assert.Equal(DeviceKeyProtection.Hardware, store.Protection);
        Assert.Equal(["exists", "open"], platform.Calls);
        Assert.Empty(software.Calls);
    }

    /// <summary>The TPM could not be asked - a lockout, a busy device - but a key
    /// made in software on an earlier start is there: that is the device's key, and
    /// opening it shadows nothing.</summary>
    [Fact]
    public void A_TPM_that_cannot_be_asked_still_finds_the_key_in_software()
    {
        if (!OperatingSystem.IsWindows()) return;

        var platform = new FakeKeyProvider { ExistsFails = TpmLockout };
        var software = new FakeKeyProvider { HasKey = true };
        var log = new CapturingLogger<CngDeviceKeyStore>();
        using var store = new CngDeviceKeyStore(KeyName, platform, software, log);

        Assert.Equal(DeviceKeyProtection.Software, store.Protection);
        Assert.Equal(["exists", "open"], software.Calls);
        Assert.Contains(log.Lines, line => line.Level == LogLevel.Warning);
    }

    /// <summary>The TPM could not be asked and software has no key either: the key may
    /// well be in the TPM, so nothing is made - a software key made now would shadow
    /// it from then on.</summary>
    [Fact]
    public void A_TPM_that_cannot_be_asked_with_no_key_in_software_throws_and_makes_nothing()
    {
        if (!OperatingSystem.IsWindows()) return;

        var platform = new FakeKeyProvider { ExistsFails = TpmLockout };
        var software = new FakeKeyProvider();
        using var store = new CngDeviceKeyStore(KeyName, platform, software);

        var thrown = Assert.Throws<CryptographicException>(store.GetOrCreate);

        Assert.Equal(TpmLockout, thrown.HResult);
        Assert.DoesNotContain("create", platform.Calls);
        Assert.DoesNotContain("create", software.Calls);
    }

    [Fact]
    public void A_machine_with_no_TPM_makes_its_key_in_software_and_says_so()
    {
        if (!OperatingSystem.IsWindows()) return;

        var platform = new FakeKeyProvider { ExistsFails = TpmDisabled };
        var software = new FakeKeyProvider();
        var log = new CapturingLogger<CngDeviceKeyStore>();
        using var store = new CngDeviceKeyStore(KeyName, platform, software, log);

        Assert.Equal(DeviceKeyProtection.Software, store.Protection);
        Assert.Equal(["exists", "create"], software.Calls);
        Assert.DoesNotContain("create", platform.Calls);
        Assert.Contains(log.Lines, line => line.Level == LogLevel.Warning);
    }

    /// <summary>A TPM key that is there but will not open - even with a code that, from
    /// the probe, would mean no TPM - is thrown rather than replaced.</summary>
    [Theory]
    [InlineData(0x80280921U)] // TPM_20_E_LOCKOUT
    [InlineData(0x80090030U)] // NTE_DEVICE_NOT_READY
    public void A_TPM_key_that_will_not_open_is_thrown_never_replaced(uint hResult)
    {
        if (!OperatingSystem.IsWindows()) return;

        var platform = new FakeKeyProvider { HasKey = true, OpenFails = unchecked((int)hResult) };
        var software = new FakeKeyProvider();
        using var store = new CngDeviceKeyStore(KeyName, platform, software);

        var thrown = Assert.Throws<CryptographicException>(store.GetOrCreate);

        Assert.Equal(unchecked((int)hResult), thrown.HResult);
        Assert.Empty(software.Calls);
    }

    /// <summary>Another start of the app made the key between the look and the
    /// create: it is opened, not made again.</summary>
    [Fact]
    public void A_key_made_by_another_start_in_the_meantime_is_opened()
    {
        if (!OperatingSystem.IsWindows()) return;

        var platform = new FakeKeyProvider { CreateFails = NteExists };
        var software = new FakeKeyProvider();
        using var store = new CngDeviceKeyStore(KeyName, platform, software);

        Assert.Equal(DeviceKeyProtection.Hardware, store.Protection);
        Assert.Equal(["exists", "create", "open"], platform.Calls);
        Assert.DoesNotContain("create", software.Calls);
    }

    /// <summary>A working TPM that answers "not supported" to the create is refusing
    /// the key it was asked for, not absent: falling back would hide that for good.</summary>
    [Fact]
    public void A_TPM_that_refuses_the_create_as_not_supported_is_thrown_not_fallen_back_from()
    {
        if (!OperatingSystem.IsWindows()) return;

        var platform = new FakeKeyProvider { CreateFails = NteNotSupported };
        var software = new FakeKeyProvider();
        using var store = new CngDeviceKeyStore(KeyName, platform, software);

        var thrown = Assert.Throws<CryptographicException>(store.GetOrCreate);

        Assert.Equal(NteNotSupported, thrown.HResult);
        Assert.DoesNotContain("create", software.Calls);
    }

    [Fact]
    public void A_TPM_switched_off_behind_its_provider_falls_back_to_software_with_a_warning()
    {
        if (!OperatingSystem.IsWindows()) return;

        var platform = new FakeKeyProvider { CreateFails = TpmDisabled };
        var software = new FakeKeyProvider();
        var log = new CapturingLogger<CngDeviceKeyStore>();
        using var store = new CngDeviceKeyStore(KeyName, platform, software, log);

        Assert.Equal(DeviceKeyProtection.Software, store.Protection);
        Assert.Contains("create", software.Calls);
        Assert.Contains(log.Lines, line => line.Level == LogLevel.Warning);
    }

    [Fact]
    public void A_key_in_the_TPM_is_made_without_a_warning()
    {
        if (!OperatingSystem.IsWindows()) return;

        var platform = new FakeKeyProvider();
        var software = new FakeKeyProvider();
        var log = new CapturingLogger<CngDeviceKeyStore>();
        using var store = new CngDeviceKeyStore(KeyName, platform, software, log);

        Assert.Equal(DeviceKeyProtection.Hardware, store.Protection);
        Assert.Equal(["exists", "create"], platform.Calls);
        Assert.DoesNotContain(log.Lines, line => line.Level >= LogLevel.Warning);
    }

    /// <summary>"Not supported" from the probe is a provider that is not there; from a
    /// create it is a TPM refusing the key, which is never a reason to fall back.</summary>
    [Fact]
    public void Not_supported_means_no_TPM_only_from_the_probe()
    {
        Assert.Equal(CngKeyFailure.PlatformUnavailable, CngKeyFailures.Classify(NteNotSupported));
        Assert.Equal(CngKeyFailure.Other, CngKeyFailures.ClassifyCreate(NteNotSupported));
        Assert.Equal(CngKeyFailure.PlatformUnavailable, CngKeyFailures.ClassifyCreate(TpmDisabled));
        Assert.Equal(CngKeyFailure.KeyExists, CngKeyFailures.ClassifyCreate(NteExists));
    }

    /// <summary>A provider whose answers and failures a test sets, recording what it
    /// was asked in order.</summary>
    private sealed class FakeKeyProvider : IDeviceKeyProvider
    {
        public bool HasKey { get; set; }

        public int? ExistsFails { get; init; }

        public int? OpenFails { get; init; }

        public int? CreateFails { get; init; }

        public List<string> Calls { get; } = [];

        public bool Exists(string keyName)
        {
            Calls.Add("exists");
            return ExistsFails is { } hr ? throw new CryptographicException(hr) : HasKey;
        }

        public ECDsa Open(string keyName)
        {
            Calls.Add("open");
            return OpenFails is { } hr ? throw new CryptographicException(hr) : ECDsa.Create(ECCurve.NamedCurves.nistP256);
        }

        public ECDsa Create(string keyName)
        {
            Calls.Add("create");
            return CreateFails is { } hr ? throw new CryptographicException(hr) : ECDsa.Create(ECCurve.NamedCurves.nistP256);
        }
    }
}
