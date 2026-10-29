using Backlog.UI.Components.Devbook;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The AI model read into the adoption board, as <c>devbook-ai.md</c> ("The loop
/// picture", Adoption) lays it out: one row per stage, then one per layer; a card
/// for every usage with a status on the ladder, at its first stage, else its first
/// layer, naming the rest; its column its <c>status</c>, its mark its
/// <c>evidence</c>, its wait its <b>Moves up when</b>, its since its <c>date</c>.
/// </summary>
public static class AiAdoptionBoard
{
    public static AdoptionBoardModel From(AiModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var homes = new List<AdoptionHome>();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < model.Stages.Count; index++)
        {
            var stage = model.Stages[index];
            homes.Add(new AdoptionHome(
                stage.Key,
                stage.Name,
                stage.BeforePullRequest ? AdoptionHomeGroup.BeforePullRequest : AdoptionHomeGroup.AfterPullRequest,
                (index + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture)));
            names[stage.Key] = stage.Name;
        }

        foreach (var layer in model.Layers)
        {
            homes.Add(new AdoptionHome(layer.Key, layer.Name, AdoptionHomeGroup.Underneath));
            names[layer.Key] = layer.Name;
        }

        var cards = new List<AdoptionCard>();
        for (var index = 0; index < model.Usages.Count; index++)
        {
            var usage = model.Usages[index];
            if (Rung(usage.Status) is not { } rung) continue;

            var places = usage.Stages.Concat(usage.Layers).ToList();
            if (places.Count == 0) continue;

            cards.Add(new AdoptionCard(
                Key(index),
                places[0],
                usage.Heading,
                rung,
                Evidence(usage.Evidence),
                usage.Date,
                usage.EvidenceText,
                usage.MovesUpWhen,
                [.. places.Skip(1).Select(place => names.GetValueOrDefault(place, place))],
                usage.IsScheduled));
        }

        return new AdoptionBoardModel(homes, cards);
    }

    /// <summary>The usage a card of <see cref="From"/> was drawn from.</summary>
    public static AiModelUsage? UsageOf(AiModel model, AdoptionCard card)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(card);

        return card.Key.StartsWith(KeyPrefix, StringComparison.Ordinal)
            && int.TryParse(card.Key.AsSpan(KeyPrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)
            && index < model.Usages.Count
                ? model.Usages[index]
                : null;
    }

    private const string KeyPrefix = "usage-";

    private static string Key(int index) => KeyPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The ladder rung of a status; a status off the ladder — or none —
    /// places no card, as a stage word off the vocabulary places no usage.</summary>
    private static AdoptionRung? Rung(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "candidate" => AdoptionRung.Candidate,
        "trial" => AdoptionRung.Trial,
        "adopted" => AdoptionRung.Adopted,
        "hold" => AdoptionRung.Hold,
        "retired" => AdoptionRung.Retired,
        _ => null
    };

    private static AdoptionEvidence Evidence(string? evidence) => evidence switch
    {
        "recorded" => AdoptionEvidence.Recorded,
        "partial" => AdoptionEvidence.Partial,
        "fired" => AdoptionEvidence.Fired,
        "none" => AdoptionEvidence.None,
        _ => AdoptionEvidence.Unstated
    };
}
