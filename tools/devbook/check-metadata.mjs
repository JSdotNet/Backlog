#!/usr/bin/env node
// check-metadata.mjs — the metadata half of the knowledge gate.
//
//   node tools/knowledge/check-metadata.mjs [--root <path>]
//
// `.github/tools/knowledge-meta/metadata.mjs` exports `validateDocument`, which
// is what knows a folder's `status` ladder, its `type` vocabulary, and which
// fields a `meta` block may carry. Nothing in this repository called it. The
// generator beside it imports `parseDocument` and `folderKindForPath` only, so
// `build.mjs --check` resolves references and says nothing about values — and
// `.domain/productivity/features.md` carried `status: idea`, a word in no
// folder's vocabulary, through every run of a workflow step named "Check
// references and metadata blocks" (issue #241).
//
// This is the missing caller, and it is deliberately a separate file rather
// than an edit to the generator: everything under
// `.github/tools/knowledge-meta/` is an installed copy of the knowledge-base
// plugin's tooling, which CLAUDE.md says to re-sync and never edit here. The
// same rule covers `build/Update-KnowledgeIndex.ps1` and both `knowledge-meta*`
// workflows, so the CI wiring is repo-native too:
// `.github/workflows/knowledge-metadata.yml`.
//
// Upstream runs this validation from the knowledge-graph canvas and the
// `knowledge-base-validate` skill rather than from `--check`. CI is a third
// consumer of the same exported seam, not a fork of it.

import { readdir, readFile, stat } from 'node:fs/promises';
import { dirname, join, resolve, sep } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import { validateDocument } from '../../.github/tools/knowledge-meta/metadata.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));

/** The repository root, two levels up from `tools/knowledge/`. */
export const DEFAULT_ROOT = resolve(HERE, '..', '..');

/** The folders `folderKindForPath` recognises. Only the ones present are scanned,
 *  so adopting a sixth folder is a matter of listing it here and nothing else. */
export const KNOWLEDGE_FOLDERS = ['.domain', '.arc42', '.backlog', '.tech', '.design'];

/** Generated output and vendored trees hold no authored `meta` blocks. `_meta`
 *  is JSON rather than Markdown and `_archify` holds specifications and rendered
 *  artifacts, but both are skipped by name so a future `.md` in either cannot
 *  quietly join the corpus. */
const SKIPPED_DIRECTORIES = new Set(['_meta', '_archify', 'node_modules', '.git']);

/** A field the folder's schema does not list.
 *
 *  `validateDocument` reports it at warning severity, and this gate treats it as
 *  blocking anyway: a field the schema does not know is the same class of defect
 *  as a value the schema does not know, and issue #241 is about that whole class
 *  passing silently, not about `status` alone. The capture group is what lets the
 *  pending-re-sync list below name individual fields. */
const UNRECOGNIZED_FIELD = /has unrecognized field `([^`]+)`/;

/** The one *error* the installed generator reports about every `.tech` chapter
 *  purely because it predates a rename.
 *
 *  The plugin's 0.16.0 tooling renamed `.tech`'s `kind` field to `type`; the copy
 *  installed here is older and still requires `kind`. All nine `.tech` files
 *  author `type:` and none authors `kind:`, so the stale copy reports 89
 *  missing-`kind` errors against a corpus that is correct. */
const STALE_TECH_KIND = /is missing required `kind` for the tech folder[.]$/;

/** Fields the installed generator does not know but the current schema defines.
 *
 *  This gate is pinned to the copy of the generator installed under
 *  `.github/tools/knowledge-meta/`, which is four releases behind the plugin.
 *  Plugin 0.16.0 allows `type`, `date` and `tests` on every folder's blocks and
 *  `index`/`number` on file-level blocks; the installed copy allows only
 *  `related, issue, effort, roadmap` plus a few folder extras. The chapter
 *  authors of this repository are told to write the *current* schema — the
 *  knowledge-base skills and `knowledge-chapter-metadata.instructions.md` come
 *  from the plugin, not from here — so blocking on these would fail a pull
 *  request for metadata that is correct.
 *
 *  Upstream reports unrecognized fields at *warning* severity for exactly this
 *  reason. Promoting the class to blocking (above) is what issue #241 asks for;
 *  exempting the fields the installed copy is merely too old to have heard of is
 *  what keeps that promotion honest. Delete this list when the generator is
 *  re-synced — at that point the schema and the validator agree again, and the
 *  tests below say what to expect when it goes. */
const FIELDS_ADDED_SINCE_INSTALL = new Set(['type', 'date', 'tests', 'index', 'number']);

/** Whether a finding blocks the build, is worth printing, or is an artifact of
 *  the pending generator re-sync. */
export function classify(relPath, issue) {
    if (relPath.startsWith('.tech/') && STALE_TECH_KIND.test(issue.message)) return 'suppressed';

    const unrecognized = UNRECOGNIZED_FIELD.exec(issue.message);
    if (unrecognized && FIELDS_ADDED_SINCE_INSTALL.has(unrecognized[1])) return 'suppressed';

    if (issue.severity === 'error') return 'blocking';
    if (issue.severity === 'warning' && unrecognized) return 'blocking';
    return 'advisory';
}

/** Whether `directory` exists and is one. A knowledge folder this repository has
 *  not adopted is skipped; one it *has* adopted that yields no files is a walker
 *  fault, and the two have to be told apart before the tally is built. */
async function isDirectory(directory) {
    try {
        return (await stat(directory)).isDirectory();
    } catch {
        return false;
    }
}

/** Every `.md` beneath `directory`, depth first, skipping generated trees.
 *
 *  An unreadable sub-directory is skipped rather than thrown, so one permission
 *  fault deep in a tree cannot take the whole gate down. The top-level folder is
 *  not covered by that: `checkRepository` checks it with `isDirectory` first, and
 *  a folder that is present but scans nothing is reported — silently covering
 *  four folders instead of five is the failure mode this gate exists to prevent. */
async function markdownUnder(directory, found = []) {
    let entries;
    try {
        entries = await readdir(directory, { withFileTypes: true });
    } catch {
        return found;
    }

    for (const entry of entries.sort((a, b) => a.name.localeCompare(b.name))) {
        if (entry.isDirectory()) {
            if (SKIPPED_DIRECTORIES.has(entry.name)) continue;
            await markdownUnder(join(directory, entry.name), found);
        } else if (entry.name.endsWith('.md')) {
            found.push(join(directory, entry.name));
        }
    }

    return found;
}

/**
 * Validate every chapter in every adopted knowledge folder under `root`.
 *
 * Returns the findings split three ways plus a per-folder tally, rather than a
 * pass/fail: the caller decides what to print and what to exit on, and the tests
 * assert on the parts separately.
 */
export async function checkRepository(root = DEFAULT_ROOT) {
    const repoRoot = resolve(root);
    const result = { root: repoRoot, files: 0, blocking: [], advisory: 0, suppressed: 0, folders: [] };

    for (const folder of KNOWLEDGE_FOLDERS) {
        const directory = join(repoRoot, folder);
        if (!(await isDirectory(directory))) continue;

        // Adopted, so it gets a tally even when it holds nothing. `folders` is
        // what the report and the tests read, and dropping an empty entry here
        // is what would let a renamed or unreadable folder go quietly ungated.
        const files = await markdownUnder(directory);

        const tally = { folder, files: files.length, blocking: 0, advisory: 0, suppressed: 0 };

        for (const file of files) {
            // `folderKindForPath` normalises separators itself, so this is for
            // the paths that end up in `blocking` and in the report: a finding
            // reads with forward slashes whichever platform produced it.
            const relPath = file.slice(repoRoot.length + 1).split(sep).join('/');

            for (const issue of validateDocument(relPath, await readFile(file, 'utf8'))) {
                const verdict = classify(relPath, issue);
                if (verdict === 'blocking') {
                    result.blocking.push({ folder, path: relPath, message: issue.message });
                }
                tally[verdict]++;
            }
        }

        result.files += tally.files;
        result.advisory += tally.advisory;
        result.suppressed += tally.suppressed;
        result.folders.push(tally);
    }

    return result;
}

/** One line per folder plus a total, followed by every blocking finding. */
export function formatReport(result) {
    const lines = [`Knowledge metadata check — ${result.root}`, ''];

    const row = (label, files, blocking, advisory, suppressed) =>
        `  ${label.padEnd(10)}${String(files).padStart(4)} files  `
        + `${String(blocking).padStart(4)} blocking  `
        + `${String(advisory).padStart(4)} advisory  `
        + `${String(suppressed).padStart(4)} pending re-sync`;

    for (const folder of result.folders) {
        lines.push(row(folder.folder, folder.files, folder.blocking, folder.advisory, folder.suppressed));
    }
    lines.push(row('total', result.files, result.blocking.length, result.advisory, result.suppressed));

    if (result.blocking.length) {
        lines.push('');
        for (const finding of result.blocking) {
            lines.push(`  ${finding.path}: ${finding.message}`);
        }
    }

    return lines.join('\n');
}

function optionValue(name) {
    const index = process.argv.indexOf(name);
    return index !== -1 ? process.argv[index + 1] : null;
}

// Only when run as the command; importing this file from a test must not check
// anything or exit.
if (process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url) {
    const result = await checkRepository(optionValue('--root') ?? DEFAULT_ROOT);
    console.log(formatReport(result));

    // A gate that silently passes because it looked in the wrong place is worse
    // than no gate, and `--root` is the one way to point it at nothing.
    const empty = result.folders.filter((folder) => folder.files === 0);

    if (!result.folders.length) {
        console.error(
            `
No knowledge folders found under ${result.root}. `
            + `Expected at least one of: ${KNOWLEDGE_FOLDERS.join(', ')}.`
        );
        process.exitCode = 2;
    } else if (empty.length) {
        console.error(
            `
${empty.map((folder) => folder.folder).join(', ')} exists but holds no Markdown. `
            + 'The gate scanned nothing there, which is indistinguishable from a pass.'
        );
        process.exitCode = 2;
    } else if (result.blocking.length) {
        console.error(
            `
${result.blocking.length} metadata block(s) do not match the schema. `
            + 'Fix the Markdown above; regenerating the indexes will not clear them.'
        );
        process.exitCode = 1;
    }

    // `process.exitCode` rather than `process.exit`: under GitHub Actions stdout
    // is a pipe, and exiting the moment after writing truncates the report on
    // exactly the run where somebody needs to read it.
}
