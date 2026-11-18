namespace Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

/// <summary>
/// One note of the owner's Brain, or one chapter of a note, reduced to what a roadmap
/// gathering reads: where it is, what it is called, the effort it registered, and the
/// Roadmap Tags it names (local ADR 0053 §5). Its content never enters the plan.
/// </summary>
/// <param name="Path">The note's path inside the Brain folder, with <c>/</c>
/// separators.</param>
/// <param name="Slug">The chapter's heading slug, or null for the note as a whole —
/// its file and its top heading, which the <c>meta</c> block under the top heading
/// speaks for.</param>
/// <param name="Title">What to show for it.</param>
/// <param name="Effort">The story points its <c>meta</c> block registered, or null
/// when it registered none.</param>
/// <param name="Roadmap">The Roadmap Tags its <c>meta</c> block's <c>roadmap</c> list
/// names.</param>
/// <param name="TitleSlug">For the note as a whole, the slug of its top heading, so a
/// reference written to that heading — <c>#brain:&lt;path&gt;#&lt;top-slug&gt;</c> — meets
/// the note rather than missing it. Null for a chapter, and for a note with no top
/// heading.</param>
public sealed record RoadmapBrainNote(
    string Path,
    string? Slug,
    string Title,
    int? Effort,
    IReadOnlyList<string> Roadmap,
    string? TitleSlug = null);
