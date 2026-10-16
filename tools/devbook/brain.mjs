// brain.mjs — the Brain's convention, for the database build.
//
//   node tools/devbook/build-database.mjs --brain --root <brain folder> --out <file>
//
// The Brain is one devbook-shaped folder per user, outside every repository
// (local ADR 0026). It keeps the devbook's `meta` blocks and `annotation` fences
// but not its folders: instead of arc42/domain/tech/design/ai it has the areas
// its own `CONVENTIONS.md` declares, each a top folder, and the PARA set when it
// declares none. A note adds three fields — `para`, `repos` and `tasks` — and may
// link into a repository devbook with `repo:<alias>/<devbook path>[#slug]`
// (local ADR 0027 §6), which no Brain database can resolve: those links are
// stored as edges whose target is no node.
//
// Nothing here parses a chapter. `loadBrainGenerator` wraps the generator this
// repository has installed — the same `parseDocument`, `buildGraph`,
// `buildOutlineDocument` and `collectAnnotations` the devbook build uses — and
// changes only what the Brain changes: which folders are read, which folder a
// path is in, and the projection of the graph described at `brainGraph`. The C#
// builder's Brain convention (`DevbookBuildLayout` with `DevbookCorpusKind.Brain`)
// is held to this by `DevbookBuilderParityTests`, and `BrainConventions.cs` is the
// C# twin of `parseConventions` below.

import { readFile, stat } from 'node:fs/promises';
import path from 'node:path';

/** The file at the Brain's root that declares its areas. */
export const CONVENTIONS_FILE = 'CONVENTIONS.md';

/** The heading the areas are declared under. */
const AREAS_HEADING = 'areas';

/** The PARA set, in reading order: the areas of a Brain that declares none. */
export const PARA_AREAS = Object.freeze([
    { name: 'Inbox', folder: '_inbox', order: 1 },
    { name: 'Projects', folder: 'projects', order: 2 },
    { name: 'Areas', folder: 'areas', order: 3 },
    { name: 'Resources', folder: 'resources', order: 4 },
    { name: 'People', folder: 'people', order: 5 },
    { name: 'Journal', folder: 'journal', order: 6 },
    { name: 'Archive', folder: 'archive', order: 7 },
]);

/** The node attributes a Brain adds to the devbook's list attributes. */
export const BRAIN_ATTRIBUTES = ['para', 'repos', 'tasks'];

/** The edge type a `repo:` link in a note's body draws. */
export const REPO_LINK_EDGE = 'repo-link';

/** The scheme every link into a repository devbook starts with. */
export const REPO_SCHEME = 'repo:';

const FENCE_LINE = /^ {0,3}(`{3,}|~{3,})/;
// ASCII classes, spelt out, as BrainConventions.cs spells them: JavaScript's
// \s and \d are not .NET's.
const HEADING = /^ {0,3}(#{1,6})[ \t]+(.*)$/;
const LIST_ITEM = /^[ \t]*(?:[-*+]|[0-9]+[.)])[ \t]+(.*)$/;
const CODE = /`([^`]+)`/;
const SEPARATORS = /[ \t]*[—–:-]+[ \t]*$/;
const ORDER_NUMBER = /^[0-9]{1,15}$/;
const FORBIDDEN = /[/\\:*?"<>|]/;

/** Whether `folder` can be an area: one plain segment, not a dot-folder. */
export function isAreaFolder(folder) {
    return typeof folder === 'string'
        && folder.trim().length > 0
        && folder === folder.trim()
        && !folder.startsWith('.')
        && !folder.endsWith('.')
        && !FORBIDDEN.test(folder)
        && !/[\u0000-\u001f\u007f-\u009f]/.test(folder);
}

/** An area's name from its folder: `_inbox` reads Inbox. */
export function nameFor(folder) {
    const words = folder.replace(/^[_\- ]+|[_\- ]+$/g, '').replace(/-/g, ' ').replace(/_/g, ' ');
    return words.length === 0 ? folder : words[0].toUpperCase() + words.slice(1);
}

function cells(line) {
    let inner = line.trim();
    if (inner.startsWith('|')) inner = inner.slice(1);
    if (inner.endsWith('|')) inner = inner.slice(0, -1);
    return inner.split('|').map((cell) => cell.trim());
}

function unquote(cell) {
    const match = CODE.exec(cell);
    return (match ? match[1] : cell).trim();
}

/**
 * The areas `markdown` declares under its `Areas` heading, in order — empty when
 * it declares none. A table needs a `Folder` column and may carry `Area` or
 * `Name` and `Order`; a list item names its folder in backticks, or is the
 * folder. See `BrainConventions.cs`, which this mirrors line for line.
 */
export function declaredAreas(markdown) {
    if (!markdown) return [];
    // A byte order mark is not text: C#'s File.ReadAllText drops it.
    if (markdown.charCodeAt(0) === 0xfeff) markdown = markdown.slice(1);

    const candidates = [];
    let fence = null;
    let sectionLevel = null;
    let header = null;

    for (const rawLine of markdown.split('\n')) {
        const raw = rawLine.replace(/\r+$/, '');
        const fenceMatch = FENCE_LINE.exec(raw);
        if (fence !== null) {
            if (fenceMatch && fenceMatch[1][0] === fence[0] && fenceMatch[1].length >= fence.length) fence = null;
            continue;
        }
        if (fenceMatch) {
            fence = fenceMatch[1];
            continue;
        }

        const heading = HEADING.exec(raw);
        if (heading) {
            const level = heading[1].length;
            if (sectionLevel !== null && level <= sectionLevel) sectionLevel = null;
            const text = heading[2].trim().replace(/#+$/, '').trim();
            if (sectionLevel === null && text.toLowerCase() === AREAS_HEADING) {
                sectionLevel = level;
                header = null;
            }
            continue;
        }

        if (sectionLevel === null) continue;

        const line = raw.trim();
        if (line.startsWith('|')) {
            const row = cells(line);
            if (header === null) {
                header = row.map((cell) => cell.toLowerCase());
                continue;
            }
            if (row.every((cell) => cell.length > 0 && /^[-: ]+$/.test(cell))) continue;

            const folderColumn = header.indexOf('folder');
            if (folderColumn < 0 || folderColumn >= row.length) continue;

            const nameColumn = header.findIndex((cell) => cell === 'area' || cell === 'name');
            const orderColumn = header.indexOf('order');
            const orderText = orderColumn >= 0 && orderColumn < row.length ? row[orderColumn] : '';
            candidates.push({
                name: nameColumn >= 0 && nameColumn < row.length ? row[nameColumn] : null,
                folder: unquote(row[folderColumn]),
                order: ORDER_NUMBER.test(orderText) ? Number(orderText) : null,
            });
            continue;
        }

        header = null;

        const item = LIST_ITEM.exec(raw);
        if (!item) continue;

        const text = item[1].trim();
        const code = CODE.exec(text);
        if (code) {
            candidates.push({ name: text.slice(0, code.index).replace(SEPARATORS, '').trim(), folder: code[1].trim(), order: null });
        } else {
            candidates.push({ name: null, folder: text, order: null });
        }
    }

    const ordered = candidates
        .map((candidate, index) => ({ ...candidate, index }))
        .sort((a, b) => (a.order ?? Number.MAX_SAFE_INTEGER) - (b.order ?? Number.MAX_SAFE_INTEGER) || a.index - b.index);

    const areas = [];
    const seen = new Set();
    for (const { name, folder } of ordered) {
        if (!isAreaFolder(folder) || seen.has(folder.toLowerCase())) continue;
        seen.add(folder.toLowerCase());
        areas.push({ name: name && name.trim() ? name.trim() : nameFor(folder), folder, order: areas.length + 1 });
    }
    return areas;
}

/** The areas `markdown` declares, or the PARA set when it declares none. */
export function parseConventions(markdown) {
    const declared = declaredAreas(markdown);
    return declared.length ? declared : PARA_AREAS.map((area) => ({ ...area }));
}

/** The areas of the Brain at `brainRoot`: its `CONVENTIONS.md`'s, or PARA. */
export async function readBrainAreas(brainRoot) {
    try {
        return parseConventions(await readFile(path.join(brainRoot, CONVENTIONS_FILE), 'utf8'));
    } catch {
        return PARA_AREAS.map((area) => ({ ...area }));
    }
}

async function isDirectory(absolute) {
    try {
        return (await stat(absolute)).isDirectory();
    } catch {
        return false;
    }
}

const asList = (value) => (value === null || value === undefined ? [] : Array.isArray(value) ? value : [value]);

/** One flag per line: inside a fenced block, fences included (CommonMark). */
function fenceMask(lines) {
    const fenced = new Array(lines.length).fill(false);
    let open = null;
    for (let index = 0; index < lines.length; index++) {
        const match = /^ {0,3}(`{3,}|~{3,})(.*)$/.exec(lines[index]);
        if (open) {
            fenced[index] = true;
            if (match && match[1][0] === open.character && match[1].length >= open.length && match[2].trim() === '') open = null;
        } else if (match) {
            fenced[index] = true;
            open = { character: match[1][0], length: match[1].length };
        }
    }
    return fenced;
}

/** Every `repo:` link target in a Markdown link's destination on `line`. */
export function repoLinks(line) {
    return [...line.matchAll(/\]\(\s*<?(repo:[^\s)>]+)>?/g)].map((match) => match[1]);
}

/**
 * The `repo-link` edges of one note: one per `repo:` link in its prose, outside
 * every fence, from the chapter the link sits in — the nearest enclosing heading
 * that is a node, as the graph's `contains` edges nest them — or from the file
 * above its first chapter. One edge per source and target.
 */
export function repoLinkEdges(relPath, markdown, chapters) {
    const lines = markdown.split(/\r?\n/);
    const fenced = fenceMask(lines);

    // The node each heading's slice belongs to, walked as buildGraph walks them.
    const owners = [];
    const ancestors = [{ level: 1, id: relPath }];
    const seen = new Set();
    for (const chapter of chapters) {
        if (chapter.level === 1) {
            owners.push({ line: chapter.line, id: relPath });
            continue;
        }
        while (ancestors.length && ancestors[ancestors.length - 1].level >= chapter.level) ancestors.pop();
        const parent = ancestors.length ? ancestors[ancestors.length - 1].id : relPath;
        const id = `${relPath}#${chapter.slug}`;
        if (seen.has(id) || !chapter.meta) {
            seen.add(id);
            owners.push({ line: chapter.line, id: parent });
            continue;
        }
        seen.add(id);
        ancestors.push({ level: chapter.level, id });
        owners.push({ line: chapter.line, id });
    }

    const edges = [];
    const ids = new Set();
    let owner = 0;
    for (let index = 0; index < lines.length; index++) {
        while (owner < owners.length && owners[owner].line <= index + 1) owner++;
        if (fenced[index]) continue;
        const source = owner === 0 ? relPath : owners[owner - 1].id;
        for (const target of repoLinks(lines[index])) {
            const id = `${REPO_LINK_EDGE}:${source}->${target}`;
            if (ids.has(id)) continue;
            ids.add(id);
            edges.push({ id, type: REPO_LINK_EDGE, source, target });
        }
    }
    return edges;
}

/**
 * The generator for a Brain: `base` — the devbook generator `generator.mjs`
 * loaded — with the Brain's areas as its folders.
 *
 * The devbook generator knows none of a Brain's folders, so on a Brain its
 * `buildGraph` leaves every node's folder empty, applies no folder's resting
 * status or `type` fallback — right for a Brain, whose areas have neither — and
 * records a reference into an area that resolves to nothing, and every `repo:`
 * reference, as an external node. `brainGraph` projects that onto the Brain:
 * each node in an area carries the area as its folder; a reference into an area
 * that resolves to nothing draws nothing, as an unresolved one inside the devbook
 * folders does; a `repo:` reference keeps its edge and loses the invented node, so
 * it is stored unresolved; and the `para`, `repos` and `tasks` fields become node
 * attributes — `para` on every file, from the folder unless the note overrides it.
 */
export async function loadBrainGenerator(brainRoot, base) {
    const areas = await readBrainAreas(brainRoot);
    const present = [];
    for (const area of areas) {
        if (await isDirectory(path.join(brainRoot, area.folder))) present.push(area.folder);
    }

    const areaForPath = (relPath) => {
        const normalized = String(relPath).replace(/\\/g, '/');
        return present.find((folder) => normalized.startsWith(`${folder}/`)) ?? null;
    };

    async function brainGraph(root) {
        const graph = await base.buildGraph(root, present);
        const dropped = new Set();
        const nodes = [];
        for (const node of graph.nodes) {
            const area = areaForPath(node.path ?? '');
            if (node.type === 'external' && (String(node.id).startsWith(REPO_SCHEME) || area !== null)) {
                if (area !== null) dropped.add(node.id);
                continue;
            }
            if (area !== null) node.folder = area;
            nodes.push(node);
        }

        const byId = new Map(nodes.map((node) => [node.id, node]));
        const edges = graph.edges.filter((edge) => !dropped.has(edge.target));

        for (const folder of present) {
            for (const relPath of await markdownUnder(root, folder)) {
                const markdown = await readFile(path.join(root, relPath), 'utf8');
                const { chapters } = base.parseDocument(markdown);

                const file = byId.get(relPath);
                const fileMeta = chapters.find((chapter) => chapter.level === 1)?.meta ?? null;
                if (file) applyBrainFields(file, fileMeta, folder);

                const claimed = new Set();
                for (const chapter of chapters) {
                    if (chapter.level === 1) continue;
                    const id = `${relPath}#${chapter.slug}`;
                    if (claimed.has(id)) continue;
                    claimed.add(id);
                    const node = byId.get(id);
                    if (node?.type === 'chapter') applyBrainFields(node, chapter.meta, null);
                }

                edges.push(...repoLinkEdges(relPath, markdown, chapters));
            }
        }

        // The generator's lint is the devbook's: it says a note "is not under
        // .devbook/" and that a `repo:` reference points outside the devbook
        // folders, both of which are what a Brain is. Neither is a problem here.
        const problems = graph.problems.filter((problem) =>
            !/no metadata rules apply/.test(problem.message)
            && !(problem.message.includes(`"${REPO_SCHEME}`) && problem.message.includes('recorded as an external node')));

        return { ...graph, nodes, edges, problems };
    }

    return {
        ...base,
        layout: 'brain',
        source: `${base.source} (brain convention)`,
        folders: areas.map((area) => area.folder),
        areas,
        listAttributes: [...BRAIN_ATTRIBUTES],
        checksDemos: false,
        discoverScopes: async () => (present.length ? [base.REPO_SCOPE, ...present] : []),
        folderKindForPath: areaForPath,
        buildGraph: brainGraph,
    };
}

/** `para`, `repos` and `tasks` onto a node: `para` from `area` when the note
 *  does not override it — a file always has one, a chapter only when it says. */
function applyBrainFields(node, meta, area) {
    const para = meta?.para;
    if (para !== null && para !== undefined) node.para = String(para);
    else if (area !== null) node.para = area;

    for (const field of ['repos', 'tasks']) {
        const values = asList(meta?.[field]);
        if (values.length) node[field] = values.map(String);
        else delete node[field];
    }
}

/** Every `.md` under `folder`, as the generator's graph walk finds them. A
 *  reparse point is neither a file nor a directory to `Dirent`, so a OneDrive
 *  placeholder is skipped here where the C# builder indexes it by name: the one
 *  stated difference between the two writers (`.devbook/tech/tooling.md`). */
async function markdownUnder(root, folder) {
    const { readdir } = await import('node:fs/promises');
    const found = [];
    async function walk(relative) {
        let entries;
        try {
            entries = await readdir(path.join(root, relative), { withFileTypes: true });
        } catch {
            return;
        }
        for (const entry of entries) {
            const child = `${relative}/${entry.name}`;
            if (entry.isDirectory()) await walk(child);
            else if (entry.isFile() && entry.name.endsWith('.md')) found.push(child);
        }
    }
    await walk(folder);
    return found.sort();
}
