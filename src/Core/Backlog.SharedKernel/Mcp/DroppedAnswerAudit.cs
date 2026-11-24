using System.Diagnostics;
using System.Globalization;

namespace Backlog.SharedKernel.Mcp;

/// <summary>
/// The <see cref="McpAudit.DroppedAnswer"/> record (local ADR 0041, ADR 0058 Phase 2), written
/// the same way by both lines that refuse an answer from a device not paired now: the
/// notification exchange, which names the notification, and the question relay, which names
/// the waiting question. Each keeps its own log line, because what happens to the answer next
/// differs between them; the device's label and the record are this class's.
/// </summary>
public static class DroppedAnswerAudit
{
    /// <summary>How a device not paired now is named in the log and the record's caller.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The device is paired: nothing is dropped.</exception>
    public static string DeviceLabel(DeviceStanding standing) =>
        standing switch
        {
            DeviceStanding.Revoked => "revoked device",
            DeviceStanding.Unknown => "unknown device",
            _ => throw new ArgumentOutOfRangeException(nameof(standing), standing, "A paired device's answer is never dropped.")
        };

    /// <summary>
    /// Records that <paramref name="deviceId"/>'s answer about <paramref name="targetId"/> was
    /// refused, when there is an audit log. A write that fails is handed to
    /// <paramref name="writeFailed"/> to log and never thrown: the answer is dropped either way.
    /// </summary>
    public static async Task AppendAsync(
        IMcpAuditLog? audit,
        DateTimeOffset at,
        DeviceStanding standing,
        Guid deviceId,
        Guid targetId,
        Action<Exception> writeFailed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writeFailed);

        if (audit is null) return;

        var record = new McpAuditRecord(
            at,
            string.Create(CultureInfo.InvariantCulture, $"{DeviceLabel(standing)} {deviceId:D}"),
            McpAudit.DroppedAnswer,
            targetId.ToString("D"),
            McpCallOutcome.Refused,
            Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"));

        try
        {
            await audit.AppendAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            writeFailed(exception);
        }
    }
}
