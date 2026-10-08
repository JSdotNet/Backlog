using System.Collections.Concurrent;

using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Desktop.WebHarness;

/// <summary>
/// A connector that exists only in this harness, so the linked-task screens —
/// the source badge, its flags, the Source filter, the Connectors settings page —
/// can be driven before a real connector ships.
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
/// <para>
/// <b>A failing target.</b> A target whose name contains <c>fail</c> fails every
/// fetch as a target the source does not have, so the card's last-sync error and
/// "Sync now"'s failure message can be seen. The field's label and placeholder are
/// left at the descriptor's neutral defaults.
/// </para>
/// <para>
/// <b>Picking a target.</b> The harness offers three targets to pick, named apart
/// from their stored spelling as a spec-manager product's name is from its slug —
/// the last of them the failing one. The installed GitHub and spec-manager
/// connectors show the typed fallback: GitHub with no repository configured, and
/// spec-manager signed out.
/// </para>
/// </summary>
internal sealed class HarnessTaskConnector : ITaskConnector
{
    /// <summary>The harness account the fixture items are assigned to.</summary>
    public const string Me = "harness-user";

    /// <summary>The number of the item every completion of is refused.</summary>
    private const int RefusingItem = 5;

    /// <summary>The external ids completed in this run. Held in memory only: a
    /// restarted harness has every item open again.</summary>
    private readonly ConcurrentDictionary<string, bool> _completed = new(StringComparer.Ordinal);

    public TaskConnectorDescriptor Descriptor { get; } = new("harness", "Harness", "harness", "color-primary-light")
    {
        TargetHelp = "Any name. One containing \"fail\" fails every sync.",
    };

    public TaskConnectorCapabilities Capabilities { get; } = new(CanComplete: true);

    public Task<ConnectorTargetChoices> ListTargetChoicesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new ConnectorTargetChoices(
        [
            new("harness-alpha", "Harness alpha"),
            new("harness-beta", "Harness beta"),
            new("harness-fail", "Fails every sync"),
        ]));

    public Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        if (target.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            throw new TaskConnectorFetchException(
                TaskConnectorFetchFailure.NotFound,
                $"The harness source has no target named {target}. Connect one whose name does not contain \"fail\".");
        }

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
}
