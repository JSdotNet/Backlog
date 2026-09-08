// Tests for build-database.mjs, run with `node --test tools/knowledge`.
//
// Two kinds of case, following check-metadata.test.mjs. Most build a throwaway
// knowledge corpus in a temp directory, because a fixture is the only way to
// assert an ordering rule against a folder small enough to write the expected
// answer down. One builds this repository's own corpus, because a writer that is
// only ever pointed at fixtures proves nothing about the file the desktop opens.
//
// The fixture is deliberately lopsided: `.domain` declares a reading order and
// holds a file the declaration does not list, `.tech` declares none at all. That
// is rules 3 and 5 of the reading order in one tree, and they are the two rules
// a database can silently get wrong — a wrong order still renders.

import { after, test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, mkdir, readdir, stat, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { fileURLToPath } from 'node:url';

import { buildDatabase, DEFAULT_ROOT, GENERATOR } from './build-database.mjs';
import { KNOWLEDGE_SCHEMA, SCHEMA_VERSION } from './knowledge-schema.mjs';
import { resolveOutline } from './reading-order.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, '..', '..');

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

/** The fixture corpus, as repo-relative path to file content. */
const FIXTURE = {
    // The repository scope declares the area order and nothing else. Reversed
    // against the generator's own KNOWLEDGE_FOLDERS constant on purpose: if the
    // areas came from that constant rather than from this file, the assertion
    // below would still pass on alphabetical luck.
    '_reading-order.json': JSON.stringify({
        version: 1,
        scope: '.',
        directories: { '.': { root: null, order: ['.tech', '.domain'] } },
    }),

    '.domain/_reading-order.json': JSON.stringify({
        version: 1,
        scope: '.domain',
        directories: {
            '.domain': { root: 'context-map.md', order: ['inbox', 'shared.md'] },
            '.domain/inbox': { root: 'domain.md', order: ['features.md'] },
        },
    }),
    '.domain/context-map.md': `# Context Map\n\n${meta({ status: 'draft' })}\n`,
    '.domain/shared.md': `# Shared\n\n${meta({ status: 'active' })}\n`,
    // Listed nowhere: rule 3, appended alphabetically and warned about.
    '.domain/unlisted.md': `# Unlisted\n\n${meta({ status: 'draft' })}\n`,
    // Three kinds of fence in one chapter, because `search_text` has to drop all
    // three: the metadata block, the annotation block a `.domain` chapter may
    // carry, and a diagram.
    '.domain/inbox/domain.md': [
        '# Inbox',
        '',
        meta({ status: 'active', related: '.tech/shared.md' }),
        '',
        '## Aggregate: Inbox Item',
        '',
        meta({ status: 'active', 'feature-flag': '[inbox, capture]' }),
        '',
        '```annotation',
        'open-question: does a triaged item keep its capture source?',
        '```',
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
    '.domain/inbox/features.md': [
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

    // No `_reading-order.json`: rule 5, filename sort, no warning.
    '.tech/technology-graph.md': `# Technology Graph\n\n${meta({ status: 'adopted' })}\n`,
    '.tech/shared.md': `# Shared Technologies\n\n${meta({ status: 'adopted' })}\n`,
    '.tech/desktop.md': `# Desktop Stack\n\n${meta({ status: 'adopted' })}\n`,

    '.domain/inbox/_archify/index.json': JSON.stringify({
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
    const root = await mkdtemp(join(tmpdir(), 'knowledge-db-'));
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

/**
 * Build the fixture, hand its database to `body`, and clean up afterwards.
 *
 * The connection is read-only, which is also the mode the C# adapter opens in:
 * the generator is the only writer, and a test that could write to the file it
 * is asserting about would be testing a database nobody ships.
 */
async function withFixture(body) {
    const root = await writeFixture();
    const target = join(root, '_meta', 'knowledge.db');
    try {
        const counts = await buildDatabase(root, target);
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

test('the database builds when _meta does not exist yet', async () => {
    const root = await writeFixture();
    try {
        assert.equal(await exists(join(root, '_meta')), false);

        const target = join(root, '_meta', 'knowledge.db');
        await buildDatabase(root, target);

        assert.equal(await exists(target), true);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});

test('the schema applies — every table, virtual table and index in the DDL exists', async () => {
    await withFixture(({ all }) => {
        const declared = [...KNOWLEDGE_SCHEMA.matchAll(/CREATE (?:VIRTUAL )?(TABLE|INDEX) (\w+)/g)]
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
        const rows = all("SELECT * FROM chapter WHERE path = '.domain/inbox/domain.md' ORDER BY line");
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

        const source = FIXTURE['.domain/inbox/domain.md'];
        const stats = await stat(join(root, '.domain', 'inbox', 'domain.md'));
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
        assert.equal(hits[0].path, '.domain/inbox/domain.md');
        assert.equal(hits[0].slug, 'aggregate-inbox-item');

        // A phrase in no chapter is an empty result, not an error — the search
        // surface's "nothing found" is a different state from "no database".
        assert.equal(all('SELECT rowid FROM chapter_fts WHERE chapter_fts MATCH ?', 'nonexistentphrase').length, 0);
    });
});

test('search_text is the chapter as prose: fences dropped, heading text kept', async () => {
    await withFixture(({ all }) => {
        const [file, aggregate] = all("SELECT * FROM chapter WHERE path = '.domain/inbox/domain.md' ORDER BY line");

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

        // `status: draft` sits in three of the fixture's metadata blocks and in
        // none of its prose. Before `search_text`, this was three hits — which is
        // the noisy half of the same defect the excerpts showed: a reader asking
        // for "draft" got every chapter whose metadata happened to say so.
        assert.equal(matches('draft'), 0);
        assert.equal(matches('status'), 0);
        assert.equal(matches('question'), 0, 'the annotation fence is not chapter content');
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
        assert.doesNotMatch(hit.excerpt, /status:|feature-flag:/);
    });
});

test('a heading inside a fence cannot swallow the prose around it', async () => {
    await withFixture(({ all }) => {
        // `parseDocument` finds `# Regenerate the index` inside the bash fence and
        // starts a chapter there. The fence mask is computed over the whole
        // document rather than per slice precisely so that this chapter's prose
        // survives and its fence lines still do not.
        const rows = all("SELECT * FROM chapter WHERE path = '.domain/inbox/features.md' ORDER BY line");
        assert.equal(rows.length, 2);

        assert.equal(rows[0].search_text, 'Inbox Features\n\nRouting moves a triaged item onward.');
        assert.equal(rows[1].search_text, 'More prose after the fence.');
        assert.match(rows[1].text, /Update-KnowledgeIndex/, 'the verbatim slice keeps the command');
        assert.equal(all("SELECT rowid FROM chapter_fts WHERE chapter_fts MATCH 'ps1'").length, 0);
    });
});

test('the outline honours _reading-order.json', async () => {
    await withFixture(({ all }) => {
        const top = all(`
            SELECT name, type, is_root FROM outline_entry
            WHERE scope = '.domain' AND parent_id IS NULL ORDER BY ordinal
        `);

        assert.deepEqual(top.map((row) => row.name), [
            'context-map.md', // rule 1: the root document always sorts first
            'inbox',          // rules 2: declared order, subdirectory before sibling file
            'shared.md',
            'unlisted.md',    // rule 3: on disk, undeclared, appended alphabetically
        ]);
        assert.deepEqual(top.map((row) => row.is_root), [1, 0, 0, 0]);
        assert.equal(top.find((row) => row.name === 'inbox').type, 'directory');

        const inbox = all(`
            SELECT e.name, e.is_root FROM outline_entry e
            JOIN outline_entry p ON p.id = e.parent_id
            WHERE e.scope = '.domain' AND p.name = 'inbox' ORDER BY e.ordinal
        `);
        assert.deepEqual(inbox.map((row) => row.name), ['domain.md', 'features.md']);

        // Rule 5: `.tech` declares nothing, so it falls back to filename sort —
        // which is a different answer from its declared order would have been.
        const tech = all(`
            SELECT name FROM outline_entry
            WHERE scope = '.tech' AND parent_id IS NULL ORDER BY ordinal
        `);
        assert.deepEqual(tech.map((row) => row.name), ['desktop.md', 'shared.md', 'technology-graph.md']);

        // The repository scope orders the areas from its own file, not from the
        // generator's KNOWLEDGE_FOLDERS constant.
        const areas = all(`
            SELECT name, kind FROM outline_entry
            WHERE scope = '.' AND parent_id IS NULL ORDER BY ordinal
        `);
        assert.deepEqual(areas.map((row) => row.name), ['.tech', '.domain']);
        assert.deepEqual(areas.map((row) => row.kind), ['tech', 'domain']);
    });
});

test('an undeclared file is a warning, and an undeclared directory is not', async () => {
    await withFixture(({ all }) => {
        const problems = all("SELECT scope, severity, path, message FROM problem WHERE path LIKE '%unlisted%'");

        const problem = problems.find((row) => row.scope === '.domain');
        assert.ok(problem, `expected a .domain problem for the unlisted file, got: ${JSON.stringify(problems)}`);
        assert.equal(problem.severity, 'warning');
        assert.match(problem.message, /_reading-order\.json/);
        assert.match(problem.message, /appended alphabetically/);

        // `.tech` has no root document, so nothing there is out of order — rule 5
        // means undeclared, not unordered.
        assert.equal(all("SELECT 1 FROM problem WHERE path LIKE '.tech/%'").length, 0);
    });
});

test('a scope filter returns only that folder', async () => {
    await withFixture(({ all }) => {
        const paths = all("SELECT DISTINCT path FROM node WHERE folder = 'tech' ORDER BY path").map((r) => r.path);
        assert.ok(paths.length > 0);
        assert.ok(paths.every((p) => p.startsWith('.tech/')), `leaked out of .tech: ${paths}`);

        const chapters = all("SELECT DISTINCT path FROM chapter WHERE folder = 'domain'").map((r) => r.path);
        assert.ok(chapters.length > 0);
        assert.ok(chapters.every((p) => p.startsWith('.domain/')), `leaked out of .domain: ${chapters}`);

        const outline = all("SELECT DISTINCT path FROM outline_entry WHERE scope = '.tech'").map((r) => r.path);
        assert.ok(outline.every((p) => p.startsWith('.tech/')), `leaked out of .tech: ${outline}`);
    });
});

test('list-valued metadata becomes attribute rows, references become edges', async () => {
    await withFixture(({ all }) => {
        const flags = all("SELECT value FROM node_attribute WHERE name = 'feature-flag' ORDER BY value");
        assert.deepEqual(flags.map((row) => row.value), ['capture', 'inbox']);

        const related = all("SELECT target FROM edge WHERE type = 'related' AND source = '.domain/inbox/domain.md'");
        assert.deepEqual(related.map((row) => row.target), ['.tech/shared.md']);
    });
});

test('the archify rows load, addressed repo-relatively', async () => {
    await withFixture(({ all }) => {
        const rows = all('SELECT * FROM archify_artifact');
        assert.equal(rows.length, 1);

        const [row] = rows;
        assert.equal(row.fence_hash, 'abc123');
        // The index records `domain.md`; the database records where that is.
        assert.equal(row.chapter_path, '.domain/inbox/domain.md');
        assert.equal(row.spec_path, '.domain/inbox/_archify/domain.1.domain.json');
        assert.equal(row.artifact_path, '.domain/inbox/_archify/domain.1.domain.html');
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
        assert.deepEqual(written.sort(), ['knowledge.db']);
    });
});

test('the resolver reads the same order the database records', async () => {
    const root = await writeFixture();
    try {
        // The database is the writer's projection of this; if they disagree, the
        // bug is in the projection rather than in the reading order.
        const outline = await resolveOutline(root, '.domain', ['.domain', '.tech']);
        assert.deepEqual(outline.entries.map((entry) => entry.name), [
            'context-map.md', 'inbox', 'shared.md', 'unlisted.md',
        ]);
        assert.equal(outline.problems.length, 1);
        assert.equal(outline.problems[0].severity, 'warning');
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});

test("this repository's own corpus builds", async () => {
    // Built into a temp file rather than over `_meta/knowledge.db`: a test suite
    // that replaces the database the desktop is reading would be a side effect
    // nobody asked for, on a file that is a build output either way.
    const scratch = await mkdtemp(join(tmpdir(), 'knowledge-db-repo-'));
    const target = join(scratch, 'knowledge.db');
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
            assert.deepEqual(scopes, ['.', '.arc42', '.backlog', '.design', '.domain', '.tech']);
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
        // Into a temp file rather than over `_meta/knowledge.db`, for the reason
        // the case above gives: a suite that replaces the database the desktop is
        // reading is a side effect nobody asked for.
        const scratch = await mkdtemp(join(tmpdir(), 'knowledge-db-corpus-'));
        const target = join(scratch, 'knowledge.db');
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

    // The chapter QA reported. Searching for a word in its first sentence used to
    // return the tail of its ```meta block:
    //   "...draft aliases: [KnowledgeNote, Note] related: [...] ``` The durable"
    const [hit] = all(`
        SELECT snippet(chapter_fts, 1, '', '', '...', 24) AS excerpt
        FROM chapter_fts JOIN chapter ON chapter.id = chapter_fts.rowid
        WHERE chapter_fts MATCH 'durable' AND chapter.path = '.domain/second-brain/naming.md'
    `);

    assert.ok(hit, 'expected .domain/second-brain/naming.md to match "durable"');
    assert.match(hit.excerpt, /The durable unit of captured knowledge/);
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
