#!/usr/bin/env node
// annotations.mjs — the only writer of an annotation fence.
//
//   node .devbook/_tools/devbook-meta/annotations.mjs list    --chapter <path#slug> [--status open]
//   node .devbook/_tools/devbook-meta/annotations.mjs add     --chapter <path#slug> [--after "<quote>"] \
//                                                             --author <who> --body <text> [--kind question]
//   node .devbook/_tools/devbook-meta/annotations.mjs reply   --chapter <path#slug> --index <n> --author <who> --body <text>
//   node .devbook/_tools/devbook-meta/annotations.mjs resolve --chapter <path#slug> --index <n> [--delete]
//   node .devbook/_tools/devbook-meta/annotations.mjs sweep   --chapter <path#slug> [--status resolved]
//
// Several channels legitimately write a note — a person in an editor, a skill,
// the desktop app, a device with no clone queuing one for later — and they all
// go through this module. The CLI below and any in-process caller import the
// same five functions; nothing anywhere else writes a fence with a regular
// expression of its own.
//
// Every edit is surgical. `reply` and `resolve` splice lines into the fence
// that is already there rather than reserializing it, so a field this version
// does not know about survives a write by a version that does not know it.
//
// Writing dirties a tracked file in a repository this tool did not create.
// Callers say so, offer the commit, never push, and never auto-commit into
// someone's branch.

import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";
import {
    parseAnnotations,
    parseAnnotationBody,
    resolveAnnotation,
    annotationKinds,
    annotationStatuses,
} from "./metadata.mjs";

const FENCE = "```";

/** Split `<path>#<slug>` into its two halves; the slug is optional. */
export function parseAddress(address) {
    const hash = address.indexOf("#");
    if (hash === -1) return { path: address, slug: null };
    return { path: address.slice(0, hash), slug: address.slice(hash + 1) };
}

function normalize(text) {
    return String(text).replace(/\s+/g, " ").trim();
}

/**
 * The line range `[start, end)` of one chapter — its heading through to the
 * next heading at the same or a higher level. Without a slug, the whole file.
 */
function chapterRange(lines, slug) {
    if (!slug) return [0, lines.length];
    let start = -1;
    let level = 0;
    for (let i = 0; i < lines.length; i++) {
        const heading = /^(#{1,6})\s+(.*)$/.exec(lines[i]);
        if (!heading) continue;
        if (start === -1) {
            if (slugifyLocal(heading[2].trim()) === slug) {
                start = i;
                level = heading[1].length;
            }
            continue;
        }
        if (heading[1].length <= level) return [start, i];
    }
    if (start === -1) return null;
    return [start, lines.length];
}

// Kept local rather than imported so this module's slug never drifts from the
// one `parseAnnotations` reports addresses with — both mirror GitHub's anchors.
function slugifyLocal(text) {
    return text
        .toLowerCase()
        .trim()
        .replace(/[^\w\s-]/g, "")
        .replace(/\s/g, "-");
}

const FENCE_LINE = /^(\s*)(`{3,}|~{3,})\s*([^\s`~]*)\s*$/;

/** The line index just past a fence that opens at `open`. */
function fenceEnd(lines, open) {
    const marker = FENCE_LINE.exec(lines[open])[2];
    const closer = new RegExp(`^\\s*\\${marker[0]}{${marker.length},}\\s*$`);
    let i = open + 1;
    while (i < lines.length && !closer.test(lines[i])) i++;
    return Math.min(i + 1, lines.length);
}

/**
 * Walk a chapter's blocks, so `add` can find the passage a `--after` quote
 * names and land the note directly beneath it.
 *
 * Returns `[{ kind, text, start, end }]` where `kind` is `heading`, `meta`,
 * `annotation`, or `text`.
 */
function blocksIn(lines, start, end) {
    const blocks = [];
    let i = start;
    while (i < end) {
        const heading = /^(#{1,6})\s+(.*)$/.exec(lines[i]);
        if (heading) {
            blocks.push({ kind: "heading", text: heading[2].trim(), start: i, end: i + 1 });
            i++;
            continue;
        }
        const fence = FENCE_LINE.exec(lines[i]);
        if (fence) {
            const close = fenceEnd(lines, i);
            const label = fence[3].toLowerCase();
            blocks.push({
                kind: label === "annotation" ? "annotation" : label === "meta" ? "meta" : "text",
                text: lines.slice(i + 1, close - 1).join("\n"),
                start: i,
                end: close,
            });
            i = close;
            continue;
        }
        if (lines[i].trim() === "") {
            i++;
            continue;
        }
        const blockStart = i;
        while (
            i < end &&
            lines[i].trim() !== "" &&
            !FENCE_LINE.test(lines[i]) &&
            !/^#{1,6}\s+/.test(lines[i])
        ) {
            i++;
        }
        blocks.push({ kind: "text", text: lines.slice(blockStart, i).join("\n"), start: blockStart, end: i });
    }
    return blocks;
}

/** Serialize a note as fence lines, omitting whatever the defaults already say. */
export function renderAnnotation({ kind, status, author, date, quote, body }) {
    const out = [FENCE + "annotation"];
    if (kind && kind !== "comment") out.push(`kind: ${kind}`);
    if (status && status !== "open") out.push(`status: ${status}`);
    out.push(`author: ${author}`);
    out.push(`date: ${date}`);
    if (quote) out.push(`quote: ${quote}`);
    out.push(...renderBody(body, 0));
    out.push(FENCE);
    return out;
}

function renderBody(body, indent) {
    const pad = " ".repeat(indent);
    const text = String(body);
    if (!text.includes("\n")) return [`${pad}body: ${text}`];
    return [`${pad}body: |`, ...text.split("\n").map((line) => `${pad}  ${line}`)];
}

function today() {
    return new Date().toISOString().slice(0, 10);
}

async function loadChapter(repoRoot, address) {
    const { path: relPath, slug } = parseAddress(address);
    const absolute = path.resolve(repoRoot, relPath);
    const markdown = await readFile(absolute, "utf8");
    const eol = markdown.includes("\r\n") ? "\r\n" : "\n";
    const lines = markdown.split(/\r?\n/);
    const range = chapterRange(lines, slug);
    if (!range) throw new Error(`No chapter "${slug}" in ${relPath}.`);
    return { relPath, slug, absolute, lines, range, eol };
}

async function save({ absolute, lines, eol }) {
    await writeFile(absolute, lines.join(eol), "utf8");
}

/** The threads in one chapter, in document order, optionally filtered by status. */
export async function list(repoRoot, address, { status = null } = {}) {
    const { relPath, slug, lines, range } = await loadChapter(repoRoot, address);
    const markdown = lines.join("\n");
    return parseAnnotations(markdown)
        .filter((note) => (slug ? note.chapter?.slug === slug : true))
        .filter((note) => note.line - 1 >= range[0] && note.line - 1 < range[1])
        .map((note) => ({ ...resolveAnnotation(note.fields), ...noteAddress(note, relPath), line: note.line }))
        .filter((thread) => (status ? thread.status === status : true));
}

function noteAddress(note, relPath) {
    return {
        path: relPath,
        chapter: note.chapter?.slug ?? null,
        index: note.ordinal,
        target: note.target,
    };
}

/**
 * Add a thread. With `after`, it lands under the block containing that phrase
 * — and under any notes already on that block, so document order and ordinals
 * agree. Without one, it lands directly after the chapter's `meta` block and
 * annotates the chapter as a whole.
 */
export async function add(repoRoot, address, note) {
    const chapter = await loadChapter(repoRoot, address);
    const { lines, range } = chapter;
    const blocks = blocksIn(lines, range[0], range[1]);

    let anchorIndex = -1;
    if (note.after) {
        const wanted = normalize(note.after);
        anchorIndex = blocks.findIndex(
            (block) => block.kind === "text" && normalize(block.text).includes(wanted)
        );
        if (anchorIndex === -1) {
            throw new Error(`No block in ${address} contains "${note.after}".`);
        }
    } else {
        anchorIndex = blocks.findIndex((block) => block.kind === "meta");
        if (anchorIndex === -1) anchorIndex = blocks.findIndex((block) => block.kind === "heading");
        if (anchorIndex === -1) throw new Error(`${address} has no heading to attach a note to.`);
    }

    // Skip past notes already attached to the same block.
    let insertAfter = blocks[anchorIndex].end;
    for (let i = anchorIndex + 1; i < blocks.length && blocks[i].kind === "annotation"; i++) {
        insertAfter = blocks[i].end;
    }

    const rendered = renderAnnotation({
        kind: note.kind,
        status: note.status,
        author: note.author,
        date: note.date ?? today(),
        quote: note.quote ?? (note.after ?? null),
        body: note.body,
    });

    lines.splice(insertAfter, 0, "", ...rendered);
    await save(chapter);
    return { path: chapter.relPath, line: insertAfter + 2 };
}

/**
 * The annotation fences the addressed chapter owns, each carrying the ordinal
 * `list` reports. An ordinal counts per heading, not over the chapter's line
 * range: a fence under a subheading belongs to that subchapter, so a parent's
 * ordinals never reach it — the same rule `parseAnnotations` numbers by.
 */
function annotationsIn(chapter) {
    const ordinals = new Map();
    const found = [];
    let slug = null;
    for (const block of blocksIn(chapter.lines, chapter.range[0], chapter.range[1])) {
        if (block.kind === "heading") {
            slug = slugifyLocal(block.text);
            continue;
        }
        if (block.kind !== "annotation") continue;
        const ordinal = (ordinals.get(slug ?? "") ?? 0) + 1;
        ordinals.set(slug ?? "", ordinal);
        found.push({ ...block, chapter: slug, ordinal });
    }
    return chapter.slug ? found.filter((note) => note.chapter === chapter.slug) : found;
}

function fenceFor(chapter, index) {
    const notes = annotationsIn(chapter);
    const matches = notes.filter((note) => note.ordinal === index);
    if (matches.length > 1) {
        // A file address spans several chapters, and each numbers from one.
        const where = matches.map((note) => `#${note.chapter ?? ""}`).join(", ");
        throw new Error(
            `Annotation ${index} is ambiguous in ${chapter.relPath} — an ordinal counts within ` +
                `one chapter, and ${where} each have one. Address the chapter.`
        );
    }
    if (!matches.length) {
        throw new Error(`No annotation ${index} in that chapter — it has ${notes.length}.`);
    }
    return matches[0];
}

/**
 * Append a reply to a thread. One fence is one thread, so this splices into
 * the block that is already there and leaves every other line untouched.
 */
export async function reply(repoRoot, address, index, { author, date, body }) {
    const chapter = await loadChapter(repoRoot, address);
    const block = fenceFor(chapter, index);
    const { lines } = chapter;

    const item = [
        `  - author: ${author}`,
        `    date: ${date ?? today()}`,
        ...renderBody(body, 4).map((line) => (line.startsWith("    ") ? line : `    ${line}`)),
    ];

    // Inside the fence body, find `replies:` and the end of its list.
    const bodyStart = block.start + 1;
    const bodyEnd = block.end - 1;
    let repliesAt = -1;
    for (let i = bodyStart; i < bodyEnd; i++) {
        if (/^replies:\s*$/.test(lines[i])) {
            repliesAt = i;
            break;
        }
    }

    if (repliesAt === -1) {
        // No thread yet. `ext` sorts last by convention, so insert above it.
        let insertAt = bodyEnd;
        for (let i = bodyStart; i < bodyEnd; i++) {
            if (/^ext:\s*$/.test(lines[i])) {
                insertAt = i;
                break;
            }
        }
        lines.splice(insertAt, 0, "replies:", ...item);
    } else {
        let end = repliesAt + 1;
        while (end < bodyEnd && (lines[end].startsWith("  ") || lines[end].trim() === "")) end++;
        lines.splice(end, 0, ...item);
    }

    await save(chapter);
    return { path: chapter.relPath, index };
}

/**
 * Resolve a thread — or sweep it.
 *
 * `resolved` is a waypoint, not a resting state: it lives for the rest of the
 * branch so a reviewer sees the exchange in the pull request that raised it,
 * and `--delete` is what actually closes the loop. A note left behind forever
 * is a smell; if the aside is worth keeping, it is prose.
 */
export async function resolve(repoRoot, address, index, { delete: sweep = false } = {}) {
    const chapter = await loadChapter(repoRoot, address);
    const block = fenceFor(chapter, index);
    const { lines } = chapter;

    if (sweep) {
        let start = block.start;
        // Take the blank line that separated the note from its passage.
        if (start > 0 && lines[start - 1].trim() === "") start--;
        lines.splice(start, block.end - start);
        await save(chapter);
        return { path: chapter.relPath, index, swept: true };
    }

    const bodyStart = block.start + 1;
    const bodyEnd = block.end - 1;
    let statusAt = -1;
    for (let i = bodyStart; i < bodyEnd; i++) {
        if (/^status:\s*/.test(lines[i])) {
            statusAt = i;
            break;
        }
    }
    if (statusAt === -1) {
        let insertAt = bodyStart;
        if (/^kind:\s*/.test(lines[bodyStart] ?? "")) insertAt = bodyStart + 1;
        lines.splice(insertAt, 0, "status: resolved");
    } else {
        lines[statusAt] = "status: resolved";
    }

    await save(chapter);
    return { path: chapter.relPath, index, swept: false };
}

/**
 * Sweep a chapter: delete every fence at `status` and nothing else.
 *
 * `resolve --delete` closes one loop by ordinal, which is the move a person
 * makes the moment they answer a note. This is the other half of the same
 * rule — before a branch merges, every resolved note goes, because the prose
 * change is the record and git holds the exchange. An open note is never
 * touched, whatever else its fence carries.
 *
 * Deletes bottom-up, so a fence earlier in the file still sits where the sweep
 * found it by the time its turn comes.
 */
export async function sweep(repoRoot, address, { status = "resolved" } = {}) {
    if (!annotationStatuses().includes(status)) {
        throw new Error(`Unknown annotation status "${status}" — one of ${annotationStatuses().join(", ")}.`);
    }
    const chapter = await loadChapter(repoRoot, address);
    const { lines } = chapter;

    const notes = annotationsIn(chapter).map((note) => ({
        ...note,
        fields: resolveAnnotation(parseAnnotationBody(note.text)),
    }));
    const matching = notes.filter((note) => note.fields.status === status);

    for (const note of [...matching].sort((a, b) => b.start - a.start)) {
        let start = note.start;
        // Take the blank line that separated the note from its passage.
        if (start > 0 && lines[start - 1].trim() === "") start--;
        lines.splice(start, note.end - start);
    }

    if (matching.length) await save(chapter);
    return {
        path: chapter.relPath,
        swept: matching.map((note) => ({
            index: note.ordinal,
            chapter: note.chapter,
            kind: note.fields.kind,
            author: note.fields.author,
            date: note.fields.date,
            body: note.fields.body,
        })),
        remaining: notes.length - matching.length,
    };
}

// --- CLI -------------------------------------------------------------------

const USAGE = `annotations.mjs — read and write annotation fences

  list    --chapter <path#slug> [--status open|resolved]
  add     --chapter <path#slug> [--after "<quote>"] --author <who> --body <text>
          [--kind ${annotationKinds().join("|")}] [--date YYYY-MM-DD]
  reply   --chapter <path#slug> --index <n> --author <who> --body <text> [--date YYYY-MM-DD]
  resolve --chapter <path#slug> --index <n> [--delete]
  sweep   --chapter <path#slug> [--status ${annotationStatuses().join("|")}]

  --root <dir>   repository root (default: the working directory)`;

function optionValue(args, name) {
    const index = args.indexOf(name);
    return index !== -1 ? args[index + 1] : null;
}

async function main(argv) {
    const [command, ...args] = argv;
    if (!command || command === "--help" || command === "-h") {
        console.log(USAGE);
        return 0;
    }

    const repoRoot = path.resolve(optionValue(args, "--root") ?? process.cwd());
    const address = optionValue(args, "--chapter");
    if (!address) {
        console.error("Missing --chapter <path#slug>.");
        return 2;
    }

    switch (command) {
        case "list": {
            const threads = await list(repoRoot, address, { status: optionValue(args, "--status") });
            for (const thread of threads) {
                const replies = thread.replies?.length ? ` (+${thread.replies.length})` : "";
                console.log(
                    `${String(thread.index).padStart(2)}. [${thread.status}] ${thread.kind} — ` +
                        `${thread.author}, ${thread.date}${replies}`
                );
                console.log(`    ${String(thread.body ?? "").split("\n").join("\n    ")}`);
            }
            if (!threads.length) console.log("No annotations.");
            return 0;
        }
        case "add": {
            const result = await add(repoRoot, address, {
                after: optionValue(args, "--after"),
                kind: optionValue(args, "--kind"),
                author: optionValue(args, "--author"),
                date: optionValue(args, "--date"),
                body: optionValue(args, "--body"),
            });
            console.log(`added  ${result.path}:${result.line}`);
            console.log("This changed a tracked file — review and commit it yourself.");
            return 0;
        }
        case "reply": {
            const result = await reply(repoRoot, address, Number(optionValue(args, "--index")), {
                author: optionValue(args, "--author"),
                date: optionValue(args, "--date"),
                body: optionValue(args, "--body"),
            });
            console.log(`replied  ${result.path} #${result.index}`);
            return 0;
        }
        case "resolve": {
            const result = await resolve(repoRoot, address, Number(optionValue(args, "--index")), {
                delete: args.includes("--delete"),
            });
            console.log(`${result.swept ? "swept   " : "resolved"} ${result.path} #${result.index}`);
            return 0;
        }
        case "sweep": {
            const result = await sweep(repoRoot, address, {
                status: optionValue(args, "--status") ?? "resolved",
            });
            for (const note of result.swept) {
                console.log(`swept   ${result.path}#${note.chapter ?? ""} #${note.index} — ${note.author}, ${note.date}`);
            }
            console.log(
                result.swept.length
                    ? `${result.swept.length} note(s) swept, ${result.remaining} left. Review and commit it yourself.`
                    : `Nothing to sweep — ${result.remaining} note(s) left, none of them resolved.`
            );
            return 0;
        }
        default:
            console.error(`Unknown command "${command}".\n\n${USAGE}`);
            return 2;
    }
}

// Only run the CLI when invoked directly, so an in-process caller — the MCP
// server, a skill, the app — imports the four operations without side effects.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
    process.exit(await main(process.argv.slice(2)));
}
