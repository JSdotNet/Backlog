// reading-order.mjs — resolves a knowledge area's outline from the *authored*
// reading order, rather than from the generated index that used to carry it.
//
//   import { resolveOutline } from './reading-order.mjs';
//
// `.github/tools/knowledge-meta/outline.mjs` reads the reading order out of the
// `_meta/index.json` it is regenerating: the order of the entries, and which
// file is a directory's root document, are carried forward from the artifact on
// disk while titles and statuses are re-read from the Markdown. That makes the
// index a fixed point, which is what let CI diff it — but it also makes a
// generated file the only home of a fact a human wrote down. ADR 0004 replaces
// the JSON artifacts with a generated database, and a generated database is not
// somewhere an authored fact can live. So the authored half moves out first,
// into a committed `_reading-order.json` per scope, and this module is what
// reads it.
//
// It is deliberately a sibling of `check-metadata.mjs` rather than an edit to
// `outline.mjs`: everything under `.github/tools/knowledge-meta/` is an
// installed copy of the knowledge-base plugin's tooling, which CLAUDE.md says to
// re-sync and never edit here. Like that file, this one *imports* the installed
// generator's exported seam — `parseDocument` and `folderKindForPath` — so
// titles, statuses and folder kinds are read by exactly the code the generator
// reads them with. Only the ordering is repo-native, because only the ordering
// changed its home.
//
// Ordering rules, per directory. These are `outline.mjs`'s rules, unchanged, and
// they are unchanged on purpose: the migration that produced the first
// `_reading-order.json` files was verified by resolving the outline through this
// module and diffing it against the committed indexes.
//   1. The *root document* is the entry `root` names. It always sorts first.
//   2. The remaining declared entries keep the order `order` records — plain
//      names of sibling files (`shared.md`) or subdirectories (`inbox`).
//   3. Anything on disk but absent from `order` is appended, filename-sorted,
//      and reported as a warning-severity problem, so a new file cannot silently
//      drift to the end of a folder that cares about its order.
//   4. Anything declared but no longer on disk is dropped without comment: the
//      file was deleted, and a resolved outline follows.
//   5. A directory with no root document has no declared order at all and falls
//      back to filename sort — which is why the numbered .arc42 chapters need no
//      declaration, and why adding a file there warns about nothing.
//
// Where this file differs from `outline.mjs`, and why:
//
//   * Each scope declares only the directories inside itself. The repository
//     scope's file declares only `"."`, the order of the knowledge *areas*. The
//     committed `_meta/index.json` pair stated every nested directory twice —
//     once in the repo-wide index and once in the area's own — and the two had
//     already drifted apart: the repo index listed `.domain/tasks` last while
//     `.domain`'s own index listed it third, and the desktop reads the area's.
//     One declaration per directory is what makes that class of drift
//     impossible, so resolving the repository scope loads each area's own file.
//   * `"."` is the one entry whose `order` is honoured with a null `root`. Rule
//     5 is about directories read off disk; the areas are not that.

import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';

import { parseDocument, folderKindForPath } from '../../.github/tools/knowledge-meta/metadata.mjs';
import { KNOWLEDGE_FOLDERS, REPO_SCOPE } from '../../.github/tools/knowledge-meta/graph.mjs';

/** The `version` this module reads. A file declaring anything else is ignored
 *  wholesale rather than guessed at — the same rule the C# readers follow for an
 *  unrecognised `schemaVersion`, and for the same reason. */
export const READING_ORDER_VERSION = 1;

/** Repo-relative path of a scope's committed reading order.
 *
 *  Underscore-prefixed and at the scope root, so `readDirectory` below (and
 *  `outline.mjs:92`, and every `.md` filter in the corpus) already skips it, and
 *  deliberately *not* under `_meta/`, which the derived-artifacts convention
 *  reserves for generated output. This file is authored. */
export function readingOrderPathFor(scope) {
    return scope === REPO_SCOPE ? '_reading-order.json' : `${scope}/_reading-order.json`;
}

/**
 * The declarations one scope's committed file carries, keyed by repo-relative
 * directory.
 *
 * A missing, unreadable, malformed or unknown-version file yields an empty map:
 * a scope with no declared order falls back to filename sort everywhere, which
 * is rule 5 applied to the whole scope rather than an error. Each declaration
 * carries `declaredIn` so a warning can name the file that should have listed
 * the entry.
 */
export async function loadDeclarations(repoRoot, scope) {
    const declarations = new Map();
    const relPath = readingOrderPathFor(scope);

    let document;
    try {
        document = JSON.parse(await readFile(path.join(repoRoot, relPath), 'utf8'));
    } catch {
        return declarations; // absent or not readable JSON: undeclared.
    }

    if (document?.version !== READING_ORDER_VERSION) return declarations;
    if (typeof document.directories !== 'object' || document.directories === null) return declarations;

    for (const [directory, declared] of Object.entries(document.directories)) {
        if (typeof declared !== 'object' || declared === null) continue;
        declarations.set(directory, {
            root: typeof declared.root === 'string' ? declared.root : null,
            order: Array.isArray(declared.order) ? declared.order.filter((n) => typeof n === 'string') : [],
            declaredIn: relPath,
        });
    }

    return declarations;
}

/**
 * Every declaration that resolving `scope` needs.
 *
 * For a knowledge folder that is its own file. For the repository scope it is
 * the repo file — which declares the area order and nothing else — plus each
 * adopted area's own file, because an area's directories are declared once, by
 * the area.
 */
async function loadDeclarationsFor(repoRoot, scope, folders) {
    if (scope !== REPO_SCOPE) return loadDeclarations(repoRoot, scope);

    const declarations = await loadDeclarations(repoRoot, REPO_SCOPE);
    for (const folder of folders) {
        for (const [directory, declared] of await loadDeclarations(repoRoot, folder)) {
            declarations.set(directory, declared);
        }
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
        if (entry.name.startsWith('_') || entry.name.startsWith('.')) continue;
        if (entry.isDirectory()) dirs.push(entry.name);
        else if (entry.isFile() && entry.name.endsWith('.md')) files.push(entry.name);
    }

    // Parse every file once: the outline needs its title and status anyway.
    const parsed = new Map();
    for (const name of files.sort()) {
        const relPath = `${relDir}/${name}`;
        const { fileTitle, fileMeta } = parseDocument(await readFile(path.join(repoRoot, relPath), 'utf8'));
        parsed.set(name, { relPath, title: fileTitle, meta: fileMeta ?? {} });
    }

    const declared = declarations.get(relDir);
    // A root document declared but since deleted stops being one, which drops
    // the directory to rule 5 rather than leaving a dangling root.
    const rootName = declared?.root && parsed.has(declared.root) ? declared.root : null;
    const declaredOrder = rootName ? declared.order : [];
    const remaining = new Set([...parsed.keys(), ...dirs].filter((name) => name !== rootName));

    const sequence = [];
    for (const name of declaredOrder) {
        // Declared but gone: rule 4, dropped without comment.
        if (remaining.delete(name)) sequence.push(name);
    }
    for (const name of [...remaining].sort()) {
        if (rootName) {
            problems.push({
                severity: 'warning',
                path: `${relDir}/${name}`,
                message: `${relDir}/${name} is not listed in the reading order declared in \`${declared.declaredIn}\`; appended alphabetically. Move it there to pin its position.`,
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
                type: 'file',
                name,
                path: doc.relPath,
                title: doc.title ?? path.basename(name, '.md'),
                status: doc.meta.status ?? null,
                ...(name === rootName ? { root: true } : {}),
            });
        } else {
            const child = `${relDir}/${name}`;
            const children = await readDirectory(repoRoot, child, problems, declarations);
            outline.push({
                type: 'directory',
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
 * The knowledge areas in declared order.
 *
 * `folders` is the set of areas this repository actually adopts. An adopted area
 * the repository file does not list is appended alphabetically and warned about,
 * which is rule 3 one level up; a declared area the repository has not adopted
 * is dropped, which is rule 4.
 */
function orderAreas(folders, declarations, problems) {
    const declared = declarations.get(REPO_SCOPE);
    const remaining = new Set(folders);
    const sequence = [];

    for (const name of declared?.order ?? []) {
        if (remaining.delete(name)) sequence.push(name);
    }
    for (const name of [...remaining].sort()) {
        if (declared) {
            problems.push({
                severity: 'warning',
                path: name,
                message: `${name} is not listed in the area order declared in \`${declared.declaredIn}\`; appended alphabetically. Move it there to pin its position.`,
            });
        }
        sequence.push(name);
    }

    return sequence;
}

/**
 * Resolve one scope's outline from its committed reading order.
 *
 * Returns `{ scope, sources, entries, problems }` — the same entry shape
 * `outline.mjs` emits, so a consumer that read `_meta/index.json` reads this
 * without changing what it expects an entry to look like. There is no
 * `schemaVersion` or `generatedBy` here: this is not an artifact, it is the
 * input to one.
 */
export async function resolveOutline(repoRoot, scope = REPO_SCOPE, folders = KNOWLEDGE_FOLDERS) {
    const problems = [];
    const roots = scope === REPO_SCOPE ? folders : [scope];
    const declarations = await loadDeclarationsFor(repoRoot, scope, folders);

    let entries;
    if (scope === REPO_SCOPE) {
        // The repo-wide outline lists the knowledge areas themselves, each with
        // its own outline nested underneath.
        entries = [];
        for (const folder of orderAreas(roots, declarations, problems)) {
            const children = await readDirectory(repoRoot, folder, problems, declarations);
            entries.push({
                type: 'area',
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

    return { scope, sources: roots, problems, entries };
}
