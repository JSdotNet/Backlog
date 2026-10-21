// parse.mjs — reads a scenarios file and its journeys, the spec that
// implements a journey, and the repository's profile file. Every other tool in
// this folder reads through it, so the shape of a journey is fixed in one place.
//
// Plain ESM with no dependencies: the coverage check and the signature run
// without a browser, an application, or a build step.

import { readdir, readFile } from "node:fs/promises";
import path from "node:path";

/** Where a repository keeps its profiles and data sets, unless it says otherwise. Runs live beside their scenarios file. */
export const SCENARIO_FOLDER = ".devbook/scenarios";
/** Where scenarios files live: `scenarios.md` and `scenarios.<name>.md` in a bounded context folder under it. */
export const DOMAIN_FOLDER = ".devbook/domain";
/** The profile file, inside the scenario folder. */
export const PROFILES_FILE = "profiles.json";
/** The six fields that say where a journey starts and in which configuration it runs. */
export const SETUP_FIELDS = ["start", "actor", "data", "profile", "flags", "settings"];
/** The words a step opens with. */
export const STEP_KEYWORDS = ["Given", "When", "Then", "And"];
/** A screenshot label: lowercase letters, digits, and hyphens. */
export const LABEL = /^[a-z0-9-]+$/;
/** The attachment names `shot.ts` writes and the run reporter reads. */
export const SHOT_ATTACHMENT = "scenario-shot:";
export const FAILURE_ATTACHMENT = "scenario-failure";

const SPEC_FILE = /\.(?:spec|test)\.[cm]?[jt]sx?$/;
const SPEC_HEADER_LINES = 10;
const HEADER_PAGE = /^\s*\/\/\s*scenario:\s*(\S+)\s*$/;
const HEADER_SIGNATURE = /^\s*\/\/\s*signature:\s*([0-9a-f]+)\s*$/i;
const SHOT_IMAGE = /!\[([^\]]*)\]\(\s*<?shot:([^)\s>]*)>?(?:\s+(?:"[^"]*"|'[^']*'))?\s*\)/g;
const LIST_ITEM = /^( {0,3})(?:[-*+]|\d+[.)])\s+(.*)$/;
// Build output, and every dot-folder below a root — `.git`, `.devbook`, worktree copies under
// `.claude/` — as the devbook checker skips them. A root may itself be a dot-folder (`.test`).
const SKIP_DIRS = new Set(["node_modules", "bin", "obj", "dist", "build", "out", "coverage", "test-results", "playwright-report"]);

/** The `code` of the error `resolveInside` throws. */
export const OUTSIDE_ROOT = "EOUTSIDE";

/**
 * `given` resolved against `root`, or a thrown error with code `OUTSIDE_ROOT`
 * when it resolves outside `root`; `root` itself counts as inside. Every read
 * these tools make of a path taken from an argument, a page, a spec header, or
 * a scanned tree goes through it. `../devbook-meta/paths.mjs` is the checker's copy.
 */
export function resolveInside(root, given) {
    const base = path.resolve(root);
    const file = path.resolve(base, given);
    const rel = path.relative(base, file);
    if (rel === ".." || rel.startsWith(`..${path.sep}`) || path.isAbsolute(rel)) {
        const error = new Error(`${String(given).replace(/\\/g, "/")} resolves outside the repository`);
        error.code = OUTSIDE_ROOT;
        throw error;
    }
    return file;
}

/** A data set's folder, `<scenarioFolder>/data/<name>/`, or null when either resolves outside the repository. */
export function dataFolder(repoRoot, scenarioFolder, name) {
    try {
        return resolveInside(resolveInside(repoRoot, `${scenarioFolder}/data`), name);
    } catch {
        return null;
    }
}

/**
 * The GitHub anchor of a heading — the same algorithm the devbook checker
 * uses, so a journey's or a part's anchor here is the one `Proved by:` and
 * `related` name.
 */
export function slugify(text) {
    return String(text)
        .toLowerCase()
        .trim()
        .replace(/[^\p{L}\p{N}_\s-]/gu, "")
        .replace(/\s/g, "-");
}

/** Split a portal prefix off a setup entry: `admin:/route` → `{ portal: "admin", rest: "/route" }`. */
export function splitPortal(entry) {
    const match = /^([a-z0-9][a-z0-9-]*):(.+)$/.exec(String(entry));
    return match ? { portal: match[1], rest: match[2] } : { portal: null, rest: String(entry) };
}

/** A repository-relative path as POSIX, without a leading `./`. */
export function normalizePath(relPath) {
    return String(relPath).replace(/\\/g, "/").replace(/^(?:\.\/)+/, "");
}

/** Collapse runs of whitespace to one space and trim. */
export function collapse(text) {
    return String(text).replace(/\s+/g, " ").trim();
}

/** A `meta` block body as an object, with the checker's scalar and `[a, b]` list syntax. */
export function parseMeta(body) {
    const result = {};
    for (const line of String(body).split(/\r?\n/)) {
        const at = line.indexOf(":");
        if (!line.trim() || at === -1) continue;
        result[line.slice(0, at).trim()] = parseScalar(line.slice(at + 1));
    }
    return result;
}

function parseScalar(raw) {
    const value = raw.trim();
    if (value === "" || value === "null") return null;
    if (value.startsWith("[") && value.endsWith("]")) {
        const inner = value.slice(1, -1).trim();
        if (!inner) return [];
        const entries = [];
        let quote = null;
        let current = "";
        for (const char of inner) {
            if (quote) {
                if (char === quote) quote = null;
            } else if ((char === '"' || char === "'") && current.trim() === "") {
                quote = char;
            } else if (char === ",") {
                entries.push(current);
                current = "";
                continue;
            }
            current += char;
        }
        entries.push(current);
        return entries.map((entry) => unquote(entry.trim())).filter(Boolean);
    }
    return unquote(value);
}

function unquote(value) {
    const first = value[0];
    return (first === '"' || first === "'") && value.endsWith(first) && value.length > 1 ? value.slice(1, -1) : value;
}

function asList(value) {
    if (value === null || value === undefined || value === "") return [];
    return (Array.isArray(value) ? value : [value]).map(String);
}

/** Replace code spans with spaces, so a quoted `![](shot:x)` is not read as an image. */
function maskCodeSpans(line) {
    return line.replace(/(`+)[^`]*?\1/g, (span) => " ".repeat(span.length));
}

/**
 * Whether a path is a scenarios file: `scenarios.md` or a split
 * `scenarios.<name>.md`, directly in a bounded context folder.
 */
export function isScenariosPath(relPath) {
    const rel = normalizePath(relPath);
    if (!rel.startsWith(`${DOMAIN_FOLDER}/`)) return false;
    const segments = rel.slice(DOMAIN_FOLDER.length + 1).split("/");
    return segments.length === 2 && /^scenarios(?:\.[^./]+)?\.md$/i.test(segments[1]);
}

/** A journey's address: the scenarios file and the journey's own anchor. */
export function journeyAddress(relPath, slug) {
    return `${normalizePath(relPath)}#${slug}`;
}

/** Split an address into `{ path, anchor }`; `anchor` is null when there is no `#`. */
export function splitAddress(address) {
    const text = normalizePath(address);
    const at = text.indexOf("#");
    return at < 0 ? { path: text, anchor: null } : { path: text.slice(0, at), anchor: text.slice(at + 1) };
}

/**
 * Where a journey's runs live: its scenarios file without `.md`, then the
 * journey's anchor — `scenarios.returns.md#return-a-parcel` runs into
 * `scenarios.returns/return-a-parcel/`.
 */
export function runFolderOf(journey) {
    return `${normalizePath(journey.path).replace(/\.md$/i, "")}/${journey.slug}`;
}

function readMetaAfter(lines, i) {
    let j = i + 1;
    while (j < lines.length && lines[j].trim() === "") j++;
    if (j >= lines.length || !/^```meta\s*$/.test(lines[j].trim())) return { meta: null, end: i };
    const body = [];
    let k = j + 1;
    while (k < lines.length && lines[k].trim() !== "```") body.push(lines[k++]);
    return { meta: parseMeta(body.join("\n")), end: k };
}

function setupOf(meta) {
    return {
        start: meta.start == null ? null : String(meta.start),
        actor: asList(meta.actor),
        data: asList(meta.data),
        profile: meta.profile == null || meta.profile === "" ? null : String(meta.profile),
        flags: asList(meta.flags),
        settings: asList(meta.settings),
    };
}

/**
 * Read one scenarios file.
 *
 * Returns `{ path, title, isScenarios, legacyPage, meta, journeys, shots,
 * issues }`. Each `##` is a journey: `{ path, slug, id, title, line,
 * isScenario, meta, setup, parts, shots }`, `id` its address and `isScenario`
 * whether its block says `type: scenario`; `setup` holds the six setup fields
 * (lists as lists, `profile` a string or null). Each `###` under a journey is
 * a part, `{ title, anchor, line, steps, shots }`, a step being `{ keyword,
 * text, line }` with its continuation lines folded in. `shots` is every
 * `shot:` image in document order as `{ label, caption, line, journey, part,
 * inStepList }`, `part` null above a journey's first part. `issues` are the
 * label, placement, and anchor problems the file carries on its own, each
 * `{ kind, line, label?, message }`. `legacyPage` is true for a page of the
 * contract before 32: a whole file typed `scenario` on its `#` block.
 */
export function parseScenariosFile(markdown, relPath = "") {
    const lines = String(markdown).split(/\r?\n/);
    const file = {
        path: normalizePath(relPath),
        title: null,
        isScenarios: isScenariosPath(relPath),
        legacyPage: false,
        meta: {},
        journeys: [],
        shots: [],
        issues: [],
    };

    let fence = null;
    let journey = null;
    let part = null;
    let inList = false;
    let blankSince = false;
    let lastStep = null;
    const anchors = new Map();
    const claim = (anchor, line, what) => {
        if (anchors.has(anchor)) {
            file.issues.push({ kind: "duplicate-anchor", line, message: `line ${line}: ${what} "#${anchor}" repeats the anchor on line ${anchors.get(anchor)}; every journey and part heading is unique in its file.` });
        } else {
            anchors.set(anchor, line);
        }
    };

    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const marker = /^\s*(`{3,}|~{3,})/.exec(line);
        if (fence) {
            if (marker && marker[1][0] === fence[0] && marker[1].length >= fence.length) fence = null;
            continue;
        }
        if (marker) {
            fence = marker[1];
            continue;
        }

        const heading = /^(#{1,6})\s+(.*)$/.exec(line);
        if (heading && heading[1].length === 1 && file.title === null) {
            file.title = heading[2].trim();
            const { meta, end } = readMetaAfter(lines, i);
            file.meta = meta ?? {};
            i = end;
            continue;
        }
        if (heading && heading[1].length === 2) {
            const title = heading[2].trim();
            const slug = slugify(title);
            const { meta, end } = readMetaAfter(lines, i);
            journey = {
                path: file.path,
                slug,
                id: journeyAddress(file.path, slug),
                title,
                line: i + 1,
                isScenario: meta?.type === "scenario",
                meta: meta ?? {},
                setup: setupOf(meta ?? {}),
                parts: [],
                shots: [],
            };
            file.journeys.push(journey);
            claim(slug, i + 1, "journey");
            part = null;
            inList = false;
            blankSince = false;
            lastStep = null;
            i = end;
            continue;
        }
        if (heading && heading[1].length === 3 && journey) {
            part = { title: heading[2].trim(), anchor: slugify(heading[2]), line: i + 1, steps: [], shots: [] };
            journey.parts.push(part);
            claim(part.anchor, i + 1, "part");
            inList = false;
            blankSince = false;
            lastStep = null;
            continue;
        }
        if (heading) {
            inList = false;
            lastStep = null;
            continue;
        }

        if (line.trim() === "") {
            blankSince = true;
            continue;
        }

        const indented = /^\s{2,}\S/.test(line) || /^\t/.test(line);
        const item = LIST_ITEM.exec(line);
        const topItem = item && item[1].length <= 1;
        let inStepList = false;

        if (part && topItem) {
            const bold = /^\*\*([^*]+)\*\*\s*(.*)$/.exec(item[2]);
            const keyword = bold && STEP_KEYWORDS.includes(bold[1].trim()) ? bold[1].trim() : null;
            lastStep = { keyword, text: collapse(keyword ? bold[2] : item[2]), line: i + 1 };
            part.steps.push(lastStep);
            inList = true;
            inStepList = true;
        } else if (part && inList && (indented || item || !blankSince)) {
            // A nested item, an indented line, or a lazy continuation belongs to the step above.
            if (lastStep) lastStep.text = collapse(`${lastStep.text} ${line.replace(/^\s*(?:[-*+]|\d+[.)])\s+/, "")}`);
            inStepList = true;
        } else {
            inList = false;
            lastStep = null;
        }
        blankSince = false;

        for (const match of maskCodeSpans(line).matchAll(SHOT_IMAGE)) {
            const shot = { label: match[2], caption: match[1], line: i + 1, journey: journey?.slug ?? null, part: part?.anchor ?? null, inStepList };
            file.shots.push(shot);
            journey?.shots.push(shot);
            part?.shots.push(shot);
            // A label inside a step would be text of the step it sits in; take it back out.
            if (inStepList && lastStep) lastStep.text = collapse(lastStep.text.replace(collapse(match[0]), ""));
        }
    }

    file.legacyPage = file.meta?.type === "scenario";

    const seen = new Map();
    for (const shot of file.shots) {
        const at = `line ${shot.line}`;
        if (!LABEL.test(shot.label)) {
            file.issues.push({ kind: "bad-label", line: shot.line, label: shot.label, message: `${at}: label "${shot.label}" is not [a-z0-9-]+.` });
        } else if (seen.has(shot.label)) {
            file.issues.push({ kind: "bad-label", line: shot.line, label: shot.label, message: `${at}: label "${shot.label}" is already used on line ${seen.get(shot.label)}; a label is unique in its file.` });
        } else {
            seen.set(shot.label, shot.line);
        }
        if (shot.journey === null) {
            file.issues.push({ kind: "shot-outside-journey", line: shot.line, label: shot.label, message: `${at}: screenshot "${shot.label}" sits above the first journey; put it inside the journey it shows.` });
        }
        if (shot.inStepList) {
            file.issues.push({
                kind: "image-in-steps",
                line: shot.line,
                label: shot.label,
                message: `${at}: screenshot "${shot.label}" sits inside a step list; put it between step lists, after a blank line.`,
            });
        }
    }
    return file;
}

/** The labels of a journey, in document order, each once. */
export function labelsOf(journey) {
    return [...new Set(journey.shots.map((shot) => shot.label))];
}

/**
 * The header a derived spec opens with: `// scenario: <journey address>` —
 * `<scenarios file>#<journey>` — and `// signature: <8 hex>` in its first
 * lines. Either is null when absent; `page` keeps the name the header field
 * had before journeys, and holds the address.
 */
export function parseSpecHeader(source) {
    let page = null;
    let signature = null;
    for (const line of String(source).split(/\r?\n/).slice(0, SPEC_HEADER_LINES)) {
        page ??= HEADER_PAGE.exec(line)?.[1] ?? null;
        signature ??= HEADER_SIGNATURE.exec(line)?.[1]?.toLowerCase() ?? null;
    }
    return { page, signature };
}

/** Every `shot(<page>, '<label>')` call in a spec, in source order. Commented lines are skipped. */
export function specLabels(source) {
    const labels = [];
    for (const line of String(source).split(/\r?\n/)) {
        if (/^\s*\/\//.test(line)) continue;
        for (const match of line.matchAll(/\bshot\([^,]*?,\s*(['"`])([^'"`]*)\1/g)) labels.push(match[2]);
    }
    return labels;
}

/** The title of every `part('…')` or `test('…')` a spec declares, in source order. */
export function specParts(source) {
    const titles = [];
    for (const line of String(source).split(/\r?\n/)) {
        if (/^\s*\/\//.test(line)) continue;
        const match = /\b(?:part|test)(?:\.(?:only|skip|fixme|fail|slow))?\s*\(\s*(['"`])((?:\\.|(?!\1).)*)\1/.exec(line);
        if (match) titles.push(match[2].replace(/\\(.)/g, "$1"));
    }
    return titles;
}

/** Every Markdown file under a folder, as repository-relative POSIX paths; `_meta` and `_tools` are skipped. */
async function markdownUnder(repoRoot, folder) {
    const found = [];
    async function walk(rel) {
        let entries;
        try {
            entries = await readdir(resolveInside(repoRoot, rel), { withFileTypes: true });
        } catch {
            return;
        }
        for (const entry of entries) {
            const child = `${rel}/${entry.name}`;
            if (entry.isDirectory()) {
                if (entry.name === "_meta" || entry.name === "_tools" || entry.name.startsWith(".")) continue;
                await walk(child);
            } else if (entry.isFile() && entry.name.endsWith(".md")) {
                found.push(child);
            }
        }
    }
    await walk(folder);
    return found.sort();
}

/** Every scenarios file in the repository, parsed, sorted by path. */
export async function listScenarioFiles(repoRoot) {
    const files = [];
    for (const relPath of await markdownUnder(repoRoot, DOMAIN_FOLDER)) {
        if (!isScenariosPath(relPath)) continue;
        files.push(parseScenariosFile(await readFile(resolveInside(repoRoot, relPath), "utf8"), relPath));
    }
    return files;
}

/** Every journey in the repository — a `##` typed `scenario` in a scenarios file — in file and page order. */
export async function listJourneys(repoRoot) {
    return (await listScenarioFiles(repoRoot)).flatMap((file) => file.journeys.filter((journey) => journey.isScenario));
}

/**
 * Every page of the contract before 32 — a whole `domain/` file typed
 * `scenario` on its `#` block — by path. The tools report each one and leave
 * it to the `032-scenarios-md` migration.
 */
export async function listLegacyPages(repoRoot) {
    const found = [];
    for (const relPath of await markdownUnder(repoRoot, DOMAIN_FOLDER)) {
        if (isScenariosPath(relPath)) continue;
        const text = await readFile(resolveInside(repoRoot, relPath), "utf8");
        if (!/type:\s*scenario\s*$/m.test(text)) continue;
        if (parseScenariosFile(text, relPath).legacyPage) found.push(relPath);
    }
    return found;
}

/** Every devbook Markdown file, for the tools that look for `scenario:` references. */
export function listDevbookMarkdown(repoRoot) {
    return markdownUnder(repoRoot, ".devbook");
}

/**
 * Every spec that names the page it implements, as `{ path, page, signature,
 * source }`. `roots` limits the walk to those folders; without it the whole
 * repository is walked, skipping build output and `.devbook/`.
 */
export async function listSpecs(repoRoot, roots = [""]) {
    const files = [];
    async function walk(rel) {
        let entries;
        try {
            entries = await readdir(resolveInside(repoRoot, rel), { withFileTypes: true });
        } catch {
            return;
        }
        for (const entry of entries) {
            const child = rel ? `${rel}/${entry.name}` : entry.name;
            if (entry.isDirectory()) {
                if (!SKIP_DIRS.has(entry.name) && !entry.name.startsWith(".")) await walk(child);
            } else if (entry.isFile() && SPEC_FILE.test(entry.name)) {
                files.push(child);
            }
        }
    }
    for (const root of roots) await walk(String(root).replace(/\\/g, "/").replace(/\/$/, ""));
    const specs = [];
    for (const file of [...new Set(files)].sort()) {
        const source = await readFile(resolveInside(repoRoot, file), "utf8");
        const header = parseSpecHeader(source);
        if (header.page) specs.push({ path: file, ...header, page: normalizePath(header.page), source });
    }
    return specs;
}

/**
 * The repository's profiles: `{ map }` when the file parses, `{ missing: true }`
 * when there is none, `{ error }` when it does not parse or the scenario
 * folder resolves outside the repository.
 */
export async function readProfiles(repoRoot, scenarioFolder = SCENARIO_FOLDER) {
    let text;
    try {
        text = await readFile(resolveInside(repoRoot, `${scenarioFolder}/${PROFILES_FILE}`), "utf8");
    } catch (error) {
        if (error.code === OUTSIDE_ROOT) return { error: "resolves outside the repository" };
        return { missing: true };
    }
    try {
        const map = JSON.parse(text);
        if (!map || typeof map !== "object" || Array.isArray(map)) return { error: "is not a JSON object of profiles by name" };
        return { map };
    } catch (error) {
        return { error: `does not parse: ${error.message}` };
    }
}
