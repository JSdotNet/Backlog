// demo-sync.mjs — copies each linked walkthrough's text from the scenario page
// part it names, and its signature from that page, into the demo's
// `demo-model`.
//
// A linked walkthrough embeds a copy of its part's step text so the demo plays
// with nothing fetched; the copy goes stale whenever the page moves. This tool
// is the one way it is refreshed: per linked walkthrough, each step's `text`
// becomes the text of the part step its `step` names, and `signature` the
// page's current one. Nothing else in the file changes — not a byte of the
// JSON around those values, not the markup.
//
//   node demo-sync.mjs [<demo.html>…] [--root <dir>] [--scenario-folder <dir>] [--check]
//
// Without a file it takes every `*.demo.html` under `.devbook/domain/`.
// `--check` writes nothing and exits 1 while any linked walkthrough is stale or
// drifted; either form exits 1 on a walkthrough it cannot resolve, and 2 on a
// bad argument.
//
// Plain ESM with no dependencies. The demo is read, and the part resolved, by
// the same functions devbook-meta's checker uses — `../devbook-meta/demo.mjs`,
// a sibling of this folder both in the plugin and under `.devbook/_tools/` —
// so the two never disagree on what is stale or drifted.

import { readdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { DEMO_MODEL_ID, LINKED_PART, parseDemo, resolveLinkedPage, stepTextMatches } from "../devbook-meta/demo.mjs";
import { DOMAIN_FOLDER, OUTSIDE_ROOT, SCENARIO_FOLDER, STEP_KEYWORDS, collapse, listScenarioPages, normalizePath, resolveInside } from "./parse.mjs";
import { pageSignature } from "./signature.mjs";

const DEMO_FILE = /\.demo\.html$/i;

// ── The model's JSON, with the span of every value ──────────────────────────

/**
 * Parse JSON `text` into nodes that keep their offsets: `{ kind, start, end,
 * value }`, an object's `members` a Map of key to node, an array's `items`.
 * Offsets are into `text`, so a value is replaced without re-serializing
 * anything around it. Throws on JSON that `JSON.parse` would refuse.
 */
export function parseWithSpans(text) {
    let i = 0;
    const fail = (what) => {
        throw new SyntaxError(`${what} at offset ${i}`);
    };
    const space = () => {
        while (i < text.length && " \t\n\r".includes(text[i])) i++;
    };
    const value = () => {
        space();
        const start = i;
        const ch = text[i];
        if (ch === "{") {
            i++;
            const members = new Map();
            space();
            if (text[i] === "}") return { kind: "object", start, end: ++i, members };
            for (;;) {
                space();
                if (text[i] !== '"') fail("expected a key");
                const key = value();
                space();
                if (text[i++] !== ":") fail("expected ':'");
                members.set(key.value, value());
                space();
                if (text[i] === ",") i++;
                else if (text[i] === "}") return { kind: "object", start, end: ++i, members };
                else fail("expected ',' or '}'");
            }
        }
        if (ch === "[") {
            i++;
            const items = [];
            space();
            if (text[i] === "]") return { kind: "array", start, end: ++i, items };
            for (;;) {
                items.push(value());
                space();
                if (text[i] === ",") i++;
                else if (text[i] === "]") return { kind: "array", start, end: ++i, items };
                else fail("expected ',' or ']'");
            }
        }
        if (ch === '"') {
            i++;
            while (i < text.length && text[i] !== '"') i += text[i] === "\\" ? 2 : 1;
            if (i >= text.length) fail("unterminated string");
            i++;
            return { kind: "string", start, end: i, value: JSON.parse(text.slice(start, i)) };
        }
        const literal = /^(?:-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?|true|false|null)/.exec(text.slice(i));
        if (!literal) fail("unexpected character");
        i += literal[0].length;
        return { kind: "literal", start, end: i, value: JSON.parse(literal[0]) };
    };
    const root = value();
    space();
    if (i !== text.length) fail("unexpected text after the value");
    return root;
}

/** A string as JSON that is safe inside `<script>`: `</` cannot close the element. */
const scriptString = (text) => JSON.stringify(text).replace(/<\//g, "<\\/");

/**
 * The text a linked step takes from `target`, the part step it names. A copy
 * that opened with its keyword keeps opening with one; otherwise the step's
 * text alone.
 */
function copiedText(current, target) {
    const shown = collapse(current ?? "").replace(/^\*\*(Given|When|Then|And)\*\*/, "$1");
    const withKeyword = STEP_KEYWORDS.some((keyword) => shown === keyword || shown.startsWith(`${keyword} `));
    return withKeyword && target.keyword ? `${target.keyword} ${target.text}` : target.text;
}

// ── One demo ────────────────────────────────────────────────────────────────

/**
 * Sync one demo's linked walkthroughs. `pages` maps a scenario page's path to
 * `{ path, stem, page, signature }`. Returns `{ html, changes, problems }`:
 * `html` the file with every stale `signature` and drifted `text` replaced and
 * nothing else; `changes` one `{ id, kind, step?, from, to }` per replaced
 * value, `kind` `signature` or `text`; `problems` the walkthroughs it could not
 * resolve, as messages. `only`, a set of walkthrough ids, limits the sync to those.
 */
export function syncDemo(html, relPath, pages, { only = null } = {}) {
    const changes = [];
    const problems = [];
    const demo = parseDemo(html);
    if (demo.modelError) return { html, changes, problems: [`${relPath} ${demo.modelError}`] };
    if (!demo.model.linked.size) return { html, changes, problems };

    const script = demo.scripts.find((s) => s.id === DEMO_MODEL_ID);
    const bodyAt = html.indexOf(">", script.offset) + 1;
    const root = parseWithSpans(script.body);
    const walkthroughs = root.members?.get("walkthroughs")?.items ?? [];

    const edits = [];
    for (const node of walkthroughs) {
        const id = node.members?.get("id")?.value;
        const partNode = node.members?.get("part");
        if (partNode?.kind !== "string" || !LINKED_PART.test(partNode.value)) continue;
        if (only && !only.has(id)) continue;
        const part = partNode.value;
        const at = `${relPath} walkthrough "${id}" (\`part\` "${part}")`;
        const hash = part.indexOf("#");
        const anchor = part.slice(hash + 1);
        const resolved = resolveLinkedPage(relPath, part.slice(0, hash), pages);
        if (resolved.error) {
            problems.push(`${at}: names no scenario page part — ${resolved.error}.`);
            continue;
        }
        const entry = pages.get(resolved.path);
        const partSteps = entry.page.parts.find((p) => p.anchor === anchor)?.steps;
        if (!partSteps) {
            problems.push(`${at}: ${resolved.path} has no part "#${anchor}".`);
            continue;
        }

        const signatureNode = node.members.get("signature");
        if (signatureNode?.kind === "string" && signatureNode.value !== entry.signature) {
            edits.push({ node: signatureNode, to: scriptString(entry.signature) });
            changes.push({ id, kind: "signature", from: signatureNode.value, to: entry.signature });
        } else if (signatureNode === undefined) {
            problems.push(`${at}: has no \`signature\` to refresh — add \`"signature": "${entry.signature}"\` by hand.`);
        }

        for (const stepNode of node.members.get("steps")?.items ?? []) {
            const index = stepNode.members?.get("step")?.value;
            const textNode = stepNode.members?.get("text");
            if (!Number.isInteger(index) || textNode?.kind !== "string") continue; // the checker reports the shape
            const target = partSteps[index - 1];
            if (!target) {
                problems.push(`${at}: a step names part step ${index}, but the part has ${partSteps.length} step(s).`);
                continue;
            }
            if (stepTextMatches(textNode.value, target, collapse)) continue;
            const to = copiedText(textNode.value, target);
            edits.push({ node: textNode, to: scriptString(to) });
            changes.push({ id, kind: "text", step: index, from: textNode.value, to });
        }
    }

    let out = html;
    for (const { node, to } of edits.sort((a, b) => b.node.start - a.node.start)) {
        out = out.slice(0, bodyAt + node.start) + to + out.slice(bodyAt + node.end);
    }
    return { html: out, changes, problems };
}

// ── The repository ──────────────────────────────────────────────────────────

async function demosUnder(repoRoot, rel) {
    const out = [];
    let entries;
    try {
        entries = await readdir(resolveInside(repoRoot, rel), { withFileTypes: true });
    } catch {
        return out;
    }
    for (const entry of entries) {
        const child = `${rel}/${entry.name}`;
        if (entry.isDirectory()) {
            if (entry.name !== "_meta") out.push(...(await demosUnder(repoRoot, child)));
        } else if (entry.isFile() && DEMO_FILE.test(entry.name)) out.push(child);
    }
    return out.sort();
}

/** Every scenario page under `domain/`, keyed by path, with its current signature. */
export async function scenarioPages(repoRoot, scenarioFolder = SCENARIO_FOLDER) {
    const pages = new Map();
    for (const page of await listScenarioPages(repoRoot)) {
        pages.set(page.path, { path: page.path, stem: page.stem, page, signature: await pageSignature(page, repoRoot, scenarioFolder) });
    }
    return pages;
}

/**
 * Sync `files` — demo paths, relative to the repository root, or every demo
 * under `domain/` when none is given. With `check`, nothing is written.
 * Returns `[{ path, changes, problems }]`, one per demo read; a path that
 * resolves outside the repository is a problem and is not read.
 */
export async function syncDemos(repoRoot, { files = [], scenarioFolder = SCENARIO_FOLDER, check = false } = {}) {
    const demos = files.length ? files : await demosUnder(repoRoot, DOMAIN_FOLDER);
    const pages = await scenarioPages(repoRoot, scenarioFolder);
    const results = [];
    for (const given of demos) {
        let relPath = normalizePath(given);
        let file;
        let html;
        try {
            file = resolveInside(repoRoot, given);
            relPath = normalizePath(path.relative(repoRoot, file));
            html = await readFile(file, "utf8");
        } catch (error) {
            const why = error.code === "ENOENT" ? "does not exist" : error.code === OUTSIDE_ROOT ? "resolves outside the repository" : error.message;
            results.push({ path: relPath, changes: [], problems: [`${relPath}: ${why}`] });
            continue;
        }
        let synced;
        try {
            synced = syncDemo(html, relPath, pages);
        } catch (error) {
            synced = { html, changes: [], problems: [`${relPath}: its \`${DEMO_MODEL_ID}\` is not valid JSON — ${error.message}`] };
        }
        if (!check && synced.html !== html) await writeFile(file, synced.html, "utf8");
        results.push({ path: relPath, changes: synced.changes, problems: synced.problems });
    }
    return results;
}

async function main(argv) {
    const options = { root: process.cwd(), scenarioFolder: SCENARIO_FOLDER, check: false, files: [] };
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === "--root" && argv[i + 1]) options.root = argv[++i];
        else if (arg === "--scenario-folder" && argv[i + 1]) options.scenarioFolder = argv[++i];
        else if (arg === "--check") options.check = true;
        else if (arg.startsWith("--")) options.error = `unknown or incomplete argument: ${arg}`;
        else options.files.push(arg);
    }
    if (options.error) {
        console.error(`${options.error}\nusage: demo-sync.mjs [<demo.html>…] [--root <dir>] [--scenario-folder <dir>] [--check]`);
        process.exitCode = 2;
        return;
    }
    const repoRoot = path.resolve(options.root);
    const results = await syncDemos(repoRoot, { files: options.files, scenarioFolder: options.scenarioFolder, check: options.check });

    let changed = 0;
    let failed = false;
    for (const { path: relPath, changes, problems } of results) {
        for (const problem of problems) console.error(problem);
        failed ||= problems.length > 0;
        for (const change of changes) {
            changed++;
            const what = change.kind === "signature" ? "signature" : `step ${change.step} text`;
            const verb = options.check ? (change.kind === "signature" ? "stale" : "drifted") : "copied";
            console.log(`${relPath} walkthrough "${change.id}" ${what} ${verb}: ${JSON.stringify(change.from)} → ${JSON.stringify(change.to)}`);
        }
    }
    if (!changed && !failed) console.log(`${results.length} demo(s): every linked walkthrough is current.`);
    process.exitCode = failed || (options.check && changed) ? 1 : 0;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main(process.argv.slice(2));
