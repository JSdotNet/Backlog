namespace Backlog.UI.Components.Diagrams;

/// <summary>
/// One node of an atlas, as the parts drawn in Blazor read it.
///
/// <para>The canvas takes the host's own opaque model, the way every other
/// diagram host here does. This record is the other half: the index list and the
/// live region render real elements, so they need a typed shape rather than an
/// <c>object</c>. It carries what a reader is told about a node and nothing about
/// how it is painted — no colour, no position, no radius.</para>
///
/// <para><see cref="Status"/> and <see cref="ToneSlug"/> are separate on purpose.
/// The word is the host's vocabulary and is printed verbatim; the slug is the
/// class the stylesheet already defines. Keeping them apart is what lets a host
/// own its own status words without the library learning them, and it is the same
/// split the status select makes.</para>
/// </summary>
/// <param name="Id">Stable identity, and what a selection is reported as.</param>
/// <param name="Label">The node's name, as a reader sees it.</param>
/// <param name="Kind">What sort of thing it is, in the host's words. May be empty.</param>
/// <param name="Status">The status word, printed as given. May be empty.</param>
/// <param name="ToneSlug">The tone modifier the stylesheet dresses the mark with. Empty draws the unknown mark.</param>
/// <param name="Group">The cluster this node belongs to, named for a reader.</param>
/// <param name="InDegree">How many nodes point at this one.</param>
/// <param name="OutDegree">How many nodes this one points at.</param>
public sealed record GraphAtlasNode(
    string Id,
    string Label,
    string Kind = "",
    string Status = "",
    string ToneSlug = "",
    string Group = "",
    int InDegree = 0,
    int OutDegree = 0);
