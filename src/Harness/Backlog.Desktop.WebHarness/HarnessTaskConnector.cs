using System.Collections.Concurrent;

using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Desktop.WebHarness;

/// <summary>
/// A connector that exists only in this harness, so the linked-task screens —
/// the source badge, its flags, the Source and "Assigned to me" filters, the
/// Connectors settings page — can be driven before a real connector ships.
/// <para>
/// Nothing arrives until a target is connected on the Connectors page; every
/// target then answers the same five open items, chosen so each screen has
/// something to show: one assigned to the harness account, one the source says is
/// blocked, one carrying two plan labels and assigned to somebody else — so its
/// plan reaches the roadmap's shelf — one assigned to nobody that waits on the
/// first, and one the source refuses to complete. The links go to
/// <c>example.com</c>, which opens harmlessly.
/// </para>
/// <para>
/// <b>Completing at the source.</b> The harness can, so the Connectors page offers
/// the switch. With it on, finishing a task completes its item here — the item
/// reads as Done on every fetch after, for as long as the harness runs, so the sync
/// the write-back asks for brings the source's Done in — except item 5, which is
/// always refused, so the refusal flag can be seen on its task.
/// </para>
/// </summary>
internal sealed class HarnessTaskConnector : ITaskConnector
{
    /// <summary>The account "Assigned to me" compares against.</summary>
    public const string Me = "harness-user";

    /// <summary>The number of the item every completion of is refused.</summary>
    private const int RefusingItem = 5;

    /// <summary>The external ids completed in this run. Held in memory only: a
    /// restarted harness has every item open again.</summary>
    private readonly ConcurrentDictionary<string, bool> _completed = new(StringComparer.Ordinal);

    public TaskConnectorDescriptor Descriptor { get; } = new("harness", "Harness", "harness", "color-primary-light");

    public TaskConnectorCapabilities Capabilities { get; } = new(CanComplete: true);

    public Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        var updated = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var slug = Uri.EscapeDataString(target);

        SourceItem Item(int number, string title, string? assignee, IReadOnlyList<string>? labels = null, bool blocked = false, string? reason = null, int? waitsOn = null)
        {
            var externalId = $"{target}#{number}";
            var done = _completed.ContainsKey(externalId);

            return new(
                externalId,
                $"H-{number}",
                title,
                $"https://example.com/harness/{slug}/{number}",
                string.Empty,
                done ? NormalisedSourceState.Done : NormalisedSourceState.Open,
                done ? "Done" : "Open",
                assignee,
                updated,
                labels ?? [],
                IsBlocked: blocked && !done,
                BlockedReason: done ? null : reason,
                WaitsOn: waitsOn is { } other ? [$"{target}#{other}"] : null);
        }

        return Task.FromResult<IReadOnlyList<SourceItem>>(
        [
            Item(1, "Assigned to the harness account", Me),
            Item(2, "Blocked at the source", Me, blocked: true, reason: "Waiting on the design review"),
            Item(3, "Filed under two plans", "someone-else", ["+harness-alpha", "+harness-beta"]),
            Item(4, "Assigned to nobody", null, waitsOn: 1),
            Item(RefusingItem, "Refuses to be completed at the source", Me),
        ]);
    }

    public Task<string?> CompleteAsync(SourceRef item, CancellationToken cancellationToken)
    {
        if (item.ExternalId.EndsWith($"#{RefusingItem}", StringComparison.Ordinal))
        {
            return Task.FromResult<string?>($"The harness source refuses to complete H-{RefusingItem}.");
        }

        _completed[item.ExternalId] = true;
        return Task.FromResult<string?>(null);
    }

    public Task<string?> WhoAmIAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(Me);
}
