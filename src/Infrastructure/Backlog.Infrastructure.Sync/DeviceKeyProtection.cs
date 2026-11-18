namespace Backlog.Infrastructure.Sync;

/// <summary>How well a device key is kept from being copied off this machine.</summary>
public enum DeviceKeyProtection
{
    /// <summary>In a hardware module — the TPM, or a phone's StrongBox or trusted
    /// execution environment — that signs without ever handing the private key out, so
    /// a copy of the profile cannot sign.</summary>
    Hardware,

    /// <summary>In software, sealed to the signed-in user. Nothing reads it out
    /// through the API, but a full copy of the user's profile carries it.</summary>
    Software,
}
