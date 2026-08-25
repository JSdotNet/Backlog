// outline.mjs — resolves the reading order of a knowledge area.
//
// Markdown stays canonical for *content*; the reading order is declared in the
// committed `_meta/index.json` itself. This module regenerates that index:
// titles and statuses are re-read from the Markdown on every run, while the
// order of the entries — and which file is the directory's root document — is
// carried forward from the index already on disk. Regeneration is therefore a
// fixed point: same Markdown plus same index in, byte-identical index out, so
// CI can still diff it to detect a stale commit.
//
// The order used to be declared in an `order` field on the root document's
// `meta` block and derived into this index. It no longer is. A directory
// listing is not metadata about a chapter — it just happened to be written in
// the same fence — and it was being stated twice in the same repository: in the
// fence, and in the index the fence was compiled into. It is now stated once, in
// the artifact that describes the directory and that every reader already opens.
//
// Ordering rules, per directory:
//   1. The *root document* is the entry the committed index marks `root: true`.
//      It always sorts first.
//   2. The remaining committed entries keep the order the index records — plain
//      names of sibling files (`shared.md`) or subdirectories (`inbox`).
//   3. Anything on disk but absent from the index is appended, filename-sorted,
//      and reported as a problem, so a new file cannot silently drift to the end
//      of a folder that cares about its order. Move it in the index to pin it.
//   4. Anything the index records but that is no longer on disk is dropped
//      without comment: the file was deleted, and a derived artifact follows.
//   5. A directory with no root document has no declared order at all and falls
//      back to filename sort — which is why the numbered .arc42 chapters need no
//      declaration, and why adding one there warns about nothing.

import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import { parseDocument, folderKindForPath } from "./metadata.mjs";
import { KNOWLEDGE_FOLDERS, SCHEMA_VERSION, REPO_SCOPE, GENERATOR } from "./graph.mjs";

/**
 * Flatten a committed index into the per-directory declarations this module
 * reads: for each directory, the recorded entry order and which entry is its
 * root document.
 *
 * Keyed on each entry's own recorded `path` rather than on a path assembled
 * while walking, so a nested directory is looked up by exactly the string the
 * index already agreed on.
 */
function collectDeclarations(entries, relDir, declarations) {
    const order = [];
    let root = null;

    for (const entry of Array.isArray(entries) ? entries : []) {
        if (typeof entry?.name !== "string") continue;
        if (entry.root === true) root = entry.name;
        else order.push(entry.name);

        if (Array.isArray(entry.children)) {
            collectDeclarations(entry.children, entry.path ?? `${relDir}/${entry.name}`, declarations);
        }
    }

    declarations.set(relDir, { order, root });
}

/**
 * The declarations the committed index for `scope` carries, or an empty map when
 * there is none — a folder being indexed for the first time has no order to
 * preserve and falls back to filename sort.
 */
async function loadDeclarations(repoRoot, scope) {
    const declarations = new Map();
    try {
        const committed = JSON.parse(await readFile(path.join(repoRoot, outlinePathFor(scope)), "utf8"));
        collectDeclarations(committed.entries, scope, declarations);
    } catch {
        // No index, or one that is not readable JSON: treat as undeclared.
    }
    return declarations;
}

/** Read one directory into ordered `file` and `directory` outline entries. */
async function readDirectory(repoRoot, relDir, problems, declarations) {
    let entries;
    try {
        entries = await readdir(path.join(repoRoot, relDir), { withFileTypes: true });
    } catch {
        return []; // folder not present yet
    }

    const files = [];
    const dirs = [];
    for (const entry of entries) {
        // `_`-prefixed folders hold tooling artifacts, not readable content.
        if (entry.name.startsWith("_") || entry.name.startsWith(".")) continue;
        if (entry.isDirectory()) dirs.push(entry.name);
        else if (entry.isFile() && entry.name.endsWith(".md")) files.push(entry.name);
    }

    // Parse every file once: the outline needs its title and status anyway.
    const parsed = new Map();
    for (const name of files.sort()) {
        const relPath = `${relDir}/${name}`;
        const { fileTitle, fileMeta } = parseDocument(await readFile(path.join(repoRoot, relPath), "utf8"));
        parsed.set(name, { relPath, title: fileTitle, meta: fileMeta ?? {} });
    }

    const declared = declarations.get(relDir);
    // A root document recorded in the index but since deleted stops being one,
    // which drops the directory to rule 5 rather than leaving a dangling root.
    const rootName = declared?.root && parsed.has(declared.root) ? declared.root : null;
    const declaredOrder = rootName ? declared.order : [];
    const remaining = new Set([...parsed.keys(), ...dirs].filter((name) => name !== rootName));

    const sequence = [];
    for (const name of declaredOrder) {
        // Recorded but gone: rule 4, dropped without comment.
        if (remaining.delete(name)) sequence.push(name);
    }
    for (const name of [...remaining].sort()) {
        if (rootName) {
            problems.push({
                severity: "warning",
                path: `${relDir}/${name}`,
                message: `${relDir}/${name} is not listed in the reading order recorded in the committed \`_meta/index.json\`; appended alphabetically. Move it there to pin its position.`,
            });
        }
        sequence.push(name);
    }
    if (rootName) sequence.unshift(rootName);

    const outline = [];
    for (const name of sequence) {
        if (parsed.has(name)) {
            const doc = parsed.get(name);
            outline.push({
                type: "file",
                name,
                path: doc.relPath,
                title: doc.title ?? path.basename(name, ".md"),
                status: doc.meta.status ?? null,
                ...(name === rootName ? { root: true } : {}),
            });
        } else {
            const child = `${relDir}/${name}`;
            const children = await readDirectory(repoRoot, child, problems, declarations);
            outline.push({
                type: "directory",
                name,
                path: child,
                // A directory shows the title of its own root document, so a
                // viewer can label it without opening anything.
                title: children.find((c) => c.root)?.title ?? name,
                children,
            });
        }
    }
    return outline;
}

/**
 * Build the serializable outline document for one scope, following the
 * derived-artifacts convention.
 *
 * `folders` is the set of knowledge folders this repository actually adopts.
 */
export async function buildOutlineDocument(repoRoot, scope = REPO_SCOPE, folders = KNOWLEDGE_FOLDERS) {
    const problems = [];
    const roots = scope === REPO_SCOPE ? folders : [scope];
    const declarations = await loadDeclarations(repoRoot, scope);

    let entries;
    if (scope === REPO_SCOPE) {
        // The repo-wide outline lists the knowledge areas themselves, in the
        // canonical area order, each with its own outline nested underneath.
        entries = [];
        for (const folder of roots) {
            const children = await readDirectory(repoRoot, folder, problems, declarations);
            entries.push({
                type: "area",
                name: folder,
                path: folder,
                kind: folderKindForPath(`${folder}/x.md`),
                title: children.find((c) => c.root)?.title ?? folder,
                children,
            });
        }
    } else {
        entries = await readDirectory(repoRoot, scope, problems, declarations);
    }

    return {
        schemaVersion: SCHEMA_VERSION,
        generatedBy: GENERATOR,
        scope,
        sources: roots,
        // Deliberately no timestamp: the index is a deterministic function of
        // the Markdown and of its own recorded order, so re-running it produces
        // a byte-identical file and CI can diff it to detect a stale commit.
        problems,
        entries,
    };
}

/** Repo-relative output path for a scope, per the derived-artifacts convention. */
export function outlinePathFor(scope) {
    return scope === REPO_SCOPE ? "_meta/index.json" : `${scope}/_meta/index.json`;
}
