// parse.mjs — reads a scenario page, the spec that implements it, and the
// repository's profile file. Every other tool in this folder reads through it,
// so the shape of a page is fixed in one place.
//
// Plain ESM with no dependencies: the coverage check and the signature run
// without a browser, an application, or a build step.

import { readdir, readFile } from "node:fs/promises";
import path from "node:path";

/** Where a repository keeps its runs, profiles, and data sets, unless it says otherwise. */
export const SCENARIO_FOLDER = ".devbook/scenarios";
/** Where scenario pages live: a bounded context folder under it, at any depth. */
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

/**
 * The GitHub anchor of a heading — the same algorithm the devbook checker
 * uses, so a part's anchor here is the one `Proved by:` and `related` name.
 */
export function slugify(text) {
    return String(text)
        .toLowerCase()
        .trim()
        .replace(/[^\p{L}\p{N}_\s-]/gu, "")
        .replace(/\s/g, "-");
}

/** A scenario page's stem: its file name without `.md`. */
export function stemOf(relPath) {
    return (String(relPath).replace(/\\/g, "/").split("/").pop() ?? "").replace(/\.md$/i, "");
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
 * Read one scenario page.
 *
 * Returns `{ path, stem, title, isScenario, meta, setup, parts, shots, issues }`:
 * `setup` holds the six setup fields (lists as lists, `profile` a string or
 * null); each part is `{ title, anchor, line, steps, shots }`, a step being
 * `{ keyword, text, line }` with its continuation lines folded in; `shots` is
 * every `shot:` image in document order as `{ label, caption, line, part,
 * inStepList }`, `part` the anchor of the part it sits in or null above the
 * first; `issues` are the label and placement problems the page carries on its
 * own, each `{ kind, line, label, message }`.
 */
export function parseScenarioPage(markdown, relPath = "") {
    const lines = String(markdown).split(/\r?\n/);
    const page = {
        path: String(relPath).replace(/\\/g, "/"),
        stem: stemOf(relPath),
        title: null,
        isScenario: false,
        meta: {},
        setup: {},
        parts: [],
        shots: [],
        issues: [],
    };

    let fence = null;
    let part = null;
    let inList = false;
    let blankSince = false;
    let lastStep = null;

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
        if (heading && heading[1].length === 1 && page.title === null) {
            page.title = heading[2].trim();
            let j = i + 1;
            while (j < lines.length && lines[j].trim() === "") j++;
            if (j < lines.length && /^```meta\s*$/.test(lines[j].trim())) {
                const body = [];
                let k = j + 1;
                while (k < lines.length && lines[k].trim() !== "```") body.push(lines[k++]);
                page.meta = parseMeta(body.join("\n"));
                i = k;
            }
            continue;
        }
        if (heading && heading[1].length === 2) {
            part = { title: heading[2].trim(), anchor: slugify(heading[2]), line: i + 1, steps: [], shots: [] };
            page.parts.push(part);
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
            const shot = { label: match[2], caption: match[1], line: i + 1, part: part?.anchor ?? null, inStepList };
            page.shots.push(shot);
            part?.shots.push(shot);
            // A label inside a step would be text of the step it sits in; take it back out.
            if (inStepList && lastStep) lastStep.text = collapse(lastStep.text.replace(collapse(match[0]), ""));
        }
    }

    page.isScenario = page.meta?.type === "scenario";
    page.setup = {
        start: page.meta.start == null ? null : String(page.meta.start),
        actor: asList(page.meta.actor),
        data: asList(page.meta.data),
        profile: page.meta.profile == null || page.meta.profile === "" ? null : String(page.meta.profile),
        flags: asList(page.meta.flags),
        settings: asList(page.meta.settings),
    };

    const seen = new Map();
    for (const shot of page.shots) {
        const at = `line ${shot.line}`;
        if (!LABEL.test(shot.label)) {
            page.issues.push({ kind: "bad-label", line: shot.line, label: shot.label, message: `${at}: label "${shot.label}" is not [a-z0-9-]+.` });
        } else if (seen.has(shot.label)) {
            page.issues.push({ kind: "bad-label", line: shot.line, label: shot.label, message: `${at}: label "${shot.label}" is already used on line ${seen.get(shot.label)}; a label is unique in its page.` });
        } else {
            seen.set(shot.label, shot.line);
        }
        if (shot.inStepList) {
            page.issues.push({
                kind: "image-in-steps",
                line: shot.line,
                label: shot.label,
                message: `${at}: screenshot "${shot.label}" sits inside a step list; put it between step lists, after a blank line.`,
            });
        }
    }
    return page;
}

/** The labels of a page, in document order, each once. */
export function labelsOf(page) {
    return [...new Set(page.shots.map((shot) => shot.label))];
}

/**
 * The header a derived spec opens with: `// scenario: <page path>` and
 * `// signature: <8 hex>` in its first lines. Either is null when absent.
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
            entries = await readdir(path.join(repoRoot, rel), { withFileTypes: true });
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

/** Every scenario page in the repository, parsed, sorted by path. */
export async function listScenarioPages(repoRoot) {
    const pages = [];
    for (const relPath of await markdownUnder(repoRoot, DOMAIN_FOLDER)) {
        const page = parseScenarioPage(await readFile(path.join(repoRoot, relPath), "utf8"), relPath);
        if (page.isScenario) pages.push(page);
    }
    return pages;
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
            entries = await readdir(path.join(repoRoot, rel), { withFileTypes: true });
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
        const source = await readFile(path.join(repoRoot, file), "utf8");
        const header = parseSpecHeader(source);
        if (header.page) specs.push({ path: file, ...header, page: normalizePath(header.page), source });
    }
    return specs;
}

/**
 * The repository's profiles: `{ map }` when the file parses, `{ missing: true }`
 * when there is none, `{ error }` when it does not parse.
 */
export async function readProfiles(repoRoot, scenarioFolder = SCENARIO_FOLDER) {
    let text;
    try {
        text = await readFile(path.join(repoRoot, scenarioFolder, PROFILES_FILE), "utf8");
    } catch {
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
