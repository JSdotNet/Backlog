using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features;

/// <summary>
/// The one rule both ways into the device table share: a device has to be
/// called something, and not something unbounded. Registration and pairing
/// would otherwise each carry their own copy of it, and only one of them would
/// get fixed.
/// </summary>
internal static class DeviceName
{
    /// <summary>Long enough for "Job's ThinkPad in the study", short enough that
    /// a device list stays a list.</summary>
    internal const int MaxLength = 100;

    internal static Result<string> Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(
                SyncErrorCodes.DeviceNameRequired,
                "A device needs a name so it can be told apart from the others.");
        }

        var trimmed = name.Trim();

        return trimmed.Length > MaxLength
            ? trimmed[..MaxLength]
            : trimmed;
    }
}
