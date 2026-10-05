using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Desktop.WebHarness;

/// <summary>
/// A connector that exists only in this harness, so the linked-task screens —
/// the source badge, its flags, the Source and "Assigned to me" filters, the
/// Connectors settings page — can be driven before a real connector ships.
/// <para>
/// Nothing arrives until a target is connected on the Connectors page; every
/// target then answers the same four open items, chosen so each screen has
/// something to show: one assigned to the harness account, one the source says is
/// blocked, one carrying two plan labels and assigned to somebody else — so its
/// plan reaches the roadmap's shelf — and one assigned to nobody that waits on the
/// first. The links go to <c>example.com</c>, which opens harmlessly.
/// </para>
/// </summary>
internal sealed class HarnessTaskConnector : ITaskConnector
{
    /// <summary>The account "Assigned to me" compares against.</summary>
    public const string Me = "harness-user";

    public TaskConnectorDescriptor Descriptor { get; } = new("harness", "Harness", "harness", "color-primary-light");

    public TaskConnectorCapabilities Capabilities { get; } = new();

    public Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        var updated = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var slug = Uri.EscapeDataString(target);

        SourceItem Item(int number, string title, string? assignee, IReadOnlyList<string>? labels = null, bool blocked = false, string? reason = null, int? waitsOn = null) =>
            new(
                $"{target}#{number}",
                $"H-{number}",
                title,
                $"https://example.com/harness/{slug}/{number}",
                string.Empty,
                NormalisedSourceState.Open,
                "Open",
                assignee,
                updated,
                labels ?? [],
                IsBlocked: blocked,
                BlockedReason: reason,
                WaitsOn: waitsOn is { } other ? [$"{target}#{other}"] : null);

        return Task.FromResult<IReadOnlyList<SourceItem>>(
        [
            Item(1, "Assigned to the harness account", Me),
            Item(2, "Blocked at the source", Me, blocked: true, reason: "Waiting on the design review"),
            Item(3, "Filed under two plans", "someone-else", ["+harness-alpha", "+harness-beta"]),
            Item(4, "Assigned to nobody", null, waitsOn: 1),
        ]);
    }

    public Task<string?> WhoAmIAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(Me);
}
