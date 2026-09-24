using Backlog.Infrastructure.Sync;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>Credential stores for the two states a device can be in.</summary>
internal static class TestDevices
{
    /// <summary>A store that already has a credential. The share and dictation
    /// tests use it too: both are about a screen that is past pairing, and
    /// neither should have to know how a device gets there.</summary>
    public static IDeviceCredentialStore Paired() => new InMemoryDeviceCredentialStore(
        new DeviceCredential(Guid.NewGuid(), Guid.NewGuid(), "Phone", "a-registration-credential"));

    public static IDeviceCredentialStore Unpaired() => new InMemoryDeviceCredentialStore();
}
