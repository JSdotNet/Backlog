using System.Globalization;

using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Infrastructure.SpecManager;

/// <summary>
/// A product's statuses and labels as one fetch reads them, and when they were
/// read. The status order is what decides Open from Active.
/// </summary>
internal sealed class ProductCatalog
{
    private readonly BacklogstatusDto? _firstWorkStatus;
    private readonly BacklogstatusDto? _firstStatus;

    public ProductCatalog(IReadOnlyList<BacklogstatusDto> statuses, IReadOnlyList<BackloglabelDto> labels, DateTimeOffset loadedAt)
    {
        var ordered = statuses.OrderBy(status => status.Volgorde).ToList();

        Statuses = ordered.ToDictionary(status => status.Id, StringComparer.OrdinalIgnoreCase);
        Labels = labels.ToDictionary(label => label.Id, label => label.Naam, StringComparer.OrdinalIgnoreCase);
        LoadedAt = loadedAt;

        _firstWorkStatus = ordered.FirstOrDefault(status => status.IsWorkStatus && !status.IsEindstatus);
        _firstStatus = ordered.FirstOrDefault(status => !status.IsEindstatus);
    }

    public IReadOnlyDictionary<string, BacklogstatusDto> Statuses { get; }

    public IReadOnlyDictionary<string, string> Labels { get; }

    public DateTimeOffset LoadedAt { get; }

    /// <summary>Whether every status and label <paramref name="item"/> names is
    /// one this catalog has.</summary>
    public bool Knows(BacklogitemDto item) =>
        Statuses.ContainsKey(item.StatusId)
        && (item.LabelIds ?? []).All(Labels.ContainsKey);

    /// <summary>
    /// The normalised state of an item in <paramref name="status"/>, archive aside.
    /// An end status is Done. Otherwise the first status flagged as work — in
    /// development, with an agent, in review — and everything after it is Active,
    /// and everything before it Open; with no flagged status, the first status is
    /// Open and the rest are Active. A status this catalog does not have is Open:
    /// not started is the reading that claims the least.
    /// </summary>
    public NormalisedSourceState StateOf(BacklogstatusDto? status)
    {
        if (status is null) return NormalisedSourceState.Open;
        if (status.IsEindstatus) return NormalisedSourceState.Done;

        if (_firstWorkStatus is not null)
        {
            return status.Volgorde >= _firstWorkStatus.Volgorde ? NormalisedSourceState.Active : NormalisedSourceState.Open;
        }

        return _firstStatus is null || string.Equals(status.Id, _firstStatus.Id, StringComparison.OrdinalIgnoreCase)
            ? NormalisedSourceState.Open
            : NormalisedSourceState.Active;
    }

    /// <summary>Whether an item counts as finished for whoever waits on it: archived,
    /// or in an end status.</summary>
    public bool IsFinished(BacklogitemDto item) =>
        item.IsGearchiveerd || (Statuses.TryGetValue(item.StatusId, out var status) && status.IsEindstatus);
}

/// <summary>
/// One spec-manager backlog item as a <see cref="SourceItem"/>, by the mapping the
/// connector's plan fixes.
/// </summary>
internal static class SpecManagerItemMapper
{
    /// <summary>The reason given for an impediment that names none.</summary>
    public const string UnnamedImpediment = "Impeded";

    /// <param name="item">The item.</param>
    /// <param name="catalog">The product's statuses and labels.</param>
    /// <param name="known">Every item this fetch read, by id — what a waited-on
    /// item's number and state are looked up in.</param>
    /// <param name="root">The installation's root, as the item link starts.</param>
    /// <param name="product">The product slug.</param>
    public static SourceItem Map(
        BacklogitemDto item,
        ProductCatalog catalog,
        IReadOnlyDictionary<string, BacklogitemDto> known,
        string root,
        string product)
    {
        catalog.Statuses.TryGetValue(item.StatusId, out var status);

        var state = item.IsGearchiveerd ? NormalisedSourceState.Dropped : catalog.StateOf(status);
        var waitsOn = (item.WachtOpIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        var waitedOn = waitsOn
            .Select(id => known.TryGetValue(id, out var other) ? other : null)
            .OfType<BacklogitemDto>()
            .ToList();

        // Nothing finished is in anybody's way any more, whatever is still said on it.
        var (isBlocked, reason) = state is NormalisedSourceState.Done or NormalisedSourceState.Dropped
            ? (false, null)
            : Blocked(item, waitedOn, catalog);

        return new SourceItem(
            ExternalId: item.Id,
            DisplayKey: DisplayKey(item),
            Title: item.Titel,
            Url: $"{root}/producten/{Uri.EscapeDataString(product)}/backlog?item={Uri.EscapeDataString(item.Id)}",
            Body: item.Omschrijving ?? string.Empty,
            State: state,
            SourceStateName: status?.Naam ?? string.Empty,
            Assignee: string.IsNullOrWhiteSpace(item.ToegewezenAanId) ? null : item.ToegewezenAanId,
            UpdatedAt: item.BijgewerktOp,
            Labels: Labels(item, catalog),
            Effort: item.Inspanning,
            DueOn: item.Deadline,
            // The contract's spelling: the waited-on items' external ids, every one of
            // them, read by this fetch or not. Numbers are for the reason below.
            WaitsOn: waitsOn,
            IsBlocked: isBlocked,
            BlockedReason: reason,
            References: item.Verwijzingen?.Where(reference => !string.IsNullOrWhiteSpace(reference)).ToList() ?? [],
            CompletedAt: item.AfgerondOp);
    }

    /// <summary>
    /// <c>#nummer</c>, the item's fixed number — never <c>prioriteit</c>, a rank that
    /// changes with every drag on the board — and the Jira key after it while the
    /// item is linked. Once the link is broken (<c>losgeraaktOp</c>) the key is left
    /// off: it no longer names this item at Jira, and a key that leads somewhere
    /// else is worse than none.
    /// </summary>
    public static string DisplayKey(BacklogitemDto item) =>
        item.Jira is { Sleutel.Length: > 0, LosgeraaktOp: null } jira
            ? $"{Number(item.Nummer)} · {jira.Sleutel}"
            : Number(item.Nummer);

    private static string Number(int nummer) => "#" + nummer.ToString(CultureInfo.InvariantCulture);

    /// <summary>The label names, then the issue type, then the sprint when the
    /// server names it. A label id the catalog does not have is left out.</summary>
    private static List<string> Labels(BacklogitemDto item, ProductCatalog catalog)
    {
        var labels = (item.LabelIds ?? [])
            .Select(id => catalog.Labels.TryGetValue(id, out var name) ? name : null)
            .OfType<string>()
            .ToList();

        if (!string.IsNullOrWhiteSpace(item.Issuetype)) labels.Add(item.Issuetype);
        if (!string.IsNullOrWhiteSpace(item.Sprintnaam)) labels.Add(item.Sprintnaam);

        return labels.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// An impediment on the item blocks it, with its reason. Otherwise it is blocked
    /// while it waits on an item that is not finished, named by number — the
    /// server's <c>geblokkeerd</c> deciding that when it fills it, and the items this
    /// fetch read when it does not.
    /// <para>
    /// The server's flag answers the waiting question only (spec-manager BACK-52:
    /// "at least one waited-on item is still open"); an impediment is said on the
    /// item itself and is never part of it. So the flag stands in for the derived
    /// waiting judgement and not for the impediment: <c>geblokkeerd: false</c> on an
    /// impeded item still reads as blocked.
    /// </para>
    /// </summary>
    private static (bool IsBlocked, string? Reason) Blocked(BacklogitemDto item, List<BacklogitemDto> waitedOn, ProductCatalog catalog)
    {
        if (item.Belemmering is { } belemmering)
        {
            return (true, string.IsNullOrWhiteSpace(belemmering.Reden) ? UnnamedImpediment : belemmering.Reden);
        }

        var open = waitedOn.Where(other => !catalog.IsFinished(other)).Select(other => other.Nummer).ToList();
        if (!(item.Geblokkeerd ?? open.Count > 0)) return (false, null);

        // The server can know of an open item this fetch did not read; its own list
        // names it then.
        if (open.Count == 0)
        {
            open = (item.WachtOp ?? []).Where(other => !other.Afgerond).Select(other => other.Nummer).ToList();
        }

        return (true, open.Count == 0 ? "Waits on another item" : "Waits on " + string.Join(", ", open.Select(Number)));
    }
}
