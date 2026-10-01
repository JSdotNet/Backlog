using System.Text.Json;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Tests;

/// <summary>A delivery-run telemetry sink that keeps the name of every hook
/// event it was handed, in order.</summary>
internal sealed class RecordingTelemetry : IDeliveryRunTelemetry
{
    public List<string> Events { get; } = [];

    public Task RecordAsync(JsonElement hookEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(hookEvent.GetProperty("hook_event_name").GetString()!);

        return Task.CompletedTask;
    }
}
