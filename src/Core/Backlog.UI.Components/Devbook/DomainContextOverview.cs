using System.Text.RegularExpressions;

using Backlog.SharedKernel.Devbook;
using Backlog.SharedKernel.Markdown;
using Backlog.SharedKernel.Metadata;

namespace Backlog.UI.Components.Devbook;

/// <summary>How many chapters of one kind a context's files hold.</summary>
/// <param name="Type">The chapters' <c>type</c>, as the <c>meta</c> block writes it.</param>
/// <param name="Label">The kind in plain words, singular for one: <c>Aggregate</c>,
/// <c>Value objects</c>.</param>
public sealed record DomainChapterCount(string Type, string Label, int Count)
{
    /// <summary>Where the tile leads: the first such chapter's unit — its file and
    /// the anchor of the <c>##</c> chapter it sits in — repository-relative, or null
    /// when the overview was read without paths.</summary>
    public string? Reference { get; init; }
}

/// <summary>One file of a context beside its <c>context.md</c>.</summary>
/// <param name="Path">Its repository-relative path, or the empty string when the
/// overview is read without links.</param>
/// <param name="Text">The file as written.</param>
public sealed record DomainContextFile(string Path, string Text);

/// <summary>How many chapters carry one <c>status</c>.</summary>
public sealed record DomainStatusCount(string Status, int Count);

/// <summary>A chapter something on the Overview links to: its heading as written
/// and its repository-relative reference.</summary>
public sealed record DomainOverviewLink(string Title, string Reference);

/// <summary>One question <c>context.md</c> says another context answers: a clause
/// of its <em>Outside it:</em> paragraph — <em>what arrives, which the Inbox
/// answers</em> — read as the question and who answers it.</summary>
/// <param name="Question">The clause up to <em>which</em>, as markdown, its first
/// letter raised.</param>
/// <param name="Answers">The contexts the clause links as answering it; empty when
/// the clause names none.</param>
public sealed record DomainBoundaryQuestion(string Question, IReadOnlyList<DomainOverviewLink> Answers);

/// <summary>How well one kind of promise is proved.</summary>
/// <param name="Count">The requirement or invariant chapters.</param>
/// <param name="Proved">Those whose block names a <c>tests</c> entry.</param>
/// <param name="Extra">For requirements, their scenarios; for invariants, those
/// whose <c>Enforced at:</c> line is <c>open</c>.</param>
/// <param name="Reference">The page the card opens, or null.</param>
public sealed record DomainProof(int Count, int Proved, int Extra, string? Reference)
{
    public static DomainProof None { get; } = new(0, 0, 0, null);
}

/// <summary>A page of the context that answers one question: Features answers
/// <em>what can a person do with it</em>.</summary>
public sealed record DomainContextLens(string Title, string Question, string Reference);

/// <summary>A <c>feature-flag</c> or <c>setting</c> chapter of <c>context.md</c>.</summary>
/// <param name="Title">The heading as written.</param>
/// <param name="IsSetting">A setting rather than a feature flag.</param>
/// <param name="Key">The block's <c>key</c>, or null when it states none.</param>
/// <param name="Default">The block's <c>default</c>, or null when it states none.</param>
/// <param name="Status">The block's <c>status</c>; <c>active</c> when it states
/// none, the domain folder's resting value.</param>
/// <param name="Summary">The chapter's first sentence as plain text.</param>
public sealed record DomainContextSwitch(string Title, bool IsSetting, string? Key, string? Default, string Status, string Summary)
{
    /// <summary>A setting's <c>scope</c> — <c>user</c>, <c>tenant</c>,
    /// <c>system</c> — or null for a flag.</summary>
    public string? Scope { get; init; }

    /// <summary>The features the switch's <c>related</c> names: what it gates.</summary>
    public IReadOnlyList<DomainOverviewLink> Gates { get; init; } = [];
}

/// <summary>
/// What the Domain view's Overview says about one bounded context (local ADR
/// 0040): the boundary <c>context.md</c> draws above its first chapter — its lead,
/// what is inside and what is answered outside — how many chapters of each kind
/// the context's files hold, how well its requirements and invariants are proved,
/// how settled the chapters are, its flags and settings, and the pages that read
/// it through a question. Everything is read from the files as written; no
/// chapter carries a field for the view.
/// </summary>
/// <param name="Boundary">The prose of <c>context.md</c> between its title and
/// its first <c>##</c> chapter, as markdown, without its <c>meta</c> block or any
/// <c>annotation</c> note.</param>
/// <param name="Counts">The domain and feature chapters per kind, in the order a
/// reader meets the kinds.</param>
/// <param name="Provable">The requirement and invariant chapters.</param>
/// <param name="Proved">Those of them whose block names a <c>tests</c> entry.</param>
/// <param name="Statuses">The chapters per <c>status</c>, least settled first.</param>
/// <param name="Switches">The <c>feature-flag</c> and <c>setting</c> chapters of
/// <c>context.md</c>, in file order.</param>
public sealed partial record DomainContextOverview(
    string Boundary,
    IReadOnlyList<DomainChapterCount> Counts,
    int Provable,
    int Proved,
    IReadOnlyList<DomainStatusCount> Statuses,
    IReadOnlyList<DomainContextSwitch> Switches)
{
    public static DomainContextOverview Empty { get; } = new(string.Empty, [], 0, 0, [], []);

    /// <summary>The boundary's opening: everything above its <em>Inside the
    /// boundary:</em> and <em>Outside it:</em> paragraphs, as markdown.</summary>
    public string Lead { get; init; } = string.Empty;

    /// <summary>What the <em>Inside the boundary:</em> paragraph says, after those
    /// words, as markdown; empty when <c>context.md</c> writes none.</summary>
    public string Inside { get; init; } = string.Empty;

    /// <summary>The <em>Outside it:</em> paragraph read as questions and who
    /// answers each; empty when it does not read that way.</summary>
    public IReadOnlyList<DomainBoundaryQuestion> Outside { get; init; } = [];

    /// <summary>What the <em>Outside it:</em> paragraph says beyond its questions,
    /// as markdown — all of it when it reads as none.</summary>
    public string OutsideNote { get; init; } = string.Empty;

    /// <summary>The boundary's blocks after its Inside and Outside paragraphs and
    /// their lists, as markdown: prose that is neither card's.</summary>
    public string More { get; init; } = string.Empty;

    /// <summary>The <c>status</c> on <c>context.md</c>'s own block, or null.</summary>
    public string? Status { get; init; }

    /// <summary>The <c>deployment</c> on <c>context.md</c>'s own block —
    /// <c>module</c> or <c>service</c> — or null.</summary>
    public string? Deployment { get; init; }

    /// <summary>The context map's subdomain type for the context — <c>core</c>,
    /// <c>supporting</c>, <c>generic</c> — set by the host, which reads the map.</summary>
    public string? Subdomain { get; init; }

    /// <summary>The requirement chapters and their scenarios.</summary>
    public DomainProof Requirements { get; init; } = DomainProof.None;

    /// <summary>The invariant chapters and those still <c>open</c>.</summary>
    public DomainProof Invariants { get; init; } = DomainProof.None;

    /// <summary>The context's pages, each with the question it answers, for the
    /// files the context has.</summary>
    public IReadOnlyList<DomainContextLens> Lenses { get; init; } = [];

    /// <summary><c>context.md</c> and each of its chapters, by reference: what the
    /// From your Brain box covers. Empty when read without paths.</summary>
    public IReadOnlyList<string> Chapters { get; init; } = [];

    /// <summary>The kinds a tile counts, in reading order, each in plain words,
    /// singular then plural. Requirements and invariants are counted by the proof
    /// cards; a file's own <c>type</c> — <c>requirements</c>, <c>model</c> — is
    /// not a chapter kind and is never counted.</summary>
    private static readonly (string Type, string One, string Many)[] Kinds =
    [
        ("aggregate", "Aggregate", "Aggregates"),
        ("entity", "Entity", "Entities"),
        ("value-object", "Value object", "Value objects"),
        ("enum", "Enum", "Enums"),
        ("domain-service", "Domain service", "Domain services"),
        ("domain-event", "Domain event", "Domain events"),
        ("term", "Term", "Terms"),
        ("feature", "Feature", "Features")
    ];

    /// <summary>Every chapter kind the status spread reads: the tiles' kinds, the
    /// proof cards' and the switches.</summary>
    private static readonly HashSet<string> Settled = new(
        [.. Kinds.Select(kind => kind.Type), "sub-feature", "requirement", "invariant", "feature-flag", "setting"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SwitchTypes = new(StringComparer.OrdinalIgnoreCase) { "feature-flag", "setting" };

    private static readonly HashSet<string> FeatureTypes = new(StringComparer.OrdinalIgnoreCase) { "feature", "sub-feature" };

    /// <summary>The status words of the domain folder, least settled first.</summary>
    private static readonly string[] StatusOrder = ["draft", "proposed", "approved", "active", "accepted", "deprecated"];

    /// <summary>The pages a context may have, the file each reads and the question
    /// it answers, in the order the Overview offers them.</summary>
    private static readonly (string Title, string File, string Question)[] LensPages =
    [
        ("Features", "features.md", "What can a person do with it, and what does each build on?"),
        ("Model", "model.md", "What is it made of, and what does it tell other contexts?"),
        ("Flow", "flow.md", "How does it move, and what does not count as moving?"),
        ("Requirements", "requirements.md", "What does it promise, and what can never be false?"),
        ("Glossary", "domain.md", "What do we call things, and what must not be confused?")
    ];

    /// <summary>Reads the overview of one context from its files' text alone, with
    /// nothing to link to.</summary>
    /// <param name="contextMarkdown">The context's <c>context.md</c>.</param>
    /// <param name="files">The context's other files, each as written.</param>
    public static DomainContextOverview Read(string? contextMarkdown, IEnumerable<string> files) =>
        Read(contextMarkdown, null, files.Select(text => new DomainContextFile(string.Empty, text)));

    /// <summary>Reads the overview of one context, with the links each part leads to.</summary>
    /// <param name="contextMarkdown">The context's <c>context.md</c>.</param>
    /// <param name="contextPath">Its repository-relative path, which a relative link
    /// in it resolves against; null reads no links.</param>
    /// <param name="files">The context's other files with their paths.</param>
    public static DomainContextOverview Read(string? contextMarkdown, string? contextPath, IEnumerable<DomainContextFile> files)
    {
        var context = contextMarkdown ?? string.Empty;
        var fromContext = contextPath ?? string.Empty;
        var others = files.ToList();
        var contextChapters = MetaChapters(context, fromContext).ToList();
        var perFile = others.Select(file => (file, chapters: MetaChapters(file.Text, file.Path).ToList())).ToList();
        var chapters = contextChapters.Concat(perFile.SelectMany(pair => pair.chapters)).ToList();
        var linked = contextPath is not null;

        IReadOnlyList<DomainChapterCount> counts =
        [
            .. Kinds
                .Select(kind => (kind, of: chapters.Where(chapter => Is(chapter, kind.Type)).ToList()))
                .Where(pair => pair.of.Count > 0)
                .Select(pair => new DomainChapterCount(pair.kind.Type, pair.of.Count == 1 ? pair.kind.One : pair.kind.Many, pair.of.Count)
                {
                    Reference = linked ? pair.of[0].UnitReference : null
                })
        ];

        var requirements = chapters.Where(chapter => Is(chapter, "requirement")).ToList();
        var invariants = chapters.Where(chapter => Is(chapter, "invariant")).ToList();
        var requirementFile = requirements.FirstOrDefault()?.Path;
        var scenarios = perFile
            .Where(pair => pair.chapters.Any(chapter => Is(chapter, "requirement")))
            .Sum(pair => ScenarioCount(pair.file.Text));

        var settled = chapters.Where(chapter => chapter.Type is { } type && Settled.Contains(type)).ToList();
        IReadOnlyList<DomainStatusCount> statuses =
        [
            .. settled
                .GroupBy(chapter => chapter.Status, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => Array.IndexOf(StatusOrder, group.Key) is var rank && rank < 0 ? StatusOrder.Length : rank)
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new DomainStatusCount(group.Key, group.Count()))
        ];

        // A gate is a feature the switch's related names, by its heading.
        var features = chapters
            .Where(chapter => chapter.Type is { } type && FeatureTypes.Contains(type) && chapter.Path.Length > 0)
            .GroupBy(chapter => DomainUnits.Normalize(chapter.Reference), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<DomainContextSwitch> switches =
        [
            .. contextChapters
                .Where(chapter => chapter.Type is { } type && SwitchTypes.Contains(type))
                .Select(chapter => new DomainContextSwitch(
                    chapter.Title,
                    string.Equals(chapter.Type, "setting", StringComparison.OrdinalIgnoreCase),
                    Field(chapter, "key"),
                    Field(chapter, "default"),
                    chapter.Status,
                    DomainUnits.FirstSentence(chapter.Prose))
                {
                    Scope = string.Equals(chapter.Type, "setting", StringComparison.OrdinalIgnoreCase) ? Field(chapter, "scope") : null,
                    Gates = linked
                        ? [.. chapter.Meta.Related
                            .Select(related => DomainUnits.RepositoryRelative(related.Raw, fromContext))
                            .Where(reference => reference.Length > 0)
                            .Select(reference => features.TryGetValue(DomainUnits.Normalize(reference), out var feature)
                                ? new DomainOverviewLink(feature.Title, feature.Reference)
                                : null)
                            .OfType<DomainOverviewLink>()
                            .DistinctBy(link => link.Reference, StringComparer.OrdinalIgnoreCase)]
                        : []
                })
        ];

        var boundary = BoundaryOf(context);
        var (lead, inside, outside, more) = Split(boundary);
        var (questions, note) = Questions(outside, fromContext, linked);
        var title = TitleMeta(context);

        return new DomainContextOverview(boundary, counts, requirements.Count + invariants.Count,
            requirements.Count(Proven) + invariants.Count(Proven), statuses, switches)
        {
            Lead = lead,
            Inside = inside,
            Outside = questions,
            OutsideNote = note,
            More = more,
            Status = string.IsNullOrWhiteSpace(title?.Status) ? null : title.Status.Trim().ToLowerInvariant(),
            Deployment = string.IsNullOrWhiteSpace(title?.Deployment) ? null : title.Deployment.Trim().ToLowerInvariant(),
            Requirements = requirements.Count == 0
                ? DomainProof.None
                : new DomainProof(requirements.Count, requirements.Count(Proven), scenarios, linked ? requirementFile : null),
            Invariants = invariants.Count == 0
                ? DomainProof.None
                : new DomainProof(invariants.Count, invariants.Count(Proven), invariants.Count(chapter => OpenRule().IsMatch(chapter.Prose)),
                    linked ? AlwaysTrueUnit(invariants[0], chapters) : null),
            Lenses = linked ? LensesOf(others, chapters) : [],
            Chapters = linked ? [fromContext, .. contextChapters.Select(chapter => chapter.Reference)] : []
        };
    }

    private static bool Is(MetaChapter chapter, string type) =>
        string.Equals(chapter.Type, type, StringComparison.OrdinalIgnoreCase);

    private static bool Proven(MetaChapter chapter) => chapter.Meta.Tests.Count > 0;

    private static string? Field(MetaChapter chapter, string name) =>
        chapter.Meta.Extra.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;

    /// <summary>The unit an invariant's Always true tab sits on: the first
    /// aggregate of the domain file beside its invariants file —
    /// <c>domain.md</c> for <c>domain.invariants.md</c>.</summary>
    private static string? AlwaysTrueUnit(MetaChapter invariant, List<MetaChapter> chapters)
    {
        var domainFile = invariant.Path.Replace(".invariants.md", ".md", StringComparison.OrdinalIgnoreCase);
        return chapters.FirstOrDefault(chapter => Is(chapter, "aggregate") && string.Equals(chapter.Path, domainFile, StringComparison.OrdinalIgnoreCase))?.Reference
            ?? chapters.FirstOrDefault(chapter => Is(chapter, "aggregate"))?.Reference;
    }

    /// <summary>The pages the context has, each once, with the question it answers.
    /// Glossary is the Ubiquitous Language chapter of a domain file.</summary>
    private static IReadOnlyList<DomainContextLens> LensesOf(List<DomainContextFile> files, List<MetaChapter> chapters)
    {
        var lenses = new List<DomainContextLens>();
        foreach (var (title, file, question) in LensPages)
        {
            if (title == "Glossary")
            {
                if (chapters.FirstOrDefault(chapter => Is(chapter, "ubiquitous-language")) is { } language)
                    lenses.Add(new DomainContextLens(title, question, language.Reference));
                continue;
            }
            if (files.FirstOrDefault(f => string.Equals(FileName(f.Path), file, StringComparison.OrdinalIgnoreCase)) is { } found)
                lenses.Add(new DomainContextLens(title, question, found.Path));
        }
        return lenses;
    }

    private static string FileName(string path) => System.IO.Path.GetFileName(path.Replace('\\', '/'));

    private static int ScenarioCount(string markdown)
    {
        var lines = Lines(markdown);
        var count = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (MarkdownFence.Open(lines[i]) is { } fence)
            {
                i = CloseOf(lines, i, fence);
                continue;
            }
            if (Scenario().IsMatch(lines[i])) count++;
        }
        return count;
    }

    /// <summary>The title's own <c>meta</c> block, or null.</summary>
    private static MetadataRecord? TitleMeta(string markdown)
    {
        var lines = Lines(markdown);
        var seenTitle = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (Heading().Match(lines[i]) is { Success: true } heading)
            {
                if (seenTitle || heading.Groups[1].Value.Length != 1) return null;
                seenTitle = true;
                continue;
            }
            if (MarkdownFence.Open(lines[i]) is { } fence)
            {
                var close = CloseOf(lines, i, fence);
                if (MetadataReader.IsMetaBlock(fence.Language))
                    return MetadataReader.Parse(string.Join('\n', lines[(i + 1)..Math.Min(close, lines.Length)]));
                i = close;
            }
        }
        return null;
    }

    /// <summary>The boundary cut into its lead, what is inside, what is outside,
    /// and what follows them: the blocks above the first <em>Inside the
    /// boundary:</em> or <em>Outside it:</em> paragraph are the lead; each of those
    /// paragraphs, with a list directly under it, is its card, its leading words
    /// dropped; every later block is the rest.</summary>
    private static (string Lead, string Inside, string Outside, string More) Split(string boundary)
    {
        var blocks = Blocks(boundary);
        var inside = blocks.FindIndex(block => InsideLead().IsMatch(block));
        var outside = blocks.FindIndex(block => OutsideLead().IsMatch(block));
        var first = new[] { inside, outside }.Where(i => i >= 0).DefaultIfEmpty(blocks.Count).Min();

        var taken = new HashSet<int>();
        var lead = string.Join("\n\n", blocks.Take(first));
        var insideText = Card(inside, InsideLead());
        var outsideText = Card(outside, OutsideLead());
        var more = string.Join("\n\n", blocks.Select((block, index) => (block, index))
            .Where(pair => pair.index >= first && !taken.Contains(pair.index))
            .Select(pair => pair.block));
        return (lead, insideText, outsideText, more);

        string Card(int at, Regex leading)
        {
            if (at < 0) return string.Empty;
            taken.Add(at);
            var text = new List<string> { leading.Replace(blocks[at], string.Empty, 1).Trim() };
            for (var next = at + 1; next < blocks.Count && IsList(blocks[next]); next++)
            {
                taken.Add(next);
                text.Add(blocks[next]);
            }
            return string.Join("\n\n", text.Where(part => part.Length > 0)).Trim();
        }
    }

    /// <summary>The <em>Outside it:</em> text as questions. A list reads one
    /// question per item; a paragraph one per clause between semicolons, up to the
    /// sentence the last clause ends. A clause reads as a question when it ends
    /// <em>…, which [X] answers</em>; a paragraph none of whose clauses does is
    /// kept whole as the note.</summary>
    private static (IReadOnlyList<DomainBoundaryQuestion> Questions, string Note) Questions(string outside, string fromDocument, bool linked)
    {
        if (outside.Length == 0) return ([], string.Empty);

        var blocks = Blocks(outside);
        List<string> clauses;
        var rest = new List<string>();
        if (IsList(blocks[0]) || (blocks.Count > 1 && blocks[0].EndsWith(':') && IsList(blocks[1])))
        {
            var list = IsList(blocks[0]) ? 0 : 1;
            clauses = Items(blocks[list]);
            rest.AddRange(blocks.Skip(list + 1));
            if (list == 1 && blocks[0].TrimEnd(':').Trim().Length > 0) rest.Insert(0, blocks[0]);
        }
        else
        {
            clauses = [.. Clauses(blocks[0], out var tail)];
            if (tail.Length > 0) rest.Add(tail);
            rest.AddRange(blocks.Skip(1));
        }

        var questions = new List<DomainBoundaryQuestion>();
        var matched = 0;
        foreach (var clause in clauses)
        {
            var text = clause.Trim().TrimEnd(';', '.').Trim();
            if (text.Length == 0) continue;
            if (Answered().Match(text) is { Success: true } answer && Links().Matches(answer.Groups["who"].Value) is { Count: > 0 } links)
            {
                matched++;
                questions.Add(new DomainBoundaryQuestion(
                    answer.Groups["q"].Value.Trim(),
                    [.. links.Select(link => new DomainOverviewLink(
                        link.Groups[1].Value,
                        linked ? DomainUnits.RepositoryRelative(link.Groups[2].Value, fromDocument) : link.Groups[2].Value))]));
            }
            else
            {
                questions.Add(new DomainBoundaryQuestion(text, []));
            }
        }

        if (matched == 0) return ([], outside);
        return (questions, string.Join("\n\n", rest).Trim());
    }

    /// <summary>A paragraph's clauses: split on semicolons outside a link, the last
    /// one ended where its sentence ends, the rest handed back as the tail.</summary>
    private static IEnumerable<string> Clauses(string paragraph, out string tail)
    {
        var text = Whitespace().Replace(paragraph, " ").Trim();
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '[' or '(') depth++;
            else if (text[i] is ']' or ')') depth = Math.Max(0, depth - 1);
            else if (text[i] == ';' && depth == 0)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }
        var last = text[start..];
        var end = SentenceEnd().Match(last);
        tail = string.Empty;
        if (end.Success)
        {
            tail = last[(end.Index + 1)..].Trim();
            last = last[..(end.Index + 1)];
        }
        parts.Add(last);
        return parts
            .SelectMany(part => AnswerComma().Split(part))
            .Select(part => AndLead().Replace(part.Trim(), string.Empty));
    }

    private static List<string> Items(string list)
    {
        var items = new List<string>();
        foreach (var line in Lines(list))
        {
            if (ListItem().Match(line) is { Success: true } item) items.Add(item.Groups[1].Value);
            else if (items.Count > 0 && line.Trim().Length > 0) items[^1] += " " + line.Trim();
        }
        return items;
    }

    private static bool IsList(string block) => ListItem().IsMatch(Lines(block)[0]);

    /// <summary>Blank-line separated blocks, a list beginning on the line after a
    /// paragraph counted as a block of its own.</summary>
    private static List<string> Blocks(string markdown)
    {
        var blocks = new List<string>();
        var current = new List<string>();
        foreach (var line in Lines(markdown))
        {
            if (line.Trim().Length == 0)
            {
                Flush();
                continue;
            }
            if (current.Count > 0 && ListItem().IsMatch(line) && !ListItem().IsMatch(current[0])) Flush();
            current.Add(line);
        }
        Flush();
        return blocks;

        void Flush()
        {
            if (current.Count > 0) blocks.Add(string.Join('\n', current));
            current.Clear();
        }
    }

    /// <summary>A chapter that opens with a <c>meta</c> block: its heading, the
    /// block as the shared reader reads it, its prose up to the next heading, and
    /// where it is.</summary>
    private sealed record MetaChapter(string Title, MetadataRecord Meta, string Prose, string Path, string Anchor, string UnitAnchor)
    {
        public string? Type => Meta.Type;

        /// <summary>The chapter's own reference: its file and its anchor.</summary>
        public string Reference => $"{Path}#{Anchor}";

        /// <summary>The <c>##</c> chapter it sits in, or itself: the unit a page opens.</summary>
        public string UnitReference => $"{Path}#{UnitAnchor}";

        /// <summary>The block's status, or the domain folder's resting value when
        /// it leaves the field out (<see cref="DevbookSchema.RestsByOmission"/>).</summary>
        public string Status => string.IsNullOrWhiteSpace(Meta.Status)
            ? DevbookSchema.RestingStatus
            : Meta.Status.Trim().ToLowerInvariant();
    }

    private static IEnumerable<MetaChapter> MetaChapters(string markdown, string path)
    {
        var lines = Lines(markdown);
        var unit = string.Empty;
        for (var i = 0; i < lines.Length; i++)
        {
            if (MarkdownFence.Open(lines[i]) is { } skipped)
            {
                i = CloseOf(lines, i, skipped);
                continue;
            }

            var heading = Heading().Match(lines[i]);
            if (!heading.Success || heading.Groups[1].Value.Length < 2) continue;

            var title = heading.Groups[2].Value.Trim();
            var anchor = DomainUnits.Slug(title);
            if (heading.Groups[1].Value.Length == 2) unit = anchor;

            var at = i + 1;
            while (at < lines.Length && string.IsNullOrWhiteSpace(lines[at])) at++;
            if (at >= lines.Length || MarkdownFence.Open(lines[at]) is not { } fence || !MetadataReader.IsMetaBlock(fence.Language)) continue;

            var close = CloseOf(lines, at, fence);
            var meta = MetadataReader.Parse(string.Join('\n', lines[(at + 1)..Math.Min(close, lines.Length)]));

            // The prose runs from after the block to the next heading outside a fence.
            var prose = new List<string>();
            var end = close + 1;
            while (end < lines.Length && !Heading().IsMatch(lines[end]))
            {
                if (MarkdownFence.Open(lines[end]) is { } inner)
                {
                    var innerClose = CloseOf(lines, end, inner);
                    prose.AddRange(lines[end..Math.Min(innerClose + 1, lines.Length)]);
                    end = innerClose + 1;
                    continue;
                }
                prose.Add(lines[end++]);
            }

            i = close;
            yield return new MetaChapter(title, meta, string.Join('\n', prose), path, anchor, unit.Length > 0 ? unit : anchor);
        }
    }

    /// <summary>The prose under the title, up to the first chapter, without the
    /// title's <c>meta</c> block or any review note: neither is the boundary.</summary>
    private static string BoundaryOf(string markdown)
    {
        var lines = Lines(markdown);
        var boundary = new List<string>();
        var seenTitle = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (MarkdownFence.Open(lines[i]) is { } fence)
            {
                var close = CloseOf(lines, i, fence);
                if (!MetadataReader.IsMetaBlock(fence.Language) && !DevbookAnnotationFence.IsAnnotationBlock(fence.Language))
                    boundary.AddRange(lines[i..Math.Min(close + 1, lines.Length)]);
                i = close;
                continue;
            }

            if (Heading().Match(lines[i]) is { Success: true } heading)
            {
                if (heading.Groups[1].Value.Length == 1 && !seenTitle)
                {
                    seenTitle = true;
                    continue;
                }
                break;
            }
            boundary.Add(lines[i]);
        }
        return string.Join('\n', boundary).Trim();
    }

    /// <summary>The line that closes the fence opened at <paramref name="open"/>,
    /// or the last line when nothing closes it.</summary>
    private static int CloseOf(string[] lines, int open, MarkdownFence fence)
    {
        var at = open + 1;
        while (at < lines.Length && !fence.IsClosedBy(lines[at])) at++;
        return Math.Min(at, lines.Length - 1);
    }

    private static string[] Lines(string markdown) =>
        markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>The ATX heading rule <see cref="DomainUnits"/> reads with.</summary>
    [GeneratedRegex(@"^(#{1,6})[ \t]+(.+?)(?:[ \t]+#+)?[ \t]*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^#{2,6}[ \t]+Scenario\b", RegexOptions.IgnoreCase)]
    private static partial Regex Scenario();

    [GeneratedRegex(@"^Enforced at:[ \t]*open\b", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex OpenRule();

    [GeneratedRegex(@"^Inside the boundary:[ \t]*", RegexOptions.IgnoreCase)]
    private static partial Regex InsideLead();

    [GeneratedRegex(@"^Outside (?:it|the boundary):[ \t]*", RegexOptions.IgnoreCase)]
    private static partial Regex OutsideLead();

    [GeneratedRegex(@"^(?<q>.+?),\s*which\s+(?:the\s+)?(?<who>.+?)\s+answers?$", RegexOptions.Singleline)]
    private static partial Regex Answered();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex Links();

    [GeneratedRegex(@"^\s*[-*][ \t]+(.*)$")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^and\s+", RegexOptions.IgnoreCase)]
    private static partial Regex AndLead();

    [GeneratedRegex(@"(?<=\banswers?)\.(?=\s|$)")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>The comma after one clause's <em>answers</em> that starts the next.</summary>
    [GeneratedRegex(@"(?<=\banswers?),\s+")]
    private static partial Regex AnswerComma();
}
