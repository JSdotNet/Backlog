using Microsoft.Extensions.Logging;

namespace Backlog.Modules.Sync.UI;

// The logging source generator cannot see Razor-generated code, so this component's
// [LoggerMessage] methods live here rather than in its @code block (org ADR 0007).
public partial class DevicesSettings
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Revoking a device on the sync service failed unexpectedly.")]
    private static partial void LogDeviceRevokeFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The device list was not read: {Error}")]
    private static partial void LogDeviceListNotRead(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the device list from the sync service failed unexpectedly.")]
    private static partial void LogDeviceListReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "This device's key could not be opened to read how it is kept.")]
    private static partial void LogDeviceKeyNotOpened(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Registering this device with the sync service failed unexpectedly.")]
    private static partial void LogDeviceRegistrationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Pairing this device with the sync service failed unexpectedly.")]
    private static partial void LogDevicePairingFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Asking the sync service for a pairing code failed unexpectedly.")]
    private static partial void LogPairingCodeRequestFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The session sync loop is registered but could not be built; the sessions card is hidden.")]
    private static partial void LogSessionSyncNotBuilt(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Task sync is not composed in this host, so the sync section is not offered.")]
    private static partial void LogTaskSyncNotComposed(ILogger logger, Exception exception);
}
