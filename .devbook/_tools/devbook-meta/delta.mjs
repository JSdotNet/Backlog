#!/usr/bin/env node
// delta.mjs — checks a change's deltas against the chapters they target, and
// merges them.
//
//   node .devbook/_tools/devbook-meta/delta.mjs --check <change>   # resolve every delta, write nothing
//   node .devbook/_tools/devbook-meta/delta.mjs --apply <change>   # check, merge, move to archive/
//   ... --root ../other-repo    ... --date 2026-09-28   (the archive date; today by default)
//   ... --no-move   merge and leave the folder where it is, for a caller whose
//                   own tool moves it — OpenSpec's `openspec archive` does
//
// `<change>` is the change's name or its folder, `openspec/changes/<name>`.
//
// A delta sits under the change's `devbook-delta/` at the path of the devbook
// file it changes, opens with a `meta` block carrying `change` and `delta`, and
// names chapters by heading. Under each chapter it names, `ADDED`, `MODIFIED`,
// and `REMOVED` sections hold entries one heading level down; the merge lands
// them one level up, heading by heading, and stamps `change` on every chapter
// block it touched. The shape is `devbook-changes.md`'s; this file only
// implements it.
//
// `--check` does the whole merge in memory and runs the chapter lint over each
// result, so a delta that would leave its target invalid is reported before
// anything is written. The graph build imports `checkDelta` and runs the same
// check on every delta it indexes.
//
// A `*.demo.html` directly in a `domain/<context>/` folder is the one other
// file a delta folder holds: no header, no sections, checked with demo.mjs's
// rules and landed by replacing its target whole. The graph does not index it
// as a delta, but the change's fingerprint covers it. Any other file is an error.
//
// Scenario pages carry rules no single file can check — a stem unique across
// contexts, setup fields that resolve to chapters, `Proved by:` pointers that
// name a part. Once every delta resolves, the check lays all of the change's
// merges over the corpus in memory and runs scenario.mjs's pass over the
// result, so a pointer to a page the same change adds resolves, and a typo or
// a colliding stem is reported against the delta that causes it, before either
// gate rather than at archive.

import { readFile, writeFile, readdir, mkdir, rename, rm, stat } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
    CHANGES_ROOT,
    CHANGES_ARCHIVE,
    DELTA_FOLDER,
    DELTA_SECTIONS,
    DECISION_STATUSES,
    DEVBOOK_FOLDER_NAMES,
    DEVBOOK_PREFIX,
    changeHash,
    changePathParts,
    chapterFingerprint,
    isDemoPath,
    parseAnnotations,
    parseDocument,
    resolveAnnotation,
    deltaHeaderIssues,
    folderKindForPath,
    parseDeltaHeader,
    resolveType,
    slugify,
    validateDocument,
} from "./metadata.mjs";
import { loadStatusLadder } from "./statuses.mjs";
import { demoFileIssues, demoReader } from "./demo.mjs";
import { regionProblem } from "./demo-template.mjs";
import { scenarioProblems } from "./scenario.mjs";

const FENCE = /^\s*(`{3,}|~{3,})/;
const HEADING = /^(#{1,6})\s+(.*?)\s*#*\s*$/;

/** Headings outside fenced code, with their line index and subtree end. */
function headingsOf(lines, from = 0, to = lines.length) {
    const found = [];
    let fence = null;
    for (let i = from; i < to; i++) {
        const open = FENCE.exec(lines[i]);
        if (fence) {
            if (open && open[1][0] === fence[0] && open[1].length >= fence.length) fence = null;
            continue;
        }
        if (open) {
            fence = open[1];
            continue;
        }
        const match = HEADING.exec(lines[i]);
        if (match) found.push({ index: i, level: match[1].length, text: match[2], slug: slugify(match[2]) });
    }
    for (const [n, heading] of found.entries()) {
        const next = found.slice(n + 1).find((h) => h.level <= heading.level);
        heading.end = next ? next.index : to;
    }
    return found;
}

/** The `meta` fence right under a heading, skipping blank lines: its open and close line. */
function metaFenceAfter(lines, headingIndex, end) {
    let j = headingIndex + 1;
    while (j < end && lines[j].trim() === "") j++;
    if (j >= end || !/^```meta\s*$/.test(lines[j].trim())) return null;
    let k = j + 1;
    while (k < end && lines[k].trim() !== "```") k++;
    return k < end ? { open: j, close: k } : null;
}

/**
 * Set fields in the `meta` block under a heading, keeping every other line as
 * it is. A heading with no block gets one. `fields` maps a key to the raw text
 * after its colon.
 */
function setMetaFields(lines, headingIndex, end, fields) {
    const fence = metaFenceAfter(lines, headingIndex, end);
    if (!fence) {
        const block = ["", "```meta", ...Object.entries(fields).map(([k, v]) => `${k}: ${v}`), "```"];
        lines.splice(headingIndex + 1, 0, ...block);
        return block.length;
    }
    let added = 0;
    for (const [key, value] of Object.entries(fields)) {
        let replaced = false;
        for (let i = fence.open + 1; i < fence.close + added; i++) {
            const idx = lines[i].indexOf(":");
            if (idx !== -1 && lines[i].slice(0, idx).trim() === key) {
                lines[i] = `${key}: ${value}`;
                replaced = true;
                break;
            }
        }
        if (!replaced) {
            lines.splice(fence.close + added, 0, `${key}: ${value}`);
            added++;
        }
    }
    return added;
}

/** Raise every heading in a slice one level, leaving fenced code alone. */
function raise(lines) {
    const out = [...lines];
    for (const heading of headingsOf(out)) out[heading.index] = out[heading.index].replace(/^#/, "");
    return out;
}

function trimBlank(lines) {
    let a = 0;
    let b = lines.length;
    while (a < b && lines[a].trim() === "") a++;
    while (b > a && lines[b - 1].trim() === "") b--;
    return lines.slice(a, b);
}

/** Stamp `change` on every chapter block inside a slice of lines. */
function stampAll(lines, change) {
    const out = [...lines];
    for (const heading of headingsOf(out).reverse()) {
        if (metaFenceAfter(out, heading.index, heading.end)) setMetaFields(out, heading.index, heading.end, { change });
    }
    return out;
}

function isBlankRange(lines, from, to) {
    return lines.slice(from, to).every((line) => line.trim() === "");
}

/**
 * Parse a delta's body into the chapters it targets and, under each, its
 * sections and their entries. Structural problems come back as issues.
 */
export function parseDelta(markdown) {
    const lines = markdown.split(/\r?\n/);
    const header = parseDeltaHeader(markdown);
    const issues = [];
    const from = header?.end ?? 0;
    const headings = headingsOf(lines, from);
    if (!headings.length) return { lines, header, level: null, targets: [], issues };

    const level = Math.min(...headings.map((h) => h.level));
    if (!isBlankRange(lines, from, headings[0].index)) {
        issues.push({ severity: "error", message: `has text between its opening \`meta\` block and the first chapter it names — a delta holds chapters, nothing loose.` });
    }
    const targets = [];
    for (const target of headings.filter((h) => h.level === level)) {
        const children = headings.filter((h) => h.index > target.index && h.index < target.end);
        const sections = [];
        const firstChild = children[0]?.index ?? target.end;
        if (children[0] && children[0].level !== level + 1) {
            issues.push({ severity: "error", message: `line ${children[0].index + 1}: "${children[0].text}" sits under "${target.text}" outside any ADDED, MODIFIED, or REMOVED section.` });
        }
        for (const section of children.filter((h) => h.level === level + 1)) {
            if (!DELTA_SECTIONS.includes(section.text)) {
                issues.push({ severity: "error", message: `line ${section.index + 1}: "${section.text}" under "${target.text}" is not one of ${DELTA_SECTIONS.join(", ")} — a delta names what it does to a chapter one level below it.` });
                continue;
            }
            if (sections.some((s) => s.name === section.text)) {
                issues.push({ severity: "error", message: `line ${section.index + 1}: "${target.text}" has two ${section.text} sections — merge them.` });
                continue;
            }
            const entries = children.filter((h) => h.level === level + 2 && h.index > section.index && h.index < section.end);
            const lead = [section.index + 1, entries[0]?.index ?? section.end];
            let meta = null;
            const fence = metaFenceAfter(lines, section.index, lead[1]);
            if (fence && section.text === "MODIFIED") {
                meta = lines.slice(fence.open + 1, fence.close);
                if (!isBlankRange(lines, fence.close + 1, lead[1])) {
                    issues.push({ severity: "error", message: `line ${section.index + 1}: MODIFIED under "${target.text}" has text beside its fields — a paragraph is not addressable; replace the section it sits in.` });
                }
            } else if (!isBlankRange(lines, ...lead)) {
                issues.push({ severity: "error", message: `line ${section.index + 1}: ${section.text} under "${target.text}" has text before its first entry — ${section.text === "MODIFIED" ? "only a `meta` block of fields to set sits there" : "every entry is a heading"}.` });
            }
            if (!entries.length && !meta) {
                issues.push({ severity: "error", message: `line ${section.index + 1}: ${section.text} under "${target.text}" names nothing.` });
            }
            sections.push({ name: section.text, line: section.index + 1, meta, entries: entries.map((e) => ({ ...e, lines: lines.slice(e.index, e.end) })) });
        }
        targets.push({
            ...target,
            line: target.index + 1,
            intro: lines.slice(target.index, firstChild),
            sections,
        });
    }
    return { lines, header, level, targets, issues };
}

/**
 * Merge one parsed delta into its target's text. Returns the merged text —
 * `null` when the delta removes the whole file — and the problems that stop
 * it. `original` is `null` when the target file does not exist.
 */
export function mergeDelta(parsed, original, change, { target = null, demoText = null } = {}) {
    const issues = [];
    const kind = parsed.header?.meta?.delta;
    const { level, targets } = parsed;
    if (!targets.length) return { merged: original, issues, placeholder: true };

    const eol = original?.includes("\r\n") ? "\r\n" : "\n";
    let lines = original === null ? [] : original.split(/\r?\n/);
    if (original === null && !(kind === "added" && level === 1)) {
        issues.push({ severity: "error", message: `targets a file that does not exist — only \`delta: added\` with a \`#\` title creates one.` });
        return { merged: null, issues };
    }

    for (const target of targets) {
        const where = `"${target.text}" (line ${target.line})`;
        const matches = headingsOf(lines).filter((h) => h.level === level && h.slug === target.slug);
        const forbidden = (names) => target.sections.filter((s) => names.includes(s.name));

        if (kind === "added") {
            if (matches.length) {
                issues.push({ severity: "error", message: `adds ${where}, which the target already has at line ${matches[0].index + 1} — use \`delta: modified\`.` });
                continue;
            }
            if (level > 2 || (level === 1 && original !== null)) {
                issues.push({ severity: "error", message: `adds ${where} at level ${level}, where it has no parent to land under — add it as an ADDED entry of the chapter above it, in a \`delta: modified\`.` });
                continue;
            }
            for (const s of forbidden(["MODIFIED", "REMOVED"])) {
                issues.push({ severity: "error", message: `adds ${where} with a ${s.name} section — a new chapter has nothing to modify or remove.` });
            }
            const added = forbidden(["ADDED"]).flatMap((s) => s.entries.flatMap((e) => ["", ...trimBlank(raise(e.lines))]));
            const chapter = stampAll([...trimBlank(target.intro), ...added], change);
            lines = [...trimBlank(lines), ...(lines.some((l) => l.trim()) ? [""] : []), ...chapter];
            continue;
        }

        if (matches.length !== 1) {
            issues.push({
                severity: "error",
                message: matches.length
                    ? `names ${where}, which the target has ${matches.length} times at level ${level} — the anchor is ambiguous.`
                    : `names ${where}, which the target has no level-${level} heading for.`,
            });
            continue;
        }
        const [hit] = matches;
        if (target.intro.slice(1).some((line) => line.trim() !== "")) {
            issues.push({ severity: "error", message: `${kind === "removed" ? "removes" : "modifies"} ${where} with text directly under the heading — ${kind === "removed" ? "a removed chapter holds nothing" : "set fields under MODIFIED and replace sections by heading"}.` });
            continue;
        }

        if (kind === "removed") {
            for (const s of target.sections) {
                issues.push({ severity: "error", message: `removes ${where} whole, so its ${s.name} section has nothing to act on.` });
            }
            if (level === 1) return { merged: null, issues };
            lines.splice(hit.index, hit.end - hit.index);
            continue;
        }

        // `modified`: work on the chapter's own lines, then splice them back.
        let chapter = lines.slice(hit.index, hit.end);
        const direct = () => headingsOf(chapter).filter((h) => h.level === level + 1);
        const section = (name) => target.sections.find((s) => s.name === name);

        for (const entry of section("REMOVED")?.entries ?? []) {
            const found = direct().filter((h) => h.slug === entry.slug);
            if (found.length !== 1) {
                issues.push({ severity: "error", message: `removes "${entry.text}" (line ${entry.index + 1}) from ${where}, which has ${found.length ? "it more than once" : "no such section"}.` });
                continue;
            }
            chapter.splice(found[0].index, found[0].end - found[0].index);
        }
        for (const entry of section("MODIFIED")?.entries ?? []) {
            const found = direct().filter((h) => h.slug === entry.slug);
            if (found.length !== 1) {
                issues.push({ severity: "error", message: `replaces "${entry.text}" (line ${entry.index + 1}) in ${where}, which has ${found.length ? "it more than once" : "no such section — use ADDED"}.` });
                continue;
            }
            const replacement = stampAll(trimBlank(raise(entry.lines)), change);
            const tail = found[0].end < chapter.length ? [""] : [];
            chapter.splice(found[0].index, found[0].end - found[0].index, ...replacement, ...tail);
        }
        for (const entry of section("ADDED")?.entries ?? []) {
            if (direct().some((h) => h.slug === entry.slug)) {
                issues.push({ severity: "error", message: `adds "${entry.text}" (line ${entry.index + 1}) to ${where}, which already has it — use MODIFIED.` });
                continue;
            }
            chapter = [...trimBlank(chapter), "", ...stampAll(trimBlank(raise(entry.lines)), change)];
        }
        const fields = {};
        for (const line of section("MODIFIED")?.meta ?? []) {
            const idx = line.indexOf(":");
            if (idx !== -1) fields[line.slice(0, idx).trim()] = line.slice(idx + 1).trim();
        }
        fields.change = change;
        // A structural heading stays structural unless the delta sets a field on it.
        if (Object.keys(fields).length > 1 || metaFenceAfter(chapter, 0, chapter.length)) {
            setMetaFields(chapter, 0, chapter.length, fields);
        }
        const rest = trimBlank(lines.slice(hit.end));
        lines = [...lines.slice(0, hit.index), ...trimBlank(chapter), ...(rest.length ? ["", ...rest] : [])];
    }

    lines = liftLapsedDecisions(original, trimBlank(lines), change, issues, target, demoText);
    return { merged: `${lines.join(eol)}${eol}`, issues };
}

// The six fields a decision rung carries, which come out with it.
const DECISION_RECORD = ["approved-by", "approved-at", "approved-hash", "accepted-by", "accepted-at", "accepted-hash"];

/**
 * Take the decision rung off every chapter this merge changed under it. A
 * chapter's own `approved` or `accepted` is of the content that was read; a
 * merge that changes that content lapses it, exactly as a hand edit would, and
 * the change's own decision on `proposal.md` is what agreed the new content —
 * so the merge writes no rung onto a chapter, and leaves none standing that is
 * no longer true. Changed means the recorded fingerprint no longer matches or,
 * where none was recorded, the merge stamped the chapter. A rung the delta set
 * itself is left for the lint to judge.
 */
function liftLapsedDecisions(original, lines, change, issues, target = null, demoText = null) {
    if (original === null) return lines;
    const rung = (c) => `${c.level}:${c.slug}:${c.meta?.status}`;
    const before = new Set(
        parseDocument(original).chapters.filter((c) => DECISION_STATUSES.includes(c.meta?.status)).map(rung)
    );
    if (!before.size) return lines;
    const text = lines.join("\n");
    const lapsed = parseDocument(text).chapters.filter((c) => {
        if (!before.has(rung(c))) return false;
        const recorded = c.meta["accepted-hash"] ?? c.meta["approved-hash"];
        return recorded != null
            ? String(recorded).trim() !== chapterFingerprint(target ?? "", text, c.line, target ? demoText : null)
            : c.meta.change === change;
    });
    return stripDecisions(lines, lapsed, issues);
}

/** Remove the status and decision record from each chapter in `lapsed`, reporting each. */
function stripDecisions(lines, lapsed, issues, reason = "the merge changes the content that was decided") {
    const out = [...lines];
    for (const chapter of [...lapsed].reverse()) {
        const fence = metaFenceAfter(out, chapter.line - 1, out.length);
        if (!fence) continue;
        for (let i = fence.close - 1; i > fence.open; i--) {
            const key = out[i].slice(0, out[i].indexOf(":")).trim();
            if (key === "status" || DECISION_RECORD.includes(key)) out.splice(i, 1);
        }
        issues.push({
            severity: "info",
            message: `lifts \`${chapter.meta.status}\` from "${chapter.text}": ${reason}, and the change's own decision is on its proposal.md.`,
        });
    }
    return out;
}

/** Strip line numbers so a lint result can be compared before and after a merge. */
const unlined = (message) => message.replace(/\(line \d+\)/g, "").replace(/line \d+/g, "");

/**
 * Check one delta: its header, its shape, that every chapter it names
 * resolves in its target, and that the merged target still passes the lint.
 * The graph build calls this for every delta it indexes.
 */
export async function checkDelta(repoRoot, relPath, markdown, { ladder = null, demoText = null } = {}) {
    const where = changePathParts(relPath);
    const issues = [...deltaHeaderIssues(relPath, markdown)];
    const parsed = parseDelta(markdown);
    issues.push(...parsed.issues);
    if (issues.some((i) => i.severity === "error") || !where) return { issues, target: where?.target ?? null };

    let original = null;
    try {
        original = await readFile(path.join(repoRoot, where.target), "utf8");
    } catch {
        original = null;
    }
    const result = mergeDelta(parsed, original, where.name, { target: where.target, demoText });
    issues.push(...result.issues);
    if (result.placeholder) {
        issues.push({ severity: "info", message: `names no chapter, so it merges nothing — a placeholder.` });
    } else if (!issues.some((i) => i.severity === "error") && result.merged !== null) {
        const before = new Set(original === null ? [] : validateDocument(where.target, original, { ladder, demoText }).map((i) => unlined(i.message)));
        for (const issue of validateDocument(where.target, result.merged, { ladder, demoText })) {
            if (issue.severity !== "error" || before.has(unlined(issue.message))) continue;
            issues.push({ severity: "error", message: `would leave ${where.target} invalid: ${issue.message}` });
        }
    }
    return { issues, target: where.target, merged: result.merged, placeholder: !!result.placeholder, removesFile: result.merged === null && !result.issues.length };
}

// Where a demo lands: `demo.html` or `<page>.demo.html` directly in a context folder.
const DEMO_LANDING = /^\.devbook\/domain\/[^/]+\/(?:[^/]+\.)?demo\.html$/;

/**
 * Check one demo delta: where it lands and the demo rules over the file. Its
 * merge is the file itself, which replaces the target whole.
 */
export function checkDemoDelta(relPath, html) {
    const { target } = changePathParts(relPath);
    const issues = [];
    if (!DEMO_LANDING.test(target)) {
        issues.push({ severity: "error", message: `lands as ${target}, which is not where a demo lives — \`demo.html\` or \`<page>.demo.html\` directly in a \`domain/<context>/\` folder.` });
    }
    const region = regionProblem(relPath, html);
    if (region) issues.push({ severity: "error", message: region });
    issues.push(...demoFileIssues(relPath, html));
    return { issues, target, merged: html, demo: true, placeholder: false, removesFile: false };
}

/** Every file a change folder indexes: its proposal and each delta. */
export async function changeFiles(repoRoot) {
    const found = [];
    let names;
    try {
        names = await readdir(path.join(repoRoot, CHANGES_ROOT), { withFileTypes: true });
    } catch {
        return found;
    }
    for (const entry of names) {
        if (!entry.isDirectory() || entry.name === "archive") continue;
        const base = `${CHANGES_ROOT}/${entry.name}`;
        if (await exists(path.join(repoRoot, base, "proposal.md"))) found.push(`${base}/proposal.md`);
        found.push(...(await markdownUnder(repoRoot, `${base}/${DELTA_FOLDER}`)));
    }
    return found.sort();
}

async function markdownUnder(repoRoot, rel, wanted = (name) => name.endsWith(".md")) {
    const out = [];
    let entries;
    try {
        entries = await readdir(path.join(repoRoot, rel), { withFileTypes: true });
    } catch {
        return out;
    }
    for (const entry of entries) {
        const child = `${rel}/${entry.name}`;
        if (entry.isDirectory()) out.push(...(await markdownUnder(repoRoot, child, wanted)));
        else if (entry.isFile() && wanted(entry.name)) out.push(child);
    }
    return out;
}

async function exists(absolute) {
    try {
        await stat(absolute);
        return true;
    } catch {
        return false;
    }
}

/** Read a file by its repository-relative path, refusing one that resolves outside the repository. */
async function readInRepo(repoRoot, relPath) {
    const root = path.resolve(repoRoot);
    const absolute = path.resolve(root, relPath);
    if (path.relative(root, absolute).startsWith("..") || path.isAbsolute(path.relative(root, absolute))) {
        throw new Error(`${relPath} resolves outside the repository.`);
    }
    return readFile(absolute, "utf8");
}

/** Check every delta of one change. */
export async function checkChange(repoRoot, name) {
    const { ladder, issues: ladderIssues } = await loadStatusLadder(repoRoot);
    const base = `${CHANGES_ROOT}/${name}`;
    const report = { name, problems: [...ladderIssues], deltas: [] };
    if (!(await exists(path.join(repoRoot, base)))) {
        report.problems.push({ severity: "error", message: `${base}/ does not exist.` });
        return report;
    }
    if (!(await exists(path.join(repoRoot, base, "proposal.md")))) {
        report.problems.push({ severity: "error", message: `${base}/ has no proposal.md.` });
    }
    const deltas = await markdownUnder(repoRoot, `${base}/${DELTA_FOLDER}`);
    const demos = await markdownUnder(repoRoot, `${base}/${DELTA_FOLDER}`, isDemoPath);
    const other = await markdownUnder(repoRoot, `${base}/${DELTA_FOLDER}`, (name) => !name.endsWith(".md") && !isDemoPath(name));
    if (!deltas.length) report.problems.push({ severity: "error", message: `${base}/ has no delta under ${DELTA_FOLDER}/ — a change with nothing to merge carries a placeholder.` });
    for (const relPath of other.sort()) {
        report.problems.push({ severity: "error", message: `${relPath} is neither a Markdown delta nor a \`*.demo.html\` — a demo is the one other file ${DELTA_FOLDER}/ holds.` });
    }
    // Chapter fingerprints read each demo as the change would leave it: the
    // proposed copy where the change carries one, the repository's otherwise.
    const proposedDemos = new Map();
    for (const relPath of demos) proposedDemos.set(changePathParts(relPath).target, await readInRepo(repoRoot, relPath));
    const onDisk = demoReader(repoRoot);
    const demoText = (relPath) => (proposedDemos.has(relPath) ? proposedDemos.get(relPath) : onDisk(relPath));
    for (const relPath of deltas.sort()) {
        const markdown = await readInRepo(repoRoot, relPath);
        report.deltas.push({ path: relPath, markdown, ...(await checkDelta(repoRoot, relPath, markdown, { ladder, demoText })) });
    }
    for (const relPath of demos.sort()) {
        const markdown = proposedDemos.get(changePathParts(relPath).target);
        report.deltas.push({ path: relPath, markdown, ...checkDemoDelta(relPath, markdown) });
    }
    if (proposedDemos.size) report.deltas.push(...(await demoLapses(repoRoot, report.deltas, onDisk, demoText)));
    // The corpus-wide scenario rules run once every delta merges: a delta that
    // does not would leave the projected corpus wrong in ways it did not cause.
    if (!report.deltas.some((d) => d.issues.some((i) => i.severity === "error"))) {
        for (const { delta, issue } of await changeScenarioIssues(repoRoot, report.deltas)) {
            (delta ? delta.issues : report.problems).push(issue);
        }
    }
    return report;
}

/**
 * Every devbook document as the scenario pass reads it — `{ relPath, raw,
 * chapters, fileMeta }` — and the chapter kinds it resolves setup fields
 * against, with `overrides` laid over the disk: a path to its text, or to
 * `null` for a file the change removes. A node is what the graph build would
 * index: one per file, one per heading with a `meta` block, the first heading
 * keeping an anchor.
 */
async function scenarioCorpus(repoRoot, overrides = new Map()) {
    const paths = new Set(overrides.keys());
    for (const name of DEVBOOK_FOLDER_NAMES) {
        for (const relPath of await markdownUnder(repoRoot, `${DEVBOOK_PREFIX}${name}`)) paths.add(relPath);
    }
    const docs = [];
    const nodes = new Map();
    for (const relPath of [...paths].sort()) {
        const raw = overrides.has(relPath) ? overrides.get(relPath) : await readInRepo(repoRoot, relPath);
        if (raw === null) continue;
        const folder = folderKindForPath(relPath);
        const { chapters } = parseDocument(raw);
        const fileMeta = chapters.find((c) => c.level === 1)?.meta ?? null;
        docs.push({ relPath, raw, chapters, fileMeta });
        nodes.set(relPath, { id: relPath, kind: fileMeta ? resolveType(folder, fileMeta) ?? undefined : undefined });
        const anchors = new Set();
        for (const chapter of chapters) {
            if (chapter.level === 1 || anchors.has(chapter.slug)) continue;
            anchors.add(chapter.slug);
            if (chapter.meta) nodes.set(`${relPath}#${chapter.slug}`, { id: `${relPath}#${chapter.slug}`, kind: resolveType(folder, chapter.meta) ?? undefined });
        }
    }
    return { docs, nodes };
}

// A problem with its line numbers taken out, so one the corpus already has is
// recognized after a merge moved it.
const unlocated = (problem) => `${problem.severity}:${unlined(problem.message.replace(/(\.md):\d+/g, "$1"))}`;

/**
 * The corpus-wide scenario problems the change introduces: scenario.mjs's pass
 * over the corpus as the change would leave it, less every problem the corpus
 * has today. Each comes back with the delta that causes it — the one whose
 * target the problem sits in, else the one whose target it names — or none,
 * when it is the change's as a whole.
 */
export async function changeScenarioIssues(repoRoot, deltas) {
    const merging = deltas.filter((d) => !d.placeholder && !d.demo && d.target && d.merged !== undefined);
    if (!merging.length) return [];
    const run = async (overrides) => {
        const { docs, nodes } = await scenarioCorpus(repoRoot, overrides);
        return (await scenarioProblems(repoRoot, docs, nodes)).problems;
    };
    const known = new Set((await run(new Map())).map(unlocated));
    const found = [];
    for (const problem of await run(new Map(merging.map((d) => [d.target, d.merged])))) {
        if (known.has(unlocated(problem))) continue;
        const delta = merging.find((d) => d.target === problem.path) ?? merging.find((d) => problem.message.includes(d.target)) ?? null;
        found.push({ delta, issue: { severity: problem.severity, message: `would leave the corpus, with the change merged, where ${problem.message}` } });
    }
    return found;
}

/**
 * A replaced demo lapses every decision whose fingerprint folds it in, in
 * files no Markdown delta of the change merges into. Each such file comes back
 * as a delta entry carrying the file with those decisions lifted, so
 * `applyChange` writes it beside the demo.
 */
async function demoLapses(repoRoot, deltas, onDisk, demoText) {
    const merged = new Set(deltas.filter((d) => !d.placeholder && !d.demo).map((d) => d.target));
    const lapses = [];
    for (const relPath of await markdownUnder(repoRoot, `${DEVBOOK_PREFIX}domain`)) {
        if (merged.has(relPath)) continue;
        const text = await readInRepo(repoRoot, relPath);
        const lapsed = parseDocument(text).chapters.filter(
            (c) => DECISION_STATUSES.includes(c.meta?.status) && chapterFingerprint(relPath, text, c.line, onDisk) !== chapterFingerprint(relPath, text, c.line, demoText)
        );
        if (!lapsed.length) continue;
        const issues = [];
        const eol = text.includes("\r\n") ? "\r\n" : "\n";
        const lines = stripDecisions(text.split(/\r?\n/), lapsed, issues, "the change replaces a demo it folds in");
        lapses.push({ path: relPath, target: relPath, markdown: text, merged: lines.join(eol), issues, placeholder: false, removesFile: false });
    }
    return lapses;
}

/** A change's proposal and deltas, read from disk; `null` when it has no proposal. */
export async function readChange(repoRoot, name) {
    const base = `${CHANGES_ROOT}/${name}`;
    let proposal;
    try {
        proposal = await readFile(path.join(repoRoot, base, "proposal.md"), "utf8");
    } catch {
        return null;
    }
    // A demo delta is HTML and merges whole, but the change is decided as one,
    // so it is fingerprinted with the rest: editing a proposed demo lifts the
    // change's rungs as editing a Markdown delta does.
    const deltas = [];
    const files = [...(await markdownUnder(repoRoot, `${base}/${DELTA_FOLDER}`)), ...(await markdownUnder(repoRoot, `${base}/${DELTA_FOLDER}`, isDemoPath))];
    for (const relPath of files.sort()) {
        deltas.push({ path: relPath, target: changePathParts(relPath).target, markdown: await readFile(path.join(repoRoot, relPath), "utf8") });
    }
    return { name, proposal, deltas };
}

/** The fingerprint a change's rungs record: its proposal and every delta, as one. */
export async function changeFingerprint(repoRoot, name) {
    const change = await readChange(repoRoot, name);
    return change ? changeHash(change.proposal, change.deltas) : null;
}

const proposalMeta = (change) => parseDocument(change.proposal).chapters.find((c) => c.level === 1)?.meta ?? {};

/**
 * What stands against a decision on the change as a whole, beyond what the
 * proposal's own block lint says. The change is one review, so an open
 * question anywhere in it — the proposal or any delta — means it is not
 * agreed. Reported only while the proposal states a rung.
 */
export function changeDecisionIssues(change) {
    const { status } = proposalMeta(change);
    if (!DECISION_STATUSES.includes(status)) return [];
    const issues = [];
    for (const file of [{ path: `${CHANGES_ROOT}/${change.name}/proposal.md`, markdown: change.proposal }, ...change.deltas]) {
        for (const note of parseAnnotations(file.markdown)) {
            const fields = resolveAnnotation(note.fields);
            if (fields.status !== "open" || fields.kind !== "question") continue;
            issues.push({
                severity: "error",
                message: `states \`status: ${status}\` while ${file.path} carries an open \`kind: question\` annotation (line ${note.line}) — the change is decided as one, so an open question anywhere in it means it is not agreed.`,
            });
        }
    }
    return issues;
}

// ── The gate ────────────────────────────────────────────────────────────────
// Run after every delta has resolved and before anything is written. A change
// merges only once both of its gates have passed, on its proposal: approved,
// then accepted, each signed and dated, each with the fingerprint of the
// change as it stands — so nothing was edited after either decision — and no
// open question anywhere in it. The collaboration plugin's approve and accept
// skills write the records; this only reads them.
async function gateCheck(repoRoot, report) {
    const change = await readChange(repoRoot, report.name);
    if (!change) return [];
    const meta = proposalMeta(change);
    const refuse = (message) => ({ severity: "error", message: `${CHANGES_ROOT}/${report.name}/proposal.md ${message}` });
    if (meta.status !== "accepted") {
        return [refuse(`is at \`status: ${meta.status ?? "(none)"}\` — a change merges once it is approved and then accepted. Run both gates on the change first.`)];
    }
    const problems = [];
    for (const field of ["approved-by", "approved-at", "accepted-by", "accepted-at"]) {
        if (meta[field] == null) problems.push(refuse(`states \`status: accepted\` without \`${field}\` — an unsigned decision is not a gate passed.`));
    }
    const current = changeHash(change.proposal, change.deltas);
    for (const field of ["approved-hash", "accepted-hash"]) {
        if (meta[field] == null) {
            problems.push(refuse(`carries no \`${field}\` — a change's gates record the fingerprint of what they decided, so an edit after either is caught.`));
        } else if (String(meta[field]).trim() !== current) {
            problems.push(refuse(`records \`${field}\` ${meta[field]}, and the change now fingerprints as ${current} — it changed after the decision. Decide it again.`));
        }
    }
    problems.push(...changeDecisionIssues(change).map((issue) => refuse(issue.message)));
    return problems;
}

/**
 * Check, merge, and archive one change. Returns the report and what was
 * written. `move: false` merges and leaves the folder open for the caller to
 * move; the archive path is still checked, so the caller's move cannot collide.
 */
export async function applyChange(repoRoot, name, { date = new Date().toISOString().slice(0, 10), move = true } = {}) {
    const report = await checkChange(repoRoot, name);
    const errors = () => [...report.problems, ...report.deltas.flatMap((d) => d.issues)].filter((i) => i.severity === "error");
    if (errors().length) return { report, applied: false };

    report.problems.push(...(await gateCheck(repoRoot, report)));
    if (errors().length) return { report, applied: false };

    const archived = `${CHANGES_ARCHIVE}/${date}-${name}`;
    if (await exists(path.join(repoRoot, archived))) {
        report.problems.push({ severity: "error", message: `${archived}/ already exists.` });
        return { report, applied: false };
    }

    const written = [];
    for (const delta of report.deltas) {
        if (delta.placeholder) continue;
        const target = path.join(repoRoot, delta.target);
        if (delta.merged === null) {
            await rm(target);
            written.push(`removed ${delta.target}`);
        } else {
            await mkdir(path.dirname(target), { recursive: true });
            await writeFile(target, delta.merged, "utf8");
            written.push(`${delta.demo ? "replaced" : "merged  "} ${delta.target}`);
        }
    }
    if (!move) return { report, applied: true, written };
    await mkdir(path.join(repoRoot, CHANGES_ARCHIVE), { recursive: true });
    await rename(path.join(repoRoot, CHANGES_ROOT, name), path.join(repoRoot, archived));
    written.push(`moved   ${CHANGES_ROOT}/${name}/ to ${archived}/`);
    return { report, applied: true, written };
}

// ── CLI ────────────────────────────────────────────────────────────────────

function printReport(report) {
    let errors = 0;
    const line = (subject, issue) => {
        if (issue.severity === "error") errors++;
        console.log(`  [${issue.severity}] ${subject ? `${subject} ` : ""}${issue.message}`);
    };
    for (const problem of report.problems) line("", problem);
    for (const delta of report.deltas) {
        console.log(`${delta.path} -> ${delta.target ?? "?"}`);
        for (const issue of delta.issues) line(delta.path, issue);
    }
    return errors;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const args = process.argv.slice(2);
    const value = (flag) => (args.includes(flag) ? args[args.indexOf(flag) + 1] : null);
    const mode = args.includes("--apply") ? "--apply" : args.includes("--check") ? "--check" : null;
    const target = mode ? value(mode) : null;
    if (!target) {
        console.error("usage: delta.mjs --check <change> | --apply <change> [--no-move] [--root <dir>] [--date YYYY-MM-DD]");
        process.exit(2);
    }
    const repoRoot = path.resolve(value("--root") ?? process.cwd());
    const name = target.replace(/\\/g, "/").replace(/\/+$/, "").split("/").pop();
    if (mode === "--check") {
        const errors = printReport(await checkChange(repoRoot, name));
        console.log(errors ? `\n${errors} problem(s) at error severity.` : `\n${name}: every delta resolves.`);
        process.exit(errors ? 1 : 0);
    }
    const result = await applyChange(repoRoot, name, {
        ...(value("--date") ? { date: value("--date") } : {}),
        move: !args.includes("--no-move"),
    });
    const errors = printReport(result.report);
    if (!result.applied) {
        console.error(`\n${name} not merged: ${errors} problem(s) at error severity.`);
        process.exit(1);
    }
    for (const entry of result.written) console.log(entry);
}
