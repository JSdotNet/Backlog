using System.Diagnostics;
using Backlog.Modules.Sync.Observability;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.Connector;

/// <summary>
/// The one place a connector refusal is counted (inherited ADR 0013: every authorization
/// failure leaves a trace, never a token). Every refusal — in place, on the redirect or from
/// the token endpoint — goes through <see cref="Record"/>, so
/// <c>backlog.sync.connector.refused</c> counts all of them, tagged by the OAuth error and
/// never by the owner.
/// </summary>
public static class ConnectorRefusals
{
    /// <summary>Counts one refusal by its OAuth error and tags the handler's activity with it.</summary>
    public static void Record(string error)
    {
        SyncTelemetry.ConnectorRefused.Add(1, new KeyValuePair<string, object?>(SyncTelemetry.ReasonTag, error));

        if (Activity.Current is { } activity && ReferenceEquals(activity.Source, SyncTelemetry.Source))
        {
            activity.SetTag(SyncTelemetry.ReasonTag, error);
        }
    }

    /// <summary>A counted refusal as a validation error carrying the OAuth error code.</summary>
    internal static Error Refused(string error, string description)
    {
        Record(error);
        return Error.Validation(error, description);
    }

    /// <summary>The activity a connector handler runs in; inert until a listener attaches.</summary>
    internal static Activity? Start(string name) => SyncTelemetry.Source.StartActivity(name, ActivityKind.Internal);
}
