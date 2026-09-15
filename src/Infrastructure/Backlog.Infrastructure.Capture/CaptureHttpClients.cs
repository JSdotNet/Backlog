namespace Backlog.Infrastructure.Capture;

/// <summary>The named clients this project registers, so the adapters and the
/// registration agree on a string in one place.</summary>
public static class CaptureHttpClients
{
    /// <summary>The client every feed and page fetch goes through: a timeout,
    /// a User-Agent, a resilience pipeline sized for a button press, and a
    /// primary handler with no cookie jar — see
    /// <c>CaptureAdapterRegistration</c> for each.</summary>
    public const string Feeds = "capture-feeds";
}
