namespace Backlog.UI.Components.Devbook;

/// <summary>One usage drawn on <see cref="DeliveryPathBoard"/>: a chip that may sit
/// in several cards at once — under each stage and sub-stage it works in, and in
/// each layer beneath — and is lit in all of them when picked.</summary>
/// <param name="Id">What identifies the usage across every card it sits in; the
/// board hands it back when the reader opens the usage.</param>
/// <param name="Name">The usage's own name, as its chapter heading spells it.</param>
/// <param name="Status">Its rung on the <c>.tech</c> ladder, drawn as the
/// <c>ai/</c> folder's status badge; null draws none.</param>
public sealed record DeliveryPathUsage(string Id, string Name, string? Status);

/// <summary>One card under a stage: a named sub-stage, or the stage's unnamed card
/// when the usages there name no sub-stage.</summary>
/// <param name="Name">The sub-stage's name, or null for the unnamed card.</param>
/// <param name="Usages">The usages working there, in reading order. None draws
/// "no AI".</param>
public sealed record DeliveryPathCard(string? Name, IReadOnlyList<DeliveryPathUsage> Usages)
{
    public bool IsEmpty => Usages.Count == 0;

    public bool Holds(string? id) =>
        id is not null && Usages.Any(usage => string.Equals(usage.Id, id, StringComparison.Ordinal));
}

/// <summary>One stage of the delivery path, in path order.</summary>
/// <param name="Key">The stage's word, used for its test id.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Where">Where the stage's work happens — the label beside its
/// number.</param>
/// <param name="BeforePullRequest">Whether the stage sits before the pull request.
/// The first stage that does not carries the pull-request marker.</param>
/// <param name="Cards">Its cards, at least one: the sub-stages, or one unnamed
/// card.</param>
/// <param name="Status">The stage's own shade — the highest rung among the usages
/// at it, drawn as a badge on its head — or null for an empty stage.</param>
public sealed record DeliveryPathStage(
    string Key,
    string Name,
    string Where,
    bool BeforePullRequest,
    IReadOnlyList<DeliveryPathCard> Cards,
    string? Status = null);

/// <summary>One row beneath the path — something that is not a stage but that the
/// stages stand on.</summary>
/// <param name="Key">The layer's word, used for its test id.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Usages">The usages in it. None draws "no AI".</param>
public sealed record DeliveryPathLayer(string Key, string Name, IReadOnlyList<DeliveryPathUsage> Usages)
{
    public bool IsEmpty => Usages.Count == 0;

    public bool Holds(string? id) =>
        id is not null && Usages.Any(usage => string.Equals(usage.Id, id, StringComparison.Ordinal));
}
