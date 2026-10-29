// brain.mjs — the Brain's convention, for the database build.
//
//   node tools/devbook/build-database.mjs --brain --root <brain folder> --out <file>
//
// The Brain is one devbook-shaped folder per user, outside every repository
// (local ADR 0026). It keeps the devbook's `meta` blocks and `annotation` fences
// but not its folders: instead of arc42/domain/tech/design/ai it has areas, each
// a top folder — the ones its own `CONVENTIONS.md` declares, or the PARA set when
// it declares none, then every other top folder holding a note, minus the
// folders its `Ignore` section lists (local ADR 0026 §2). A note adds three
// fields — `para`, `repos` and `tasks` — and may link into a repository devbook
// with `repo:<alias>/<devbook path>[#slug]` (local ADR 0027 §6), which no Brain
// database can resolve: those links are stored as edges whose target is no node.
//
// Nothing here parses a chapter. `loadBrainGenerator` wraps the generator this
// repository has installed — the same `parseDocument`, `buildGraph`,
// `buildOutlineDocument` and `collectAnnotations` the devbook build uses — and
// changes only what the Brain changes: which folders are read, which folder a
// path is in, and the projection of the graph described at `brainGraph`. The C#
// builder's Brain convention (`DevbookBuildLayout` with `DevbookCorpusKind.Brain`)
// is held to this by `DevbookBuilderParityTests`, and `BrainConventions.cs` is the
// C# twin of `parseConventions`, `ignoredFolders` and `readBrainAreas` below.

import { readdir, readFile, readlink, stat } from 'node:fs/promises';
import path from 'node:path';

/** The file at the Brain's root that declares its areas. */
export const CONVENTIONS_FILE = 'CONVENTIONS.md';

/** The heading the areas are declared under. */
const AREAS_HEADING = 'areas';

/** The heading the folders that are never areas are listed under. */
const IGNORE_HEADING = 'ignore';

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
 * Every folder the section under `headingName` names, as written: table rows
 * with a `Folder` column and list items, outside code fences.
 */
function sectionCandidates(markdown, headingName) {
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
            if (sectionLevel === null && text.toLowerCase() === headingName) {
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

    return candidates;
}

/**
 * The areas `markdown` declares under its `Areas` heading, in order — empty when
 * it declares none. A table needs a `Folder` column and may carry `Area` or
 * `Name` and `Order`; a list item names its folder in backticks, or is the
 * folder. See `BrainConventions.cs`, which this mirrors line for line.
 */
export function declaredAreas(markdown) {
    const ordered = sectionCandidates(markdown, AREAS_HEADING)
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

/**
 * The folders `markdown` lists under its `Ignore` heading, in order: list items
 * naming the folder in backticks or bare, or a table's `Folder` column. A folder
 * that is not one plain segment is skipped, one listed twice counts once.
 */
export function ignoredFolders(markdown) {
    const seen = new Set();
    const folders = [];
    for (const { folder } of sectionCandidates(markdown, IGNORE_HEADING)) {
        if (!isAreaFolder(folder) || seen.has(caseKey(folder))) continue;
        seen.add(caseKey(folder));
        folders.push(folder);
    }
    return folders;
}

/**
 * A folder name as .NET's `StringComparer.OrdinalIgnoreCase` sees it: each code
 * point upper-cased on its own by simple case mapping. A code point whose upper
 * case is longer (`ß`) is kept, and so is dotless `ı` (U+0131), which .NET
 * deliberately leaves alone where JavaScript maps it to `I`.
 *
 * Known limits, both outside any plain folder name seen so far:
 * - Node's ICU may know a newer Unicode than .NET's casing table: U+019B,
 *   U+0264, U+1C8A, U+A7CD, U+A7CF, U+A7D3, U+A7D5 and U+A7DB upper-case
 *   here and not in .NET 10. No generic rule closes that gap.
 * - The key is compared by UTF-16 unit, as `ordinal` does, so a name with an
 *   astral character (U+10428) may sort apart from .NET's order against a BMP
 *   character above the surrogates (U+FF41).
 */
function caseKey(folder) {
    let key = '';
    for (const point of folder) {
        const upper = point === 'ı' ? point : point.toUpperCase();
        key += upper.length === point.length ? upper : point;
    }
    return key;
}

/** UTF-16 code-unit order, as .NET's ordinal comparison. */
const ordinal = (left, right) => (left < right ? -1 : left > right ? 1 : 0);

/** Folder names in tab order: ordinal by upper case, then ordinal. */
const folderOrder = (left, right) => ordinal(caseKey(left), caseKey(right)) || ordinal(left, right);

/**
 * The areas from what `readBrainAreas` finds on disk: `declared` (or the PARA set
 * when it is empty) in order, minus `ignored`, then every top folder that can be
 * an area, is neither declared nor ignored, and `holdsNote` — by folder name,
 * named after the folder. `BrainConventions.Resolve` is its twin.
 */
export async function resolveAreas(declared, ignored, topFolders, holdsNote) {
    const leading = declared.length ? declared : PARA_AREAS;
    const skipped = new Set(ignored.map(caseKey));
    const named = new Set(leading.map((area) => caseKey(area.folder)));

    const kept = leading.filter((area) => !skipped.has(caseKey(area.folder)));
    const candidates = [];
    for (const folder of topFolders) {
        if (!isAreaFolder(folder) || skipped.has(caseKey(folder)) || named.has(caseKey(folder))) continue;
        named.add(caseKey(folder));
        candidates.push(folder);
    }
    candidates.sort(folderOrder);

    const areas = kept.map((area, index) => ({ ...area, order: index + 1 }));
    for (const folder of candidates) {
        if (await holdsNote(folder)) areas.push({ name: nameFor(folder), folder, order: areas.length + 1 });
    }
    return areas;
}

/** Whether a directory entry leads to a folder. A link is told by its target,
 *  and a placeholder — a reparse point `readlink` cannot read — by `stat`,
 *  which reads attributes and downloads nothing. */
async function leadsToFolder(directory, entry) {
    if (entry.isDirectory()) return true;
    if (!entry.isSymbolicLink()) return false;
    try {
        return (await stat(path.join(directory, entry.name))).isDirectory();
    } catch {
        return false;
    }
}

/** Whether a reparse point is a link — a symbolic link or a junction, which
 *  could loop — rather than a placeholder, as `BrainConventions.IsLink` tells. */
async function isLink(absolute) {
    try {
        await readlink(absolute);
        return true;
    } catch {
        return false;
    }
}

/** The names of the folders at the Brain's root, links and placeholders included. */
async function topFolders(brainRoot) {
    let entries;
    try {
        entries = await readdir(brainRoot, { withFileTypes: true });
    } catch {
        return [];
    }
    const folders = [];
    for (const entry of entries) {
        if (await leadsToFolder(brainRoot, entry)) folders.push(entry.name);
    }
    return folders;
}

/**
 * Whether a `.md` sits anywhere under `directory`, outside its dot sub-folders:
 * by name alone, stopping at the first, reparse points included — a OneDrive
 * placeholder counts here, as it does to `BrainConventions`, although the
 * indexing below still skips it. A sub-folder that is a link is not followed.
 */
async function holdsNote(directory) {
    let entries;
    try {
        entries = await readdir(directory, { withFileTypes: true });
    } catch {
        return false;
    }
    const folders = [];
    for (const entry of entries) {
        if (!(await leadsToFolder(directory, entry))) {
            if (entry.name.endsWith('.md')) return true;
            continue;
        }
        if (entry.name.startsWith('.')) continue;
        const child = path.join(directory, entry.name);
        if (entry.isSymbolicLink() && (await isLink(child))) continue;
        folders.push(child);
    }
    for (const child of folders) {
        if (await holdsNote(child)) return true;
    }
    return false;
}

/** The text of the Brain's `CONVENTIONS.md`, or null when it cannot be read. */
async function readConventions(brainRoot) {
    try {
        return await readFile(path.join(brainRoot, CONVENTIONS_FILE), 'utf8');
    } catch {
        return null;
    }
}

/** The areas of the Brain at `brainRoot`: its `CONVENTIONS.md`'s, or PARA, then
 *  the top folders holding a note, minus the ignored ones. */
export async function readBrainAreas(brainRoot) {
    const markdown = await readConventions(brainRoot);
    return resolveAreas(declaredAreas(markdown), ignoredFolders(markdown), await topFolders(brainRoot), (folder) =>
        holdsNote(path.join(brainRoot, folder)));
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
