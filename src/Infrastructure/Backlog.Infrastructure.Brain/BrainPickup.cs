using System.Text;

using Backlog.Modules.Brain.Abstractions;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.Brain;

/// <summary>
/// Answers <see cref="IBrainPickup"/>: a Brain note made into backlog work, over
/// Tasks' published <see cref="ITaskItems"/>, the Inbox's
/// <see cref="IInboxPlanDrafter"/> — the drafter Create plan asks — and the Brain's
/// own <see cref="IBrainNoteLinkWriter"/>.
/// <para>
/// Here, beside the Inbox's To Brain join, because only an adapter may see the
/// Brain, Tasks and the Inbox at once (local ADR 0040 §1). The work is always made
/// before the link is written, and a link the writer refuses never undoes the work:
/// the task or plan is real, so the answer says the note was not linked and why, and
/// the person repairs the note's <c>meta</c> block (local ADR 0052 §4).
/// </para>
/// <para>
/// A task is entry text through the ordinary save, born <c>!draft</c>, and gets its
/// <c>#brain:</c> reference written beside the text, as the task pane writes one
/// (local ADR 0053 §2). A plan is the drafter's task entries under one
/// <c>plan</c> entry this adapter writes itself — titled from the note, carrying the
/// plan's Roadmap Tag and the note as its Knowledge Ref — so the roadmap item points
/// back at the note however the model wrote its answer. The drafter may name only
/// the repositories the note's <c>repos</c> field names; a plan naming another is
/// refused whole before the import could register it, as Create plan refuses one.
/// </para>
/// </summary>
public sealed class BrainPickup : IBrainPickup
{
    /// <summary>What both actions answer while no Brain folder is set.</summary>
    public const string NoBrain = "Set up your Brain in Settings › Storage first.";

    /// <summary>What both actions answer in a host that composes no backlog.</summary>
    public const string NoBacklog = "The backlog is not available here.";

    /// <summary>What Plan this project answers in a host with no plan drafter.</summary>
    public const string NoDrafter = "No plan drafter is configured. Set up Azure Foundry in Settings to draft plans.";

    /// <summary>What Plan this project answers on a note outside the projects area.</summary>
    public const string NotAProject = "Only a note under projects/ can be planned as a project.";

    /// <summary>The kind a drafter is told a Brain note is.</summary>
    internal const string NoteKind = "note";

    /// <summary>The most linked notes a plan request carries, so a note that links
    /// half its Brain does not become a prompt the model cannot take.</summary>
    internal const int MaxLinkedNotes = 10;

    private readonly ITaskItems? _tasks;
    private readonly IBrainFolderSource _brain;
    private readonly IBrainNoteLinkWriter _links;
    private readonly IInboxPlanDrafter? _drafter;
    private readonly IRepositoryDirectory? _repositories;

    public BrainPickup(
        ITaskItems? tasks,
        IBrainFolderSource brain,
        IBrainNoteLinkWriter links,
        IInboxPlanDrafter? drafter = null,
        IRepositoryDirectory? repositories = null)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(links);

        _tasks = tasks;
        _brain = brain;
        _links = links;
        _drafter = drafter;
        _repositories = repositories;
    }

    /// <summary>What a pickup answers when the note's <c>repos</c> names a
    /// repository this workspace does not have, after the name.</summary>
    public const string UnknownRepository = "is not a repository this workspace has. Fix the note's repos field, or add the repository in Settings.";

    /// <summary>
    /// The note's <c>repos</c> as the registry's ids, or the first one it does not
    /// know. Asked read-only, as a batch route asks: the field is free text, and a
    /// typo must not become a repository the import registers.
    /// </summary>
    private (IReadOnlyList<string> Ids, string? Unknown) ResolveRepos(IReadOnlyList<string> repos)
    {
        var ids = new List<string>(repos.Count);
        foreach (var repo in repos)
        {
            if (_repositories?.Resolve(repo) is not { } known) return ([], repo);
            if (!ids.Contains(known.Id, StringComparer.OrdinalIgnoreCase)) ids.Add(known.Id);
        }

        return (ids, null);
    }

    public string? CreateTaskUnavailableReason =>
        _brain.BrainRoot is null ? NoBrain
        : _tasks is null ? NoBacklog
        : null;

    public string? PlanUnavailableReason =>
        CreateTaskUnavailableReason
        ?? (_drafter is null ? NoDrafter
            : !_drafter.IsAvailable ? _drafter.UnavailableReason ?? NoDrafter
            : null);

    public async Task<BrainPickupResult> CreateTaskAsync(string notePath, string? line = null, CancellationToken cancellationToken = default)
    {
        if (CreateTaskUnavailableReason is { } unavailable) return BrainPickupResult.Failed(unavailable);
        var tasks = _tasks!;

        if (await ReadNoteAsync(notePath, cancellationToken).ConfigureAwait(false) is not { } note)
        {
            return BrainPickupResult.Failed(BrainNoteLinkWriter.NotANote);
        }

        var (repos, unknownRepo) = ResolveRepos(note.Repos);
        if (unknownRepo is not null) return BrainPickupResult.Failed($"The task was not created: `{unknownRepo}` {UnknownRepository}");

        var title = BrainPickupText.TitleFromLine(line) ?? note.Title;
        var reference = BrainNoteAddress.Format(note.Path);
        IReadOnlyList<string?> targets = repos.Count == 0 ? [null] : [.. repos];

        // Appended after everything the person has ranked, as a routed item is.
        var order = (await tasks.ListAsync(cancellationToken).ConfigureAwait(false)).Count;
        var created = new List<Guid>(targets.Count);

        foreach (var repo in targets)
        {
            var saved = await tasks.SaveFromTextAsync(null, TaskText(title, repo), order++, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
            {
                return created.Count == 0
                    ? BrainPickupResult.Failed($"The task was not created: {saved.Error.Message}")
                    : await LinkTasksAsync(note.Path, created, $"Only {created.Count} of {targets.Count} tasks were created: {saved.Error.Message}", cancellationToken).ConfigureAwait(false);
            }

            var id = saved.Value.Entry.Id;
            created.Add(id);

            var referenced = await tasks.SetDevbookReferencesAsync(id, [reference], cancellationToken).ConfigureAwait(false);
            if (referenced.IsFailure)
            {
                return await LinkTasksAsync(note.Path, created, $"The task does not point back at the note: {referenced.Error.Message}", cancellationToken).ConfigureAwait(false);
            }
        }

        return await LinkTasksAsync(note.Path, created, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BrainPickupResult> PlanProjectAsync(string notePath, CancellationToken cancellationToken = default)
    {
        if (PlanUnavailableReason is { } unavailable) return BrainPickupResult.Failed(unavailable);
        var tasks = _tasks!;
        var drafter = _drafter!;

        if (!BrainPickupText.IsProjectNote(notePath, ProjectsFolder())) return BrainPickupResult.Failed(NotAProject);

        if (await ReadNoteAsync(notePath, cancellationToken).ConfigureAwait(false) is not { } note)
        {
            return BrainPickupResult.Failed(BrainNoteLinkWriter.NotANote);
        }

        var (repos, unknownRepo) = ResolveRepos(note.Repos);
        if (unknownRepo is not null) return BrainPickupResult.Failed($"The plan was not drafted: `{unknownRepo}` {UnknownRepository}");
        note = note with { Repos = repos };

        var planId = Guid.NewGuid();
        var tag = InboxPlanTag.For(note.Title, planId);
        var body = await RequestBodyAsync(note, cancellationToken).ConfigureAwait(false);

        var draft = await drafter.DraftAsync(
            new InboxPlanDraftRequestDto(planId, note.Title, body, SourceUrl: null, NoteKind, [], note.Repos, tag),
            cancellationToken).ConfigureAwait(false);
        if (draft.IsFailure) return BrainPickupResult.Failed($"The plan was not drafted: {draft.Error.Message}");

        var steps = EntryTextParser.SplitSegments(draft.Value.PlanMarkdown)
            .Where(segment => EntryTextParser.Parse(segment) is { Kind: not EntryKind.Plan } parsed && !string.IsNullOrWhiteSpace(parsed.Title))
            .Select(step => AsStep(step, tag))
            .ToList();
        if (steps.Count == 0) return BrainPickupResult.Failed(InboxErrors.PlanEmpty.Message);

        // The one check the import cannot make: it registers a repository it does not
        // know, and one the model made up must not become one the workspace has.
        var allowed = new HashSet<string>(note.Repos, StringComparer.OrdinalIgnoreCase);
        if (steps.SelectMany(step => EntryTextParser.Parse(step).RepoIds ?? [])
                .FirstOrDefault(repo => _repositories?.Resolve(repo) is not { } known || !allowed.Contains(known.Id)) is { } unknown)
        {
            return BrainPickupResult.Failed(InboxErrors.PlanUnknownRepository(unknown).Message);
        }

        var document = string.Join("\n\n", [PlanText(note, tag), .. steps.Select(step => step.Trim('\r', '\n'))]) + "\n";
        var imported = await tasks.ImportPlanAsync(document, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (imported.IsFailure) return BrainPickupResult.Failed($"The plan was not imported: {imported.Error.Message}");

        var taskIds = imported.Value.Entries.Select(entry => entry.Id).ToList();
        var roadmapNote = imported.Value.Roadmap?.Refusal is { } refusal ? $" {refusal}" : string.Empty;

        var link = await _links.AppendRoadmapTagAsync(note.Path, tag, cancellationToken).ConfigureAwait(false);
        return link.Succeeded
            ? new BrainPickupResult(BrainPickupOutcome.Linked, taskIds, tag, $"Planned as +{tag}: {Count(taskIds.Count)}, and the note names the plan.{roadmapNote}")
            : new BrainPickupResult(BrainPickupOutcome.NotLinked, taskIds, tag, $"Planned as +{tag}: {Count(taskIds.Count)}. The link was not written into the note: {link.Error}{roadmapNote}");
    }

    /// <summary>Appends every task made to the note's <c>tasks</c>, stopping at the
    /// first refusal: the writer refuses a block for good, so the rest would be too.</summary>
    private async Task<BrainPickupResult> LinkTasksAsync(string notePath, List<Guid> created, string? shortfall, CancellationToken cancellationToken)
    {
        var made = created.Count == 1 ? "Task created" : $"{created.Count} tasks created";
        var tail = shortfall is null ? string.Empty : $" {shortfall}";

        foreach (var id in created)
        {
            var link = await _links.AppendTaskAsync(notePath, id.ToString("D"), cancellationToken).ConfigureAwait(false);
            if (!link.Succeeded)
            {
                return new BrainPickupResult(BrainPickupOutcome.NotLinked, created, null, $"{made}. The link was not written into the note: {link.Error}{tail}");
            }
        }

        return new BrainPickupResult(
            shortfall is null ? BrainPickupOutcome.Linked : BrainPickupOutcome.NotLinked,
            created,
            null,
            $"{made}, and the note lists {(created.Count == 1 ? "it" : "them")}.{tail}");
    }

    /// <summary>
    /// One drafted step as the plan holds it: under this plan's Roadmap Tag alone —
    /// any other <c>+</c> tag the model wrote replaced, so the roadmap gathers every
    /// step — and at <c>!draft</c>, because nobody has read it yet. Other tags stay.
    /// </summary>
    internal static string AsStep(string step, string tag)
    {
        var parsed = EntryTextParser.Parse(step);
        var tags = parsed.Tags.Where(existing => !existing.StartsWith('+')).Append("+" + tag);
        return EntryTextParser.WithStatus(EntryTextParser.WithTags(step, tags), EntryStatus.Draft);
    }

    private static string Count(int tasks) => tasks == 1 ? "1 task" : $"{tasks} tasks";

    /// <summary>A draft task's entry text: the title on its line, then its type,
    /// <c>!draft</c> and, for a repository, its <c>repo:</c>.</summary>
    internal static string TaskText(string title, string? repo)
    {
        var text = new StringBuilder();
        text.Append("# ").Append(TitleLine(title)).Append('\n');
        text.Append("`task` `!draft`");
        if (!string.IsNullOrWhiteSpace(repo)) text.Append(" `repo:").Append(repo.Trim()).Append('`');
        return text.Append('\n').ToString();
    }

    /// <summary>The <c>plan</c> entry the import lays out as the roadmap item: the
    /// note's title, the Roadmap Tag, the note as its Knowledge Ref and the note's
    /// repositories as its scope.</summary>
    internal static string PlanText(BrainPickupNote note, string tag)
    {
        var text = new StringBuilder();
        text.Append("# ").Append(TitleLine(note.Title)).Append('\n');
        text.Append("`plan` `+").Append(tag).Append("` `devbook:").Append(BrainNoteAddress.Format(note.Path)).Append('`');
        foreach (var repo in note.Repos) text.Append(" `repo:").Append(repo).Append('`');
        return text.Append('\n').ToString();
    }

    /// <summary>A title as one line no token can start in: whitespace runs made one
    /// space, and a backtick, which would open a token, dropped.</summary>
    private static string TitleLine(string title) =>
        string.Join(' ', title.Replace('`', '\'').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The note's text and, under it, each Brain note its <c>related</c>
    /// field names, so the drafter reads the project with what it links to.</summary>
    private async Task<string> RequestBodyAsync(BrainPickupNote note, CancellationToken cancellationToken)
    {
        var body = new StringBuilder(note.Text.TrimEnd());

        foreach (var linked in LinkedNotePaths(note).Take(MaxLinkedNotes))
        {
            if (await ReadNoteAsync(linked, cancellationToken).ConfigureAwait(false) is not { } other) continue;

            body.Append("\n\n---\n\nLinked note `").Append(other.Path).Append("`:\n\n").Append(other.Text.TrimEnd());
        }

        return body.Append('\n').ToString();
    }

    /// <summary>The Brain notes the note's <c>related</c> field names — a
    /// <c>#brain:</c> address or a plain path, its slug dropped — in order, once
    /// each, never the note itself; a <c>repo:</c> link or an address is not one.</summary>
    internal static IEnumerable<string> LinkedNotePaths(BrainPickupNote note)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { note.Path };

        foreach (var raw in note.Related)
        {
            string? path;
            if (BrainNoteAddress.TryParse(raw, out var address)) path = address.Path;
            else if (BrainRepoLink.IsRepoLink(raw) || raw.Contains("://", StringComparison.Ordinal)) path = null;
            else path = raw.Split('#')[0].Trim().Replace('\\', '/').TrimStart('/');

            if (path is { Length: > 0 } && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && seen.Add(path)) yield return path;
        }
    }

    private string? ProjectsFolder()
    {
        try
        {
            return BrainNoteDestinations.ProjectsArea(_brain.Areas())?.Folder;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The note's text and what a pickup reads off it, or null for a path
    /// that leaves the Brain, is not a <c>.md</c> file, or is not there.</summary>
    private async Task<BrainPickupNote?> ReadNoteAsync(string notePath, CancellationToken cancellationToken)
    {
        if (_brain.BrainRoot is not { } root || string.IsNullOrWhiteSpace(notePath)) return null;

        var relative = notePath.Replace('\\', '/').Trim().TrimStart('/');
        if (!relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return null;

        // One plain name per segment, as the link writer takes a path: no empty,
        // dot or dot-dot segment, so the path the task and the plan reference is the
        // one the writer and the Brain address read.
        if (relative.Split('/').Any(segment => segment.Length == 0 || segment.StartsWith('.'))) return null;

        var rootFull = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(rootFull, relative.Replace('/', Path.DirectorySeparatorChar)));
        var inside = rootFull.EndsWith(Path.DirectorySeparatorChar) ? rootFull : rootFull + Path.DirectorySeparatorChar;
        if (!full.StartsWith(inside, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;

        string text;
        try
        {
            text = await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return new BrainPickupNote(
            relative,
            TitleOf(text) ?? Path.GetFileNameWithoutExtension(full),
            text,
            [.. BrainNoteLinks.ReadList(text, "repos").Select(repo => repo.Trim()).Where(repo => repo.Length > 0 && !repo.Any(char.IsWhiteSpace)).Distinct(StringComparer.OrdinalIgnoreCase)],
            BrainNoteLinks.ReadList(text, "related"));
    }

    /// <summary>The note's first level-1 heading outside a fence.</summary>
    private static string? TitleOf(string text)
    {
        var fenced = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (!fenced && line.StartsWith("# ", StringComparison.Ordinal) && line[2..].Trim() is { Length: > 0 } title) return title;
        }

        return null;
    }
}

/// <summary>A note as a pickup reads it.</summary>
/// <param name="Path">Relative to the Brain's root, with <c>/</c>.</param>
/// <param name="Title">Its first level-1 heading, else its file name.</param>
/// <param name="Text">The whole file.</param>
/// <param name="Repos">Its <c>repos</c> field's values.</param>
/// <param name="Related">Its <c>related</c> field's values, as written.</param>
internal sealed record BrainPickupNote(string Path, string Title, string Text, IReadOnlyList<string> Repos, IReadOnlyList<string> Related);
