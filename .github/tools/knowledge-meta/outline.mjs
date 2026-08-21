// outline.mjs — derives the ordered reading outline of a knowledge area from
// the `order` field in each directory's root document.
//
// Markdown stays canonical; this produces the *derived* index that a viewer
// reads to present files in their intended order instead of alphabetically.
//
// Ordering rules, per directory:
//   1. The directory's *root document* is the file whose file-level `meta`
//      block carries `order`. It always sorts first.
//   2. `order` lists the remaining entries — plain names of sibling files
//      (`shared.md`) or subdirectories (`inbox`) — in reading order.
//   3. Anything present but unlisted is appended, filename-sorted, and
//      reported as a problem so the declaration cannot silently drift.
//   4. A directory with no root document falls back to filename sort, which is
//      why the numbered .arc42 chapters need no declaration at all.

import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import { parseDocument, folderKindForPath, resolveType } from "./metadata.mjs";
import { KNOWLEDGE_FOLDERS, SCHEMA_VERSION, REPO_SCOPE, GENERATOR } from "./graph.mjs";

/** Read one directory into ordered `file` and `directory` outline entries. */
async function readDirectory(repoRoot, relDir, problems) {
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

    // Parse every file once: we need its title/status for the outline anyway,
    // and its file-level `order` to know whether it is the root document.
    const parsed = new Map();
    for (const name of files.sort()) {
        const relPath = `${relDir}/${name}`;
        const { fileTitle, fileMeta } = parseDocument(await readFile(path.join(repoRoot, relPath), "utf8"));
        parsed.set(name, { relPath, title: fileTitle, meta: fileMeta ?? {} });
    }

    const roots = [...parsed.entries()].filter(([, doc]) => Array.isArray(doc.meta.order));
    if (roots.length > 1) {
        problems.push({
            severity: "error",
            path: relDir,
            message: `${relDir} has more than one document declaring \`order\` (${roots
                .map(([name]) => name)
                .join(", ")}); exactly one root document may define the directory's reading order.`,
        });
    }

    const [rootName, rootDoc] = roots[0] ?? [];
    const declared = rootDoc?.meta.order ?? [];
    const remaining = new Set([...parsed.keys(), ...dirs].filter((name) => name !== rootName));

    const sequence = [];
    for (const name of declared) {
        if (!remaining.delete(name)) {
            problems.push({
                severity: "error",
                path: rootDoc.relPath,
                message: `\`order\` lists "${name}", which is not a file or directory in ${relDir}.`,
            });
            continue;
        }
        sequence.push(name);
    }
    for (const name of [...remaining].sort()) {
        if (rootName) {
            problems.push({
                severity: "warning",
                path: rootDoc.relPath,
                message: `${relDir}/${name} is missing from \`order\` in ${rootName}; appended alphabetically. Add it to pin its position.`,
            });
        }
        sequence.push(name);
    }
    if (rootName) sequence.unshift(rootName);

    const outline = [];
    for (const name of sequence) {
        if (parsed.has(name)) {
            const doc = parsed.get(name);
            // Titles are name-only, so every file in a `.domain` bounded context
            // shares one title; `kind` is what tells them apart in a viewer.
            const fileKind = resolveType(folderKindForPath(doc.relPath), doc.meta);
            outline.push({
                type: "file",
                name,
                path: doc.relPath,
                title: doc.title ?? path.basename(name, ".md"),
                ...(fileKind ? { kind: fileKind } : {}),
                status: doc.meta.status ?? null,
                ...(name === rootName ? { root: true } : {}),
            });
        } else {
            const child = `${relDir}/${name}`;
            const children = await readDirectory(repoRoot, child, problems);
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

    let entries;
    if (scope === REPO_SCOPE) {
        // The repo-wide outline lists the knowledge areas themselves, in the
        // canonical area order, each with its own outline nested underneath.
        entries = [];
        for (const folder of roots) {
            const children = await readDirectory(repoRoot, folder, problems);
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
        entries = await readDirectory(repoRoot, scope, problems);
    }

    return {
        schemaVersion: SCHEMA_VERSION,
        generatedBy: GENERATOR,
        scope,
        sources: roots,
        // Deliberately no timestamp: the index is a deterministic function of
        // the Markdown, so re-running it produces a byte-identical file and CI
        // can diff it to detect a stale commit.
        problems,
        entries,
    };
}

/** Repo-relative output path for a scope, per the derived-artifacts convention. */
export function outlinePathFor(scope) {
    return scope === REPO_SCOPE ? "_meta/index.json" : `${scope}/_meta/index.json`;
}
