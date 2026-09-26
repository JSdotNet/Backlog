// Tests for build-database.mjs, run with `node --test "tools/devbook/*.test.mjs"`.
//
// Two kinds of case, following check-metadata.test.mjs. Most build a throwaway
// devbook corpus in a temp directory, because a fixture is the only way to
// assert an ordering rule against a folder small enough to write the expected
// answer down. One builds this repository's own corpus, because a writer that is
// only ever pointed at fixtures proves nothing about the file the desktop opens.
//
// The fixture is indexed by the generator this repository has installed at
// `.devbook/_tools/devbook-meta/`, passed in as `generatorDir`, so the reading
// order under test is the real convention rather than a stand-in's. Each folder
// exercises one of its rules: `domain/` the convention roots and slots, a context
// that names its own root with `index: root`, and one that has no root at all;
// `arc42/` a numbered set that filename sort would get wrong; `tech/` pinned
// first and last files. And two stray `_reading-order.json` files that declare a
// different order, which nothing may read.

import { after, test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, mkdir, readdir, stat, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { fileURLToPath } from 'node:url';

import { buildDatabase, DEFAULT_ROOT, GENERATOR } from './build-database.mjs';
import { DEVBOOK_SCHEMA, SCHEMA_VERSION } from './devbook-schema.mjs';
import { loadGenerator } from './generator.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, '..', '..');
const INSTALLED_GENERATOR = join(REPO, '.devbook', '_tools', 'devbook-meta');

const sha256 = (text) => createHash('sha256').update(text, 'utf8').digest('hex');

const exists = async (target) => {
    try {
        await stat(target);
        return true;
    } catch {
        return false;
    }
};

/** A fenced ```meta block, built from `key: value` pairs. */
function meta(fields) {
    const body = Object.entries(fields).map(([key, value]) => `${key}: ${value}`).join('\n');
    return ['```meta', body, '```'].join('\n');
}

/** A fenced ```annotation block, built from `key: value` pairs. */
function annotation(fields) {
    const body = Object.entries(fields).map(([key, value]) => `${key}: ${value}`).join('\n');
    return ['```annotation', body, '```'].join('\n');
}

/** A one-heading document. */
const page = (title, fields = {}) => `# ${title}\n\n${meta(fields)}\n`;

/** The fixture corpus, as repo-relative path to file content. */
const FIXTURE = {
    // Stray reading-order files, each declaring an order the convention does
    // not give. Local ADR 0016 retired the file: were either read, the outline
    // assertions below would come out in these orders instead.
    '.devbook/_reading-order.json': JSON.stringify({
        version: 1,
        scope: '.',
        directories: { '.': { root: null, order: ['.devbook/tech', '.devbook/domain', '.devbook/arc42'] } },
    }),
    '.devbook/domain/_reading-order.json': JSON.stringify({
        version: 1,
        scope: '.devbook/domain',
        directories: {
            '.devbook/domain': { root: 'context-map.md', order: ['legacy', 'inbox', 'billing'] },
            '.devbook/domain/inbox': { root: 'notes.md', order: ['features.md', 'domain.md'] },
        },
    }),

    '.devbook/domain/context-map.md': page('Context Map', { status: 'draft' }),

    // A bounded context by the convention: `context.md` is its root, then the
    // pinned slots in the convention's order, then everything else by name.
    '.devbook/domain/inbox/context.md': page('Inbox', { type: 'context' }),
    // Three kinds of fence in one chapter, because `search_text` has to drop all
    // three: the metadata block, the annotation block a `domain/` chapter may
    // carry, and a diagram. The annotation is open, so this chapter counts one.
    '.devbook/domain/inbox/domain.md': [
        '# Inbox',
        '',
        meta({ status: 'active', related: '.devbook/tech/shared.md' }),
        '',
        '## Aggregate: Inbox Item',
        '',
        meta({ status: 'active', roadmap: '[inbox, capture]' }),
        '',
        annotation({ kind: 'question', body: 'does a triaged item keep its capture source?' }),
        '',
        'A captured note waits here until it is triaged.',
        '',
        '```mermaid',
        'stateDiagram-v2',
        '    [*] --> Unprocessed',
        '```',
        '',
    ].join('\n'),
    // A `#` line inside a fence, which `parseDocument` reads as a heading
    // because it does not track fences. The chapter it invents is the case the
    // whole-document fence mask exists for.
    '.devbook/domain/inbox/features.md': [
        '# Inbox Features',
        '',
        meta({ status: 'draft' }),
        '',
        'Routing moves a triaged item onward.',
        '',
        '```bash',
        '# Regenerate the index',
        './build/Update-KnowledgeIndex.ps1',
        '```',
        '',
        'More prose after the fence.',
        '',
    ].join('\n'),
    '.devbook/domain/inbox/dependencies.md': page('Inbox Dependencies'),
    // Not a convention file: after the pinned slots, by name. It also carries
    // the two annotations the open-count rule has to tell apart from the one
    // above: one before any heading, which counts on the file's first chapter,
    // and one resolved, which counts nowhere.
    '.devbook/domain/inbox/notes.md': [
        annotation({ kind: 'comment', body: 'a note on the whole file' }),
        '',
        '# Inbox Notes',
        '',
        meta({ status: 'draft' }),
        '',
        '## Open Item',
        '',
        annotation({ kind: 'question', status: 'resolved', body: 'settled' }),
        '',
        'Nothing is open here any more.',
        '',
    ].join('\n'),

    // `index: root` names a root the convention would not: `overview.md` beats
    // `context.md`, which then sorts with the rest.
    '.devbook/domain/billing/context.md': page('Billing Context', { type: 'context' }),
    '.devbook/domain/billing/overview.md': page('Billing', { index: 'root' }),

    // A context with no `context.md` and no `index: root` has no entry point,
    // which the outline reports rather than guessing one.
    '.devbook/domain/legacy/domain.md': page('Legacy'),

    // Numbered: by number, not by name, so `9-` reads before `10-`; the
    // unnumbered file follows the numbered ones.
    '.devbook/arc42/01-introduction.md': page('Introduction'),
    '.devbook/arc42/10-quality.md': page('Quality'),
    '.devbook/arc42/9-risks.md': page('Risks'),
    '.devbook/arc42/about.md': page('About'),

    // Root, pinned first, the rest by name, pinned last.
    '.devbook/tech/technology-graph.md': page('Technology Graph', { status: 'adopted' }),
    '.devbook/tech/tooling.md': page('Tooling', { status: 'adopted' }),
    '.devbook/tech/shared.md': page('Shared Technologies', { status: 'adopted' }),
    '.devbook/tech/desktop.md': page('Desktop Stack', { status: 'adopted' }),

    '.devbook/domain/inbox/_archify/index.json': JSON.stringify({
        schemaVersion: 1,
        entries: {
            abc123: {
                chapter: 'domain.md',
                ordinal: 1,
                type: 'domain',
                quality: 'showcase',
                kind: 'flowchart',
                spec: 'domain.1.domain.json',
                artifact: 'domain.1.domain.html',
                checksPassed: 8,
                checkCount: 9,
            },
        },
    }),
};

/** Write the fixture corpus to a fresh temp directory and return its root. */
async function writeFixture() {
    const root = await mkdtemp(join(tmpdir(), 'devbook-db-'));
    for (const [relPath, content] of Object.entries(FIXTURE)) {
        const file = join(root, ...relPath.split('/'));
        await mkdir(dirname(file), { recursive: true });
        await writeFile(file, content, 'utf8');
    }
    // No `_meta` here on purpose. It is not committed any more, so a fresh clone
    // does not have one and the writer has to make it. Creating it in the fixture
    // hid a bug that failed every clean checkout with "unable to open database
    // file", and the test below names that case on its own as well.
    return root;
}

/** The fixture's generator: this repository's installed one. */
const fixtureGenerator = (root) => loadGenerator(root, { generatorDir: INSTALLED_GENERATOR });

/**
 * Build the fixture, hand its database to `body`, and clean up afterwards.
 *
 * The connection is read-only, which is also the mode the C# adapter opens in:
 * the generator is the only writer, and a test that could write to the file it
 * is asserting about would be testing a database nobody ships.
 */
async function withFixture(body) {
    const root = await writeFixture();
    const target = join(root, '_meta', 'devbook.db');
    try {
        const counts = await buildDatabase(root, target, await fixtureGenerator(root));
        const db = new DatabaseSync(target, { readOnly: true });
        try {
            await body({ root, target, counts, db, all: (sql, ...p) => db.prepare(sql).all(...p) });
        } finally {
            db.close();
        }
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}

/** The names of one directory's outline entries in `scope`, in order. */
function namesUnder(all, scope, parentName) {
    const rows = parentName === null
        ? all('SELECT name FROM outline_entry WHERE scope = ? AND parent_id IS NULL ORDER BY ordinal', scope)
        : all(`
            SELECT e.name FROM outline_entry e
            JOIN outline_entry p ON p.id = e.parent_id
            WHERE e.scope = ? AND p.name = ? ORDER BY e.ordinal
        `, scope, parentName);
    return rows.map((row) => row.name);
}

test('the database builds into a folder that does not exist yet', async () => {
    const root = await writeFixture();
    const outside = await mkdtemp(join(tmpdir(), 'devbook-out-'));
    try {
        const target = join(outside, 'not-yet', 'devbook.db');
        await buildDatabase(root, target, await fixtureGenerator(root));

        assert.equal(await exists(target), true);
        assert.equal(await exists(join(root, '_meta')), false, 'nothing is written into the repository');
        assert.equal(await exists(join(root, '.devbook', '_meta')), false, 'nothing is written under .devbook/');
    } finally {
        await rm(root, { recursive: true, force: true });
        await rm(outside, { recursive: true, force: true });
    }
});

test('the schema applies — every table, virtual table and index in the DDL exists', async () => {
    await withFixture(({ all }) => {
        const declared = [...DEVBOOK_SCHEMA.matchAll(/CREATE (?:VIRTUAL )?(TABLE|INDEX) (\w+)/g)]
            .map((match) => match[2])
            .sort();
        const actual = all("SELECT name FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' AND name NOT LIKE 'chapter_fts_%'")
            .map((row) => row.name)
            .sort();

        assert.deepEqual(actual, declared);
        // The FTS5 table is queryable, not merely declared: `content=` linking it
        // to a table that does not match would only fail at MATCH time.
        assert.doesNotThrow(() => all("SELECT rowid FROM chapter_fts WHERE chapter_fts MATCH 'inbox'"));
    });
});

test('the meta table records the schema version, the generator and a timestamp', async () => {
    await withFixture(({ all }) => {
        const entries = Object.fromEntries(all('SELECT key, value FROM meta').map((r) => [r.key, r.value]));

        assert.equal(entries.schemaVersion, String(SCHEMA_VERSION));
        assert.equal(entries.generatedBy, GENERATOR);
        // Unlike the JSON artifacts, which CI diffs and which therefore carry no
        // timestamp, this file is never diffed and knowing its age is the whole
        // input to a staleness report.
        assert.ok(!Number.isNaN(Date.parse(entries.generatedAt)), `generatedAt is not a date: ${entries.generatedAt}`);
    });
});

test("a chapter's text and hashes round-trip", async () => {
    await withFixture(async ({ root, all }) => {
        const rows = all("SELECT * FROM chapter WHERE path = '.devbook/domain/inbox/domain.md' ORDER BY line");
        assert.equal(rows.length, 2, 'both headings of the fixture document should be chapters');

        const [file, aggregate] = rows;
        assert.equal(file.title, 'Inbox');
        assert.equal(file.level, 1);
        assert.equal(file.status, 'active');
        assert.equal(file.folder, 'domain');

        assert.equal(aggregate.title, 'Aggregate: Inbox Item');
        assert.equal(aggregate.slug, 'aggregate-inbox-item');
        assert.match(aggregate.text, /waits here until it is triaged/);
        // Disjoint slices: the parent chapter must not carry the child's body,
        // or a phrase search would match every ancestor of the chapter it is in.
        assert.doesNotMatch(file.text, /waits here until it is triaged/);

        for (const row of rows) {
            assert.equal(row.content_hash, sha256(row.text), 'content_hash is sha256 of the chapter slice');
        }

        const source = FIXTURE['.devbook/domain/inbox/domain.md'];
        const stats = await stat(join(root, '.devbook', 'domain', 'inbox', 'domain.md'));
        assert.equal(file.source_hash, sha256(source), 'source_hash is sha256 of the whole file');
        assert.equal(file.size, stats.size);
        assert.equal(file.mtime, Math.round(stats.mtimeMs));
        // The file-level facts are what the drift check compares, so they must be
        // on every chapter of the file rather than only on its first.
        assert.equal(aggregate.source_hash, file.source_hash);
        assert.equal(aggregate.mtime, file.mtime);
    });
});

test('FTS5 matches a phrase and names the chapter it came from', async () => {
    await withFixture(({ all }) => {
        const hits = all(`
            SELECT c.path, c.slug, c.title
            FROM chapter_fts f JOIN chapter c ON c.id = f.rowid
            WHERE chapter_fts MATCH ?
        `, '"waits here until it is triaged"');

        assert.equal(hits.length, 1);
        assert.equal(hits[0].path, '.devbook/domain/inbox/domain.md');
        assert.equal(hits[0].slug, 'aggregate-inbox-item');

        // A phrase in no chapter is an empty result, not an error — the search
        // surface's "nothing found" is a different state from "no database".
        assert.equal(all('SELECT rowid FROM chapter_fts WHERE chapter_fts MATCH ?', 'nonexistentphrase').length, 0);
    });
});

test('search_text is the chapter as prose: fences dropped, heading text kept', async () => {
    await withFixture(({ all }) => {
        const [file, aggregate] = all("SELECT * FROM chapter WHERE path = '.devbook/domain/inbox/domain.md' ORDER BY line");

        // `text` is still the chapter as authored — a hash is taken over it and
        // the Archify fence addressing reads it, so nothing may be missing.
        assert.match(aggregate.text, /```meta/);
        assert.match(aggregate.text, /```annotation/);
        assert.match(aggregate.text, /stateDiagram-v2/);

        // `search_text` is the same slice with every fenced block gone and the
        // heading's `##` off the front of its words.
        assert.equal(
            aggregate.search_text,
            'Aggregate: Inbox Item\n\nA captured note waits here until it is triaged.');
        assert.equal(file.search_text, 'Inbox');
    });
});

test('a word that only appears inside a fence no longer matches', async () => {
    await withFixture(({ all }) => {
        const matches = (query) => all('SELECT rowid FROM chapter_fts WHERE chapter_fts MATCH ?', query).length;

        // `status: draft` sits in several of the fixture's metadata blocks and in
        // none of its prose. Before `search_text`, each was a hit — which is the
        // noisy half of the same defect the excerpts showed: a reader asking for
        // "draft" got every chapter whose metadata happened to say so.
        assert.equal(matches('draft'), 0);
        assert.equal(matches('status'), 0);
        assert.equal(matches('triaged AND keep'), 0, 'the annotation fence is not chapter content');
        assert.equal(matches('statediagram'), 0, 'diagram source is not prose');

        // The prose beside those fences is still there, so this is a narrower
        // index and not an emptier one.
        assert.equal(matches('"waits here until it is triaged"'), 1);
    });
});

test('an excerpt reads as prose rather than as fence debris', async () => {
    await withFixture(({ all }) => {
        const [hit] = all(`
            SELECT chapter.slug, snippet(chapter_fts, 1, '', '', '...', 24) AS excerpt
            FROM chapter_fts JOIN chapter ON chapter.id = chapter_fts.rowid
            WHERE chapter_fts MATCH 'captured'
        `);

        assert.equal(hit.slug, 'aggregate-inbox-item');
        assert.equal(
            hit.excerpt,
            'Aggregate: Inbox Item\n\nA captured note waits here until it is triaged.');
        // `snippet()` returns the indexed column, so this is the assertion that
        // the fix is at the index and not post-processed on the way out.
        assert.doesNotMatch(hit.excerpt, /```/);
        assert.doesNotMatch(hit.excerpt, /status:|roadmap:/);
    });
});

test('a heading inside a fence cannot swallow the prose around it', async () => {
    await withFixture(({ all }) => {
        // `parseDocument` finds `# Regenerate the index` inside the bash fence and
        // starts a chapter there. The fence mask is computed over the whole
        // document rather than per slice precisely so that this chapter's prose
        // survives and its fence lines still do not.
        const rows = all("SELECT * FROM chapter WHERE path = '.devbook/domain/inbox/features.md' ORDER BY line");
        assert.equal(rows.length, 2);

        assert.equal(rows[0].search_text, 'Inbox Features\n\nRouting moves a triaged item onward.');
        assert.equal(rows[1].search_text, 'More prose after the fence.');
        assert.match(rows[1].text, /Update-KnowledgeIndex/, 'the verbatim slice keeps the command');
        assert.equal(all("SELECT rowid FROM chapter_fts WHERE chapter_fts MATCH 'ps1'").length, 0);
    });
});

test('the domain outline follows the convention: context-map.md, then the contexts by name', async () => {
    await withFixture(({ all }) => {
        const top = all(`
            SELECT name, type, is_root FROM outline_entry
            WHERE scope = '.devbook/domain' AND parent_id IS NULL ORDER BY ordinal
        `);

        // Not the stray file's legacy, inbox, billing.
        assert.deepEqual(top.map((row) => row.name), ['context-map.md', 'billing', 'inbox', 'legacy']);
        assert.deepEqual(top.map((row) => row.is_root), [1, 0, 0, 0]);
        assert.deepEqual(top.map((row) => row.type), ['file', 'directory', 'directory', 'directory']);
    });
});

test('within a context, context.md is the root, then the convention slots, then the rest by name', async () => {
    await withFixture(({ all }) => {
        // Not the stray file's notes.md root and features-before-domain order.
        assert.deepEqual(namesUnder(all, '.devbook/domain', 'inbox'), [
            'context.md', 'domain.md', 'features.md', 'dependencies.md', 'notes.md',
        ]);

        const root = all(`
            SELECT e.name FROM outline_entry e JOIN outline_entry p ON p.id = e.parent_id
            WHERE e.scope = '.devbook/domain' AND p.name = 'inbox' AND e.is_root = 1
        `);
        assert.deepEqual(root.map((row) => row.name), ['context.md']);

        // A directory is titled by its own root document.
        const inbox = all("SELECT title FROM outline_entry WHERE scope = '.devbook/domain' AND name = 'inbox'")[0];
        assert.equal(inbox.title, 'Inbox');
    });
});

test('index: root wins over the convention root', async () => {
    await withFixture(({ all }) => {
        assert.deepEqual(namesUnder(all, '.devbook/domain', 'billing'), ['overview.md', 'context.md']);

        const rows = all(`
            SELECT e.name, e.is_root FROM outline_entry e JOIN outline_entry p ON p.id = e.parent_id
            WHERE e.scope = '.devbook/domain' AND p.name = 'billing' ORDER BY e.ordinal
        `);
        assert.deepEqual(rows.map((row) => row.is_root), [1, 0]);
    });
});

test('a numbered set sorts by number, unnumbered entries after it', async () => {
    await withFixture(({ all }) => {
        // Filename sort would put 10-quality.md before 9-risks.md.
        assert.deepEqual(namesUnder(all, '.devbook/arc42', null), [
            '01-introduction.md', '9-risks.md', '10-quality.md', 'about.md',
        ]);
    });
});

test('tech/ reads its root, its pinned first file, the rest, then its pinned last file', async () => {
    await withFixture(({ all }) => {
        assert.deepEqual(namesUnder(all, '.devbook/tech', null), [
            'technology-graph.md', 'shared.md', 'desktop.md', 'tooling.md',
        ]);
    });
});

test('the repository scope lists the areas in the generator order, not a stray file\'s', async () => {
    await withFixture(({ all }) => {
        const areas = all(`
            SELECT name, kind, title FROM outline_entry
            WHERE scope = '.' AND parent_id IS NULL ORDER BY ordinal
        `);
        assert.deepEqual(areas.map((row) => row.name), ['.devbook/arc42', '.devbook/domain', '.devbook/tech']);
        assert.deepEqual(areas.map((row) => row.kind), ['arc42', 'domain', 'tech']);
        assert.equal(areas[1].title, 'Context Map');
    });
});

test('outline rows carry the resolved status and the entry kind', async () => {
    await withFixture(({ all }) => {
        const row = (path) => all("SELECT status, kind FROM outline_entry WHERE scope = '.devbook/domain' AND path = ?", path)[0];

        // Declared.
        assert.equal(row('.devbook/domain/context-map.md').status, 'draft');
        // Omitted in an editorial folder: the folder's resting value, not null.
        assert.equal(row('.devbook/domain/legacy/domain.md').status, 'active');
        // A file's resolved `type` is its kind.
        assert.equal(row('.devbook/domain/inbox/context.md').kind, 'context');
        // A directory has no kind of its own, so it takes its folder's.
        assert.equal(row('.devbook/domain/inbox').kind, 'domain');
    });
});

test('a directory with no root is an outline problem, and a stray _reading-order.json is none', async () => {
    await withFixture(({ all }) => {
        const problems = all("SELECT scope, severity, path, message FROM problem WHERE path LIKE '%legacy%' AND scope = '.devbook/domain'");
        assert.equal(problems.length, 1, JSON.stringify(problems));
        assert.equal(problems[0].severity, 'warning');
        assert.equal(problems[0].path, '.devbook/domain/legacy');
        assert.match(problems[0].message, /context\.md/);

        assert.equal(all("SELECT 1 FROM problem WHERE message LIKE '%_reading-order%'").length, 0);
        assert.equal(all("SELECT 1 FROM problem WHERE path LIKE '.devbook/tech/%' OR path LIKE '.devbook/arc42/%'").length, 0);
    });
});

test('open_annotations counts open threads on their chapter, and a note above every heading on the first', async () => {
    await withFixture(({ all, counts }) => {
        const open = Object.fromEntries(
            all('SELECT path, slug, open_annotations FROM chapter WHERE open_annotations > 0')
                .map((row) => [`${row.path}#${row.slug}`, row.open_annotations]));

        assert.deepEqual(open, {
            // The open question under the aggregate heading.
            '.devbook/domain/inbox/domain.md#aggregate-inbox-item': 1,
            // The note above `# Inbox Notes`: its address is the bare path, so it
            // counts on that file's first chapter.
            '.devbook/domain/inbox/notes.md#inbox-notes': 1,
        });
        // The resolved thread under `## Open Item` counts nowhere.
        assert.equal(all("SELECT open_annotations AS n FROM chapter WHERE slug = 'open-item'")[0].n, 0);
        assert.equal(counts.open_annotations, 2);
    });
});

test('a scope filter returns only that folder', async () => {
    await withFixture(({ all }) => {
        const paths = all("SELECT DISTINCT path FROM node WHERE folder = 'tech' ORDER BY path").map((r) => r.path);
        assert.ok(paths.length > 0);
        assert.ok(paths.every((p) => p.startsWith('.devbook/tech/')), `leaked out of tech: ${paths}`);

        const chapters = all("SELECT DISTINCT path FROM chapter WHERE folder = 'domain'").map((r) => r.path);
        assert.ok(chapters.length > 0);
        assert.ok(chapters.every((p) => p.startsWith('.devbook/domain/')), `leaked out of domain: ${chapters}`);

        const outline = all("SELECT DISTINCT path FROM outline_entry WHERE scope = '.devbook/tech'").map((r) => r.path);
        assert.ok(outline.every((p) => p.startsWith('.devbook/tech/')), `leaked out of tech: ${outline}`);
    });
});

test('list-valued metadata becomes attribute rows, references become edges', async () => {
    await withFixture(({ all }) => {
        const roadmap = all("SELECT value FROM node_attribute WHERE name = 'roadmap' ORDER BY value");
        assert.deepEqual(roadmap.map((row) => row.value), ['capture', 'inbox']);

        const related = all("SELECT target FROM edge WHERE type = 'related' AND source = '.devbook/domain/inbox/domain.md'");
        assert.deepEqual(related.map((row) => row.target), ['.devbook/tech/shared.md']);
    });
});

test('the archify rows load, addressed repo-relatively', async () => {
    await withFixture(({ all }) => {
        const rows = all('SELECT * FROM archify_artifact');
        assert.equal(rows.length, 1);

        const [row] = rows;
        assert.equal(row.fence_hash, 'abc123');
        // The index records `domain.md`; the database records where that is.
        assert.equal(row.chapter_path, '.devbook/domain/inbox/domain.md');
        assert.equal(row.spec_path, '.devbook/domain/inbox/_archify/domain.1.domain.json');
        assert.equal(row.artifact_path, '.devbook/domain/inbox/_archify/domain.1.domain.html');
        assert.equal(row.ordinal, 1);
        assert.equal(row.kind, 'flowchart');
        assert.equal(row.quality, 'showcase');
        // Carried although no C# DTO reads them today; ADR 0004 lists them.
        assert.equal(row.checks_passed, 8);
        assert.equal(row.check_count, 9);
    });
});

test('chapter_embedding is created and left empty', async () => {
    await withFixture(({ all, counts }) => {
        assert.equal(all('SELECT count(*) AS c FROM chapter_embedding')[0].c, 0);
        assert.equal(counts.chapter_embedding, 0);
    });
});

test('the build leaves no temporary file and no journal sidecar behind', async () => {
    await withFixture(async ({ root }) => {
        const written = await readdir(join(root, '_meta'));
        assert.deepEqual(written.sort(), ['devbook.db']);
    });
});

test("this repository's own corpus builds", async () => {
    // Built into a temp file: nothing this suite builds belongs anywhere else.
    const scratch = await mkdtemp(join(tmpdir(), 'devbook-db-repo-'));
    const target = join(scratch, 'devbook.db');
    try {
        const counts = await buildDatabase(DEFAULT_ROOT, target);

        assert.ok(counts.files > 100, `only ${counts.files} markdown files were indexed`);
        assert.ok(counts.chapter > counts.files, 'every file should contribute at least one chapter');
        assert.ok(counts.node > 0 && counts.edge > 0, 'the graph came out empty');
        assert.ok(counts.outline_entry > 0, 'the outline came out empty');
        assert.equal(counts.archify_artifact, 39, 'the fourteen _archify indexes hold 39 artifacts');

        const db = new DatabaseSync(target, { readOnly: true });
        try {
            const blocking = db.prepare("SELECT scope, path, message FROM problem WHERE severity = 'error'").all();
            assert.equal(blocking.length, 0, `errors recorded:\n${blocking.map((p) => `${p.path}: ${p.message}`).join('\n')}`);

            // Every scope the repository adopts produced an outline, so a folder
            // that quietly stopped resolving cannot pass as "nothing to order".
            const scopes = db.prepare('SELECT DISTINCT scope FROM outline_entry ORDER BY scope').all().map((r) => r.scope);
            assert.deepEqual(scopes, ['.', '.devbook/ai', '.devbook/arc42', '.devbook/design', '.devbook/domain', '.devbook/tech']);

            // The domain folder opens on its convention root.
            const first = db.prepare("SELECT name, is_root FROM outline_entry WHERE scope = '.devbook/domain' AND parent_id IS NULL AND ordinal = 0").get();
            assert.deepEqual({ ...first }, { name: 'context-map.md', is_root: 1 });
        } finally {
            db.close();
        }
    } finally {
        await rm(scratch, { recursive: true, force: true });
    }
});

/**
 * This repository's own corpus, built once and shared by the cases below it.
 *
 * A fixture cannot answer what these ask. "Does an excerpt read as prose" is a
 * question about seventy-two authored documents whose metadata blocks are the
 * densest thing in them, and the defect this pair pins was reported against the
 * real corpus rather than against anything a test wrote.
 */
let repositoryCorpus = null;

function withRepositoryCorpus() {
    repositoryCorpus ??= (async () => {
        // Into a temp file, for the reason the case above gives.
        const scratch = await mkdtemp(join(tmpdir(), 'devbook-db-corpus-'));
        const target = join(scratch, 'devbook.db');
        await buildDatabase(DEFAULT_ROOT, target);
        const db = new DatabaseSync(target, { readOnly: true });
        return { scratch, db, all: (sql, ...p) => db.prepare(sql).all(...p) };
    })();

    return repositoryCorpus;
}

after(async () => {
    if (!repositoryCorpus) return;
    const { scratch, db } = await repositoryCorpus;
    db.close();
    await rm(scratch, { recursive: true, force: true });
});

test("an excerpt from this repository's corpus reads as prose, not fence debris", async () => {
    const { all } = await withRepositoryCorpus();

    // The chapter QA reported, since folded from `naming.md` into the aggregate it
    // named. Searching for a word in its first sentence used to return the tail
    // of its ```meta block:
    //   "...draft aliases: [KnowledgeNote, Note] related: [...] ``` The durable"
    const [hit] = all(`
        SELECT snippet(chapter_fts, 1, '', '', '...', 24) AS excerpt
        FROM chapter_fts JOIN chapter ON chapter.id = chapter_fts.rowid
        WHERE chapter_fts MATCH 'organized' AND chapter.path = '.devbook/domain/devbook/domain.md'
            AND chapter.slug = 'knowledge-note'
    `);

    assert.ok(hit, 'expected .devbook/domain/devbook/domain.md#knowledge-note to match "organized"');
    assert.match(hit.excerpt, /An organized unit of project knowledge/);
    assert.doesNotMatch(hit.excerpt, /```/);
    assert.doesNotMatch(hit.excerpt, /\b(status|aliases|related|type):/);

    // And not that one chapter only: an excerpt is a window onto the indexed
    // column, so no fenced block anywhere in the corpus may still be in one.
    // A line that *opens or closes* a fence is the test — prose that mentions
    // ```meta mid-sentence, or a table cell holding ``` as inline code, is a
    // reader's own words and stays.
    const fenced = all('SELECT path, slug, search_text FROM chapter')
        .filter((row) => /^ {0,3}(```|~~~)/m.test(row.search_text))
        .map((row) => `${row.path}#${row.slug}`);
    assert.deepEqual(fenced, [], 'a fenced block survived into the search text');
});

test("`draft` no longer matches a chapter that only says so in its metadata", async () => {
    const { all } = await withRepositoryCorpus();

    const metadataOnly = all(`
        SELECT path, slug FROM chapter
        WHERE text LIKE '%status: draft%' AND lower(search_text) NOT LIKE '%draft%'
    `);
    assert.ok(
        metadataOnly.length > 20,
        `only ${metadataOnly.length} chapters carry "status: draft" without saying "draft" in prose; the case has no teeth`);

    const matched = new Set(all(`
        SELECT chapter.path || '#' || chapter.slug AS reference
        FROM chapter_fts JOIN chapter ON chapter.id = chapter_fts.rowid
        WHERE chapter_fts MATCH 'draft'
    `).map((row) => row.reference));

    const leaked = metadataOnly
        .map((row) => `${row.path}#${row.slug}`)
        .filter((reference) => matched.has(reference));

    assert.deepEqual(leaked, [], 'these chapters match "draft" on their metadata block alone');

    // The measured effect, not just the absence of one: 444 of 1124 chapters
    // matched "draft" when the fence was indexed as prose.
    assert.ok(
        matched.size < metadataOnly.length,
        `"draft" still matches ${matched.size} chapters, more than the ${metadataOnly.length} whose fence says so`);
});

test('the repository root is where this file thinks it is', () => {
    assert.equal(DEFAULT_ROOT, REPO);
});
