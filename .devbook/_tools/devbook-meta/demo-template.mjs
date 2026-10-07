#!/usr/bin/env node
// demo-template.mjs — every demo's managed region against the repository's
// template at `.devbook/design/demo-template.html`.
//
//   node .devbook/_tools/devbook-meta/demo-template.mjs --check     # report, write nothing; build.mjs --check runs this
//   node .devbook/_tools/devbook-meta/demo-template.mjs --refresh   # re-stamp the template, rewrite every demo's region
//   node .devbook/_tools/devbook-meta/demo-template.mjs --check --root ../other-repo
//
// The region is the text between `<!-- template:begin hash=… -->` and
// `<!-- template:end -->`; its hash is `sha256:` over that text with every
// CRLF read as LF. The marker certifies the version: a region whose text
// hashes to the value its own marker carries is a release of the template
// exactly as it shipped, and one that does not was edited by hand. So no list
// of past versions is kept. A demo's region is
//
//   current      its marker carries the template's hash and its text matches
//   stale        its text matches its marker, but the marker names an earlier
//                template version — a warning; `--refresh` brings it forward
//   hand-edited  its text matches no version, its own marker's included — an
//                error; the edit belongs in the template, and `--refresh`
//                discards it
//
// `--refresh` is the one write into a demo that is not `/prototype`'s: it
// replaces the region and leaves the demo's own parts — `demo-meta`, the
// screens, `demo-model` — byte for byte. It first re-stamps the template's
// own marker when a `flow-spec` edit left it behind.
//
// Dependency-free ESM against node built-ins.

import { createHash } from "node:crypto";
import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { collectDemos } from "./demo.mjs";
import { discoverLayout } from "./graph.mjs";

/** Where the repository keeps its template. */
export const TEMPLATE_PATH = ".devbook/design/demo-template.html";

const BEGIN = /<!--\s*template:begin\b([^>]*?)-->/gi;
const END = /<!--\s*template:end\b[^>]*?-->/gi;

/** The region hash: `sha256:` over the text with every CRLF read as LF. */
export function regionHash(text) {
    return `sha256:${createHash("sha256").update(text.replace(/\r\n/g, "\n"), "utf8").digest("hex")}`;
}

/**
 * The one managed region in `html`: `{ start, end, declared, text, hash }`,
 * `start`–`end` spanning both markers and `declared` the marker's hash or
 * null. `{ problem }` when there is no region, or more than one.
 */
export function readRegion(html) {
    const begins = [...html.matchAll(BEGIN)];
    const ends = [...html.matchAll(END)];
    if (!begins.length) return { problem: "has no `<!-- template:begin hash=… -->` marker, so it is not built on the template" };
    if (begins.length > 1 || ends.length > 1) return { problem: `has ${begins.length} begin and ${ends.length} end markers — the template has one region` };
    const [begin] = begins;
    const end = ends[0];
    if (!end || end.index < begin.index) return { problem: "has no `<!-- template:end -->` marker after its begin marker" };
    const text = html.slice(begin.index + begin[0].length, end.index);
    return {
        start: begin.index,
        end: end.index + end[0].length,
        declared: /\bhash=(\S+)/.exec(begin[1])?.[1] ?? null,
        text,
        hash: regionHash(text),
    };
}

async function readText(repoRoot, relPath) {
    try {
        return await readFile(path.join(repoRoot, relPath), "utf8");
    } catch {
        return null;
    }
}

/** The region `template` would write into `html`, in `html`'s line endings. */
function regionFor(html, template) {
    const block = template.html.slice(template.region.start, template.region.end).replace(/\r\n/g, "\n");
    return html.includes("\r\n") ? block.replace(/\n/g, "\r\n") : block;
}

/** `html` with its begin marker carrying `hash`. */
function stamp(html, region, hash) {
    const marker = html.slice(region.start, region.start + html.slice(region.start).search(/-->/) + 3);
    const stamped = /\bhash=\S+/.test(marker) ? marker.replace(/\bhash=(?:(?!-->)\S)+/, `hash=${hash}`) : marker.replace(/template:begin\b/, `template:begin hash=${hash}`);
    return html.slice(0, region.start) + stamped + html.slice(region.start + marker.length);
}

/**
 * What is wrong with one demo's managed region on its own, as a message, or
 * null: no single region, or a region that does not hash to its own marker.
 */
export function regionProblem(relPath, html) {
    const region = readRegion(html);
    if (region.problem) return `${relPath} ${region.problem}.`;
    if (region.declared === region.hash) return null;
    return (
        `${relPath} has a managed region matching no template version — it hashes to ${region.hash}, not the ${region.declared ?? "missing hash"} its marker carries, so it was edited by hand. ` +
        `Make the change in ${TEMPLATE_PATH}, then run demo-template.mjs --refresh, which rewrites the region and discards the edit.`
    );
}

/**
 * Every demo's region against the template, as `{ severity, path, message }`.
 * Hand edits are found with or without a template; staleness needs one.
 */
export async function templateProblems(repoRoot, folders) {
    const problems = [];
    const demos = await collectDemos(repoRoot, folders);
    const html = await readText(repoRoot, TEMPLATE_PATH);
    let current = null;
    if (html != null) {
        const region = readRegion(html);
        if (region.problem) {
            problems.push({ severity: "error", path: TEMPLATE_PATH, message: `${TEMPLATE_PATH} ${region.problem}.` });
        } else if (region.declared !== region.hash) {
            problems.push({
                severity: "error",
                path: TEMPLATE_PATH,
                message: `${TEMPLATE_PATH} carries ${region.declared ?? "no hash"} on its begin marker, but its region hashes to ${region.hash} — run demo-template.mjs --refresh to re-stamp it and bring every demo forward.`,
            });
        } else current = region.hash;
    } else if (demos.length) {
        problems.push({
            severity: "warning",
            path: TEMPLATE_PATH,
            message: `${TEMPLATE_PATH} does not exist, so ${demos.length} demo(s) are checked for hand edits only — every demo is built on the repository's template.`,
        });
    }

    for (const relPath of demos) {
        const html = (await readText(repoRoot, relPath)) ?? "";
        const region = readRegion(html);
        const problem = regionProblem(relPath, html);
        if (problem) {
            problems.push({ severity: "error", path: relPath, message: problem });
        } else if (current && region.declared !== current) {
            problems.push({
                severity: "warning",
                path: relPath,
                message: `${relPath} is on an earlier template version, ${region.declared}; the template is at ${current} — run demo-template.mjs --refresh.`,
            });
        }
    }
    return problems;
}

/**
 * Re-stamp the template and rewrite every demo's region from it. Returns
 * `{ stamped, written, unchanged, skipped }`, `skipped` as problems for the
 * demos with no region to replace. Throws when there is no usable template.
 */
export async function refreshDemos(repoRoot, folders) {
    let html = await readText(repoRoot, TEMPLATE_PATH);
    if (html == null) throw new Error(`${TEMPLATE_PATH} does not exist — there is no template to refresh from.`);
    let region = readRegion(html);
    if (region.problem) throw new Error(`${TEMPLATE_PATH} ${region.problem}.`);
    let stamped = false;
    if (region.declared !== region.hash) {
        html = stamp(html, region, region.hash);
        await writeFile(path.join(repoRoot, TEMPLATE_PATH), html, "utf8");
        region = readRegion(html);
        stamped = true;
    }
    const template = { html, region };

    const written = [];
    const unchanged = [];
    const skipped = [];
    for (const relPath of await collectDemos(repoRoot, folders)) {
        const demo = (await readText(repoRoot, relPath)) ?? "";
        const own = readRegion(demo);
        if (own.problem) {
            skipped.push({ severity: "error", path: relPath, message: `${relPath} ${own.problem}, so there is no region to refresh.` });
            continue;
        }
        const next = demo.slice(0, own.start) + regionFor(demo, template) + demo.slice(own.end);
        if (next === demo) {
            unchanged.push(relPath);
            continue;
        }
        await writeFile(path.join(repoRoot, relPath), next, "utf8");
        written.push(relPath);
    }
    return { stamped, written, unchanged, skipped };
}

export async function main(argv = process.argv.slice(2)) {
    const rootAt = argv.indexOf("--root");
    const repoRoot = path.resolve(rootAt !== -1 ? argv[rootAt + 1] : process.cwd());
    const refresh = argv.includes("--refresh");
    const { folders } = await discoverLayout(repoRoot);

    if (refresh) {
        const result = await refreshDemos(repoRoot, folders);
        if (result.stamped) console.log(`stamped   ${TEMPLATE_PATH}`);
        for (const relPath of result.written) console.log(`refreshed ${relPath}`);
        console.log(`${result.written.length} demo(s) refreshed, ${result.unchanged.length} already current.`);
        for (const problem of result.skipped) console.log(`  [${problem.severity}] ${problem.message}`);
        return result.skipped.length ? 1 : 0;
    }

    const problems = await templateProblems(repoRoot, folders);
    for (const problem of problems) console.log(`  [${problem.severity}] ${problem.message}`);
    const errors = problems.filter((p) => p.severity === "error").length;
    console.log(`demo template: ${errors} error(s), ${problems.length - errors} warning(s).`);
    return errors ? 1 : 0;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    try {
        process.exitCode = await main();
    } catch (error) {
        console.error(error.message);
        process.exitCode = 2;
    }
}
