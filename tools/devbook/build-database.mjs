#!/usr/bin/env node
// build-database.mjs — builds `devbook.db`, the generated devbook database, from a
// repository's devbook folders, and writes it where it is told.
//
//   node tools/devbook/build-database.mjs --check                  # build it, report, write nothing
//   node tools/devbook/build-database.mjs --out <file>             # write the database to <file>
//   node tools/devbook/build-database.mjs --root ../other-repo --check
//   node tools/devbook/build-database.mjs --root ../other-repo --generator ../devbook/tools/devbook-meta --out <file>
//
// Local ADR 0015 moved the database the app reads out of every repository and
// made the desktop app its writer: `Backlog.Infrastructure.Devbook` builds it
// into the app's storage, one per repository path. This script stays for two
// jobs and writes nothing into a repository for either. CI runs `--check`
// against the real corpus as a blocking step. And `--out` produces the
// reference the C# builder is held to: `DevbookBuilderParityTests` builds this
// repository's own `.devbook/` both ways and compares every table but `meta`
// and `problem`, row for row — which is how the two parsers stay in step.
//
// ADR 0004 replaces the twelve committed `_meta/*.json` artifacts with one
// SQLite file per repository. A scope is `WHERE folder = ?` rather than a
// separate file, which is most of the decision: the panels stop each carrying a
// parser and a projection of the same corpus, and a reader asks the index a
// question instead of reading it whole. The database is a build output,
// regenerated per machine and never committed.
//
// This is repo-native tooling, and deliberately not an edit to the generator:
// everything under `.devbook/_tools/devbook-meta/` is materialized by the
// devbook plugin, which CLAUDE.md says never to edit here. So this file
// *imports* the installed generator's exported seam, which `generator.mjs`
// finds — `buildGraph` for the nodes and edges, `buildOutlineDocument` for the
// reading order, `parseDocument` for the chapters, `collectAnnotations` and
// `openCountsByAddress` for each chapter's open-note count, `folderKindForPath`
// for the folder a path belongs to, `discoverScopes` for the folders this
// repository actually adopts. Nothing about the corpus is parsed here — the C#
// builder's port of the same parse is what the parity test holds to this output.
//
// The reading order is the generator's convention and nothing authored beside
// it: a directory's `index: root` document, else the root its folder convention
// names, then numbered entries by number, else the convention's slots with the
// rest filename-sorted between them (local ADR 0016). A `_reading-order.json`
// found in any repository is ignored; the generator never opens one.
//
// SQLite comes from `node:sqlite`, which ships with Node 22 and later and has
// FTS5 compiled in — verified against the Node 24 this repository builds with.
// There is no `package.json` for this tooling and this file does not add one.

import { createHash } from 'node:crypto';
import { mkdir, readFile, readdir, rename, rm, stat } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { dirname, resolve } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { fileURLToPath, pathToFileURL } from 'node:url';

import { DEVBOOK_SCHEMA, SCHEMA_VERSION } from './devbook-schema.mjs';
import { loadGenerator } from './generator.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));

/** The repository root, two levels up from `tools/devbook/`. */
export const DEFAULT_ROOT = resolve(HERE, '..', '..');

/** What the `meta` table records as having written the file. */
export const GENERATOR = 'tools/devbook/build-database.mjs';

/** The list-valued node attributes. These are the fields `graph.mjs` keeps as
 *  node data rather than turning into edges — `roadmap` in particular holds
 *  roadmap item tag slugs and not chapter addresses, so it must never become
 *  one — plus `aliases` and `alternatives`, which name things rather than
 *  address them. One row per value, so a reader filters instead of splitting. */
const LIST_ATTRIBUTES = ['feature-flag', 'roadmap', 'aliases', 'alternatives'];

/** Generated output and vendored trees hold no authored Markdown. */
const SKIPPED_DIRECTORIES = new Set(['_meta', '_archify', 'node_modules', '.git']);

const sha256 = (text) => createHash('sha256').update(text, 'utf8').digest('hex');

/** Every `.md` under `relFolder`, as repo-relative posix paths, sorted. */
async function collectMarkdown(repoRoot, relFolder, found = []) {
    let entries;
    try {
        entries = await readdir(path.join(repoRoot, relFolder), { withFileTypes: true });
    } catch {
        return found; // folder not adopted by this repository
    }

    for (const entry of entries) {
        const child = `${relFolder}/${entry.name}`;
        if (entry.isDirectory()) {
            if (SKIPPED_DIRECTORIES.has(entry.name)) continue;
            await collectMarkdown(repoRoot, child, found);
        } else if (entry.isFile() && entry.name.endsWith('.md')) {
            found.push(child);
        }
    }

    return found.sort();
}

/** Every `_archify/index.json` under `relFolder`, as repo-relative posix paths. */
async function collectArchifyIndexes(repoRoot, relFolder, found = []) {
    let entries;
    try {
        entries = await readdir(path.join(repoRoot, relFolder), { withFileTypes: true });
    } catch {
        return found;
    }

    for (const entry of entries) {
        if (!entry.isDirectory()) continue;
        const child = `${relFolder}/${entry.name}`;
        if (entry.name === '_archify') found.push(`${child}/index.json`);
        else if (!SKIPPED_DIRECTORIES.has(entry.name)) await collectArchifyIndexes(repoRoot, child, found);
    }

    return found.sort();
}

/** A fenced block opens or closes here: three or more backticks or tildes, at
 *  most three columns in from the margin. */
const FENCE = /^ {0,3}(`{3,}|~{3,})(.*)$/;

/** An ATX heading's marker, which `search_text` drops while keeping the words
 *  after it: the heading is the chapter's name for itself and belongs in an
 *  excerpt, the hashes are punctuation a reader did not type. */
const HEADING_MARKER = /^ {0,3}#{1,6}\s+/;

/**
 * One flag per line of `markdown`: whether that line is inside a fenced block,
 * counting the opening and closing fences themselves.
 *
 * Computed over the whole document rather than per chapter, and that is not an
 * optimisation. `parseDocument` finds headings by regular expression and does
 * not track fences, so a `#` line inside a mermaid diagram starts a chapter
 * there; a slice cut at that heading then begins inside a fence and ends with a
 * marker that closes one it never saw open. Scanning the file once means such a
 * slice is recognised as fenced throughout instead of having its prose swallowed
 * by a phantom fence — or, worse, its diagram source let through.
 */
function fenceMask(lines) {
    const fenced = new Array(lines.length).fill(false);
    let open = null;

    for (let index = 0; index < lines.length; index++) {
        const match = FENCE.exec(lines[index]);

        if (open) {
            fenced[index] = true;
            // CommonMark: a closing fence is the same character, at least as
            // long, and carries no info string. So the ``` inside a ````-fenced
            // example of a fence does not close it.
            if (match && match[1][0] === open.character && match[1].length >= open.length && match[2].trim() === '') {
                open = null;
            }
        } else if (match) {
            fenced[index] = true;
            open = { character: match[1][0], length: match[1].length };
        }
    }

    return fenced;
}

/** The prose of `lines[start..end)`: no fenced blocks, no heading markers, and
 *  no run of more than one blank line, which is what dropping whole blocks out
 *  of the middle of a chapter otherwise leaves behind. */
function proseText(lines, fenced, start, end) {
    const kept = [];

    for (let index = start; index < end; index++) {
        if (fenced[index]) continue;

        const line = lines[index].replace(HEADING_MARKER, '');
        const blank = line.trim() === '';
        if (blank && (kept.length === 0 || kept[kept.length - 1] === '')) continue;

        kept.push(blank ? '' : line);
    }

    while (kept.length && kept[kept.length - 1] === '') kept.pop();

    return kept.join('\n');
}

/**
 * One text slice per heading: from the heading line to the line before the next
 * heading, whatever its level.
 *
 * Disjoint on purpose. A nested chapter's text belongs to the nested chapter, so
 * full-text search matches the smallest chapter that contains a phrase rather
 * than every ancestor of it. The first slice starts at line 1 instead of at its
 * own heading, which is how a file's preamble — anything above the first
 * heading — stays in the corpus rather than falling out of it.
 *
 * Each slice comes back twice: `text` verbatim, and `searchText` as prose. The
 * second is what `chapter_fts` indexes and therefore what a reader is shown as
 * an excerpt — see `devbook-schema.mjs` for why a fenced block never counts as
 * prose here.
 */
function chapterSlices(markdown, chapters) {
    const lines = markdown.split(/\r?\n/);
    const fenced = fenceMask(lines);

    return chapters.map((chapter, index) => {
        const start = index === 0 ? 0 : chapter.line - 1;
        const end = index + 1 < chapters.length ? chapters[index + 1].line - 1 : lines.length;
        return {
            text: lines.slice(start, end).join('\n'),
            searchText: proseText(lines, fenced, start, end),
        };
    });
}

function insertGraph(db, graph) {
    const node = db.prepare(`
        INSERT INTO node (id, type, label, folder, path, slug, level, line, status, out_of_scope, effort, kind, version, issue)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    `);
    const attribute = db.prepare('INSERT INTO node_attribute (node_id, name, value) VALUES (?, ?, ?)');
    const edge = db.prepare('INSERT INTO edge (id, type, source, target) VALUES (?, ?, ?, ?)');

    let attributes = 0;
    for (const data of graph.nodes) {
        node.run(
            data.id,
            data.type,
            data.label ?? null,
            data.folder ?? null,
            data.path ?? null,
            data.slug ?? null,
            data.level ?? null,
            data.line ?? null,
            data.status ?? null,
            // A node outside the knowledge folders altogether: a reference
            // target that resolves to no chapter here. See devbook-schema.mjs.
            data.folder ? 0 : 1,
            typeof data.effort === 'number' ? data.effort : null,
            data.kind ?? null,
            data.version ?? null,
            data.issue ?? null
        );

        for (const name of LIST_ATTRIBUTES) {
            const values = data[name];
            if (!values) continue;
            for (const value of Array.isArray(values) ? values : [values]) {
                attribute.run(data.id, name, String(value));
                attributes++;
            }
        }
    }

    for (const data of graph.edges) {
        edge.run(data.id, data.type, data.source, data.target);
    }

    return { node: graph.nodes.length, node_attribute: attributes, edge: graph.edges.length };
}

function insertOutline(db, scope, entries, folderKindForPath) {
    const insert = db.prepare(`
        INSERT INTO outline_entry (scope, parent_id, ordinal, type, name, path, title, status, kind, is_root)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    `);

    let count = 0;
    const walk = (nodes, parentId) => {
        nodes.forEach((entry, ordinal) => {
            const result = insert.run(
                scope,
                parentId,
                ordinal,
                entry.type,
                entry.name,
                entry.path,
                entry.title ?? null,
                // The generator's resolved status: a file that omits it in an
                // editorial folder is at that folder's resting value.
                entry.status ?? null,
                // The entry's own kind where the generator gives one — an
                // area's folder, a file's resolved `type` — and otherwise one
                // derived from the path, so a reader can filter any row by
                // folder without walking back up to the area that contains it.
                entry.kind ?? folderKindForPath(entry.path.endsWith('.md') ? entry.path : `${entry.path}/x.md`),
                entry.root === true ? 1 : 0
            );
            count++;
            if (entry.children) walk(entry.children, Number(result.lastInsertRowid));
        });
    };

    walk(entries, null);
    return count;
}

/**
 * Which chapter row of `chapters` (one file's, in document order) carries the
 * open-note count of each address, as a map from chapter index to count.
 *
 * The rule `devbook-schema.mjs` states for `chapter.open_annotations`: an
 * address `<path>#<slug>` counts on the row of that path with that slug and the
 * smallest line, a bare `<path>` — a note above the file's first heading — on
 * the row of that path with the smallest line, and an address that names no
 * row is not counted anywhere.
 */
function openCountsForFile(relPath, chapters, openCounts) {
    const counts = new Map();
    if (!chapters.length) return counts;

    const firstBySlug = new Map();
    let first = 0;
    chapters.forEach((chapter, index) => {
        const current = firstBySlug.get(chapter.slug);
        if (current === undefined || chapter.line < chapters[current].line) firstBySlug.set(chapter.slug, index);
        if (chapter.line < chapters[first].line) first = index;
    });

    const add = (index, count) => counts.set(index, (counts.get(index) ?? 0) + count);

    const bare = openCounts.get(relPath);
    if (bare) add(first, bare);
    for (const [slug, index] of firstBySlug) {
        const count = openCounts.get(`${relPath}#${slug}`);
        if (count) add(index, count);
    }

    return counts;
}

async function insertChapters(db, repoRoot, folders, { parseDocument, folderKindForPath }, openCounts) {
    const insert = db.prepare(`
        INSERT INTO chapter (path, folder, slug, level, title, status, line, text, search_text, content_hash, source_hash, size, mtime, open_annotations)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    `);

    let count = 0;
    let files = 0;
    for (const folder of folders) {
        for (const relPath of await collectMarkdown(repoRoot, folder)) {
            const absolute = path.join(repoRoot, relPath);
            const markdown = await readFile(absolute, 'utf8');
            const stats = await stat(absolute);
            const { chapters } = parseDocument(markdown);
            const slices = chapterSlices(markdown, chapters);
            const sourceHash = sha256(markdown);
            const open = openCountsForFile(relPath, chapters, openCounts);
            files++;

            chapters.forEach((chapter, index) => {
                insert.run(
                    relPath,
                    folderKindForPath(relPath),
                    chapter.slug,
                    chapter.level,
                    chapter.text,
                    chapter.meta?.status ?? null,
                    chapter.line,
                    slices[index].text,
                    slices[index].searchText,
                    // Over the verbatim slice, not the prose one: the hash
                    // answers "has this chapter changed on disk", so it has to
                    // notice an edit inside a fence too.
                    sha256(slices[index].text),
                    sourceHash,
                    stats.size,
                    Math.round(stats.mtimeMs),
                    open.get(index) ?? 0
                );
                count++;
            });
        }
    }

    // External-content FTS5: the text lives in `chapter` and the index points at
    // it by rowid. Filled once, after the table it mirrors, because nothing ever
    // updates a row here — the database is built whole and renamed into place.
    // `search_text` and not `text`: the index holds the chapter's prose, so that
    // is what matches and what `snippet()` shows.
    db.exec('INSERT INTO chapter_fts (rowid, title, search_text) SELECT id, title, search_text FROM chapter');

    return { chapter: count, files };
}

async function insertArchify(db, repoRoot, folders, problems) {
    const insert = db.prepare(`
        INSERT INTO archify_artifact (chapter_path, fence_hash, ordinal, type, quality, kind, spec_path, artifact_path, checks_passed, check_count)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    `);

    let count = 0;
    for (const folder of folders) {
        for (const indexPath of await collectArchifyIndexes(repoRoot, folder, [])) {
            const artifactDirectory = path.posix.dirname(indexPath);
            const chapterDirectory = path.posix.dirname(artifactDirectory);

            let document;
            try {
                document = JSON.parse(await readFile(path.join(repoRoot, indexPath), 'utf8'));
            } catch {
                // An unreadable index is the same answer as no index — the same
                // rule ArchifyDiagramArtifacts already applies when it reads one.
                problems.push({
                    scope: folder,
                    severity: 'warning',
                    path: indexPath,
                    message: `${indexPath} is not readable JSON; its artifacts are absent from the database and their chapters fall back to mermaid.`,
                });
                continue;
            }

            for (const [fenceHash, entry] of Object.entries(document?.entries ?? {})) {
                if (typeof entry?.chapter !== 'string') {
                    // Written before one folder was found to hold several
                    // chapters. There is no chapter to key the row to, so the
                    // fence renders as mermaid until the index is regenerated.
                    problems.push({
                        scope: folder,
                        severity: 'warning',
                        path: indexPath,
                        message: `${indexPath} entry "${fenceHash}" names no chapter; re-run tools/diagrams/archify-artifacts.mjs.`,
                    });
                    continue;
                }

                insert.run(
                    `${chapterDirectory}/${entry.chapter}`,
                    fenceHash,
                    entry.ordinal ?? 0,
                    entry.type ?? null,
                    entry.quality ?? null,
                    entry.kind ?? null,
                    entry.spec ? `${artifactDirectory}/${entry.spec}` : null,
                    entry.artifact ? `${artifactDirectory}/${entry.artifact}` : null,
                    typeof entry.checksPassed === 'number' ? entry.checksPassed : null,
                    typeof entry.checkCount === 'number' ? entry.checkCount : null
                );
                count++;
            }
        }
    }

    return count;
}

/**
 * Build the devbook database for `repoRoot` at `target`.
 *
 * Writes a temporary file beside the target and renames over it, so a reader
 * never opens a half-built database — SQLite's own atomicity covers a
 * transaction, not a file that is still being populated. WAL mode is set on the
 * temporary file and survives the rename, because it is recorded in the database
 * header; closing the connection checkpoints and removes the sidecars, and any
 * sidecars left beside a *previous* target are removed too. A stale `-wal` next
 * to a replaced database is the one way this could hand a reader something worse
 * than no database at all.
 *
 * `generator` is what `generator.mjs` loaded for `repoRoot`; omitted, it is
 * loaded here.
 *
 * Returns the row counts, per table, that the file came out with.
 */
export async function buildDatabase(repoRoot, target, generator = null) {
    generator ??= await loadGenerator(repoRoot);
    const { REPO_SCOPE } = generator;

    const scopes = await generator.discoverScopes(repoRoot);
    if (!scopes.length) {
        throw new Error(
            `No knowledge folders found under ${repoRoot}. `
            + `Expected at least one of: ${generator.folders.join(', ')}.`
        );
    }

    const folders = scopes.filter((scope) => scope !== REPO_SCOPE);

    // The target's folder may not exist yet, and `DatabaseSync` reports only
    // "unable to open database file" for a missing parent.
    await mkdir(dirname(target), { recursive: true });

    const temporary = `${target}.building-${process.pid}`;
    await rm(temporary, { force: true });

    const db = new DatabaseSync(temporary);
    let counts;
    try {
        db.exec('PRAGMA journal_mode = WAL');
        db.exec(DEVBOOK_SCHEMA);
        db.exec('BEGIN');

        const problems = [];

        // One parse of the corpus, projected nowhere: `folder` is what a scope
        // narrows by, which is the point of holding one database instead of six.
        const graph = await generator.buildGraph(repoRoot);
        for (const problem of graph.problems) {
            problems.push({ scope: REPO_SCOPE, ...problem });
        }

        counts = insertGraph(db, graph);
        counts.outline_entry = 0;

        for (const scope of scopes) {
            const outline = await generator.buildOutlineDocument(repoRoot, scope, folders);
            counts.outline_entry += insertOutline(db, scope, outline.entries, generator.folderKindForPath);
            for (const problem of outline.problems) {
                problems.push({ scope, ...problem });
            }
        }

        const threads = await generator.collectAnnotations(repoRoot, folders);
        const openCounts = generator.openCountsByAddress(threads);

        const chapters = await insertChapters(db, repoRoot, folders, generator, openCounts);
        counts.chapter = chapters.chapter;
        // What the rows carry, not what the corpus holds: an open note whose
        // address names no chapter row is counted nowhere.
        counts.open_annotations = Number(db.prepare('SELECT coalesce(sum(open_annotations), 0) AS total FROM chapter').get().total);
        counts.files = chapters.files;
        counts.chapter_embedding = 0; // the semantic tier is wired and makes no live call yet.
        counts.archify_artifact = await insertArchify(db, repoRoot, folders, problems);

        const problem = db.prepare('INSERT INTO problem (scope, severity, path, message) VALUES (?, ?, ?, ?)');
        for (const entry of problems) {
            problem.run(entry.scope, entry.severity, entry.path ?? null, entry.message);
        }
        counts.problem = problems.length;

        // A timestamp is fine here and useless in the JSON artifacts: those are
        // committed and diffed by CI to detect a stale commit, so they carry
        // none. This file is never diffed, and knowing when it was built is what
        // a stale-index report is made of.
        const meta = db.prepare('INSERT INTO meta (key, value) VALUES (?, ?)');
        meta.run('schemaVersion', String(SCHEMA_VERSION));
        meta.run('generatedBy', GENERATOR);
        meta.run('generatedAt', new Date().toISOString());
        meta.run('scopes', scopes.join(','));
        counts.meta = 4;

        db.exec('COMMIT');
    } finally {
        db.close();
    }

    await rm(`${temporary}-wal`, { force: true });
    await rm(`${temporary}-shm`, { force: true });
    await rename(temporary, target);
    await rm(`${target}-wal`, { force: true });
    await rm(`${target}-shm`, { force: true });

    return counts;
}

/** The row counts, one table per line, widest label first. */
export function formatCounts(counts) {
    const order = [
        'files', 'chapter', 'open_annotations', 'node', 'node_attribute', 'edge',
        'outline_entry', 'archify_artifact', 'chapter_embedding', 'problem', 'meta',
    ];
    return order
        .filter((table) => counts[table] !== undefined)
        .map((table) => `  ${table.padEnd(18)}${String(counts[table]).padStart(6)}`)
        .join('\n');
}

// Only when run as the command; importing this file from a test must not build
// anything.
if (process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url) {
    const args = process.argv.slice(2);
    const optionValue = (name) => {
        const index = args.indexOf(name);
        return index !== -1 ? args[index + 1] : null;
    };

    const repoRoot = resolve(optionValue('--root') ?? DEFAULT_ROOT);
    const checkOnly = args.includes('--check');
    const out = optionValue('--out');

    if (!checkOnly && !out) {
        // No default target on purpose: the only place this used to write was
        // inside the repository, and local ADR 0015 took the database out of it.
        // The app builds the one it reads by itself.
        console.error('Pass --check to build and discard, or --out <file> to write the database somewhere.');
        console.error('The Backlog app builds the database it reads into its own storage (local ADR 0015).');
        process.exit(2);
    }

    let generator;
    try {
        generator = await loadGenerator(repoRoot, { generatorDir: optionValue('--generator') });
    } catch (error) {
        console.error(`Failed to build the devbook database: ${error.message}`);
        process.exit(2);
    }

    // `--check` builds the whole database and then throws it away. There is
    // nothing to diff — the file carries a timestamp — so the only question CI
    // can usefully ask is whether it builds and what it reports while building,
    // which is exactly this. It builds in the temp folder, not in the repository.
    const written = checkOnly
        ? path.join(tmpdir(), `devbook-check-${process.pid}.db`)
        : resolve(out);
    const databasePath = checkOnly ? 'devbook.db (check only)' : written;

    try {
        const counts = await buildDatabase(repoRoot, written, generator);
        console.log(`${checkOnly ? 'checked' : 'wrote  '} ${databasePath} (${generator.layout} layout, ${generator.source})`);
        console.log(formatCounts(counts));
        if (counts.problem) {
            console.log(`\n${counts.problem} problem(s) recorded in the database.`);
        }
    } catch (error) {
        console.error(`Failed to build ${databasePath}: ${error.message}`);
        process.exitCode = 2;
    } finally {
        if (checkOnly) await rm(written, { force: true });
    }
}
