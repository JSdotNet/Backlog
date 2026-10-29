// Tests for brain.mjs, run with `node --test "tools/devbook/*.test.mjs"`.
//
// The Brain convention for the database build: the areas `CONVENTIONS.md`
// declares, with PARA as the fallback, then every other top folder holding a
// note, minus the folders its `Ignore` section lists, and a Brain built into a
// database whose folders are those areas, whose notes carry `para`, `repos` and
// `tasks`, and whose `repo:` links are edges nothing resolves. The C# side is held to the
// same answers by `BrainConventionsTests` and `DevbookBuilderParityTests`.

import { after, test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { fileURLToPath } from 'node:url';

import { declaredAreas, ignoredFolders, loadBrainGenerator, parseConventions, PARA_AREAS, readBrainAreas, repoLinkEdges, resolveAreas } from './brain.mjs';
import { buildDatabase } from './build-database.mjs';
import { loadGenerator } from './generator.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, '..', '..');

const scratch = await mkdtemp(join(tmpdir(), 'backlog-brain-'));
after(() => rm(scratch, { recursive: true, force: true }));

async function brain(files) {
    const root = await mkdtemp(join(scratch, 'brain-'));
    for (const [relPath, text] of Object.entries(files)) {
        await mkdir(dirname(join(root, relPath)), { recursive: true });
        await writeFile(join(root, relPath), text);
    }
    return root;
}

test('a table declares the areas, its Order column first', () => {
    const areas = parseConventions([
        '## Areas',
        '',
        '| Name | Folder | Order |',
        '|---|---|---|',
        '| Later | later | 2 |',
        '| Unnumbered | `unnumbered` | |',
        '| First | first | 1 |',
    ].join('\n'));

    assert.deepEqual(areas, [
        { name: 'First', folder: 'first', order: 1 },
        { name: 'Later', folder: 'later', order: 2 },
        { name: 'Unnumbered', folder: 'unnumbered', order: 3 },
    ]);
});

test('a list declares the areas, named or not, outside fences and other sections', () => {
    const areas = declaredAreas([
        '# Conventions',
        '- not an area',
        '### Areas',
        '- Inbox — `_inbox`',
        '- `projects`',
        '- people',
        '* Side work: `side-work`',
        '- `../escape`',
        '- `Projects`',
        '```',
        '- `fenced`',
        '```',
        '## Naming',
        '- `naming`',
    ].join('\r\n'));

    assert.deepEqual(areas, [
        { name: 'Inbox', folder: '_inbox', order: 1 },
        { name: 'Projects', folder: 'projects', order: 2 },
        { name: 'People', folder: 'people', order: 3 },
        { name: 'Side work', folder: 'side-work', order: 4 },
    ]);
});

test('a byte order mark does not hide the first heading', () => {
    assert.deepEqual(declaredAreas('﻿## Areas\n\n- `work`\n').map((area) => area.folder), ['work']);
});

test('nothing declared is the PARA set', () => {
    for (const markdown of [null, '', '# Conventions\n', '## Areas\n\nNone yet.\n']) {
        assert.deepEqual(parseConventions(markdown).map((area) => area.folder), PARA_AREAS.map((area) => area.folder));
    }
});

test('an Ignore section lists folders as list items or a Folder column, once each', () => {
    const markdown = [
        '- `not-ignored`',
        '## Areas',
        '- `work`',
        '## Ignore',
        '- `_templates`',
        '- Attachments — `attachments`',
        '- scratch',
        '- `Scratch`',
        '- `../escape`',
        '',
        '| Folder | Why |',
        '|---|---|',
        '| Product | scratch |',
        '## Notes',
        '- `notes`',
    ].join('\n');

    assert.deepEqual(ignoredFolders(markdown), ['_templates', 'attachments', 'scratch', 'Product']);
    assert.deepEqual(ignoredFolders('## Areas\n- `work`\n'), []);
    assert.deepEqual(ignoredFolders(null), []);
});

test('discovered areas follow the declared ones by folder name, as .NET compares it', async () => {
    const probed = [];
    const areas = await resolveAreas(
        declaredAreas('## Areas\n- Work — `work`\n- `missing`\n- `_templates`\n'),
        ['_TEMPLATES', 'product'],
        ['zeta', 'WORK', 'Meetings', 'alpha', 'empty', '_drafts', '.obsidian', 'Product', '_templates'],
        async (folder) => {
            probed.push(folder);
            return folder !== 'empty';
        });

    // Ordinal and case-insensitive by upper case, so an underscore sorts after
    // the letters, as BrainConventions.Resolve orders them.
    assert.deepEqual(areas, [
        { name: 'Work', folder: 'work', order: 1 },
        { name: 'Missing', folder: 'missing', order: 2 },
        { name: 'Alpha', folder: 'alpha', order: 3 },
        { name: 'Meetings', folder: 'Meetings', order: 4 },
        { name: 'Zeta', folder: 'zeta', order: 5 },
        { name: 'Drafts', folder: '_drafts', order: 6 },
    ]);
    assert.deepEqual(probed.sort(), ['Meetings', '_drafts', 'alpha', 'empty', 'zeta']);
});

test('dotless ı keeps its case, as .NET ordinal ignore-case keeps it', async () => {
    const areas = await resolveAreas(declaredAreas('## Areas\n- `work`\n'), ['ILIK'], ['ılık', 'kitap'], async () => true);

    // `ı` (U+0131) is no `I` to .NET: `ılık` is not ignored by `ILIK`, and it
    // sorts after `kitap`, since U+0131 is above `K`.
    assert.deepEqual(areas.map((area) => area.folder), ['work', 'kitap', 'ılık']);
});

test('nothing declared puts the PARA set first; ignoring every declared area does not bring it back', async () => {
    assert.deepEqual(
        (await resolveAreas([], [], ['projects', 'meetings'], async () => true)).map((area) => area.folder),
        [...PARA_AREAS.map((area) => area.folder), 'meetings']);
    assert.deepEqual(await resolveAreas(declaredAreas('## Areas\n- `work`\n'), ['work'], ['work'], async () => true), []);
});

test('a top folder is an area once a note sits anywhere below it, outside dot-folders', async () => {
    const root = await brain({
        'CONVENTIONS.md': '## Areas\n\n- `_inbox`\n\n## Ignore\n\n- `_templates`\n',
        'README.md': '# Brain\n',
        'Meetings/2026/10/standup.md': '# Standup\n',
        'Demo/readme.txt': 'not a note',
        'trash-only/.trash/old.md': '# Old\n',
        'upper/NOTE.MD': '# Upper\n',
        '_templates/meeting.md': '# Meeting\n',
        '.obsidian/workspace.md': '# Workspace\n',
    });
    await mkdir(join(root, 'empty'));

    assert.deepEqual(await readBrainAreas(root), [
        { name: 'Inbox', folder: '_inbox', order: 1 },
        { name: 'Meetings', folder: 'Meetings', order: 2 },
    ]);
});

test('repo: links in prose are edges from the chapter that holds them, once each', () => {
    const markdown = [
        '# Plan',
        '[a](repo:r/a.md)',
        '## Step',
        '```meta',
        'status: draft',
        '```',
        '[b](repo:r/b.md) [b](repo:r/b.md)',
        '### Plain',
        '[c](<repo:r/c.md>)',
        '```',
        '[d](repo:r/d.md)',
        '```',
    ].join('\n');
    const chapters = [
        { level: 1, slug: 'plan', line: 1, meta: null },
        { level: 2, slug: 'step', line: 3, meta: { status: 'draft' } },
        { level: 3, slug: 'plain', line: 8, meta: null },
    ];

    assert.deepEqual(repoLinkEdges('p/plan.md', markdown, chapters).map((edge) => `${edge.source} -> ${edge.target}`), [
        'p/plan.md -> repo:r/a.md',
        'p/plan.md#step -> repo:r/b.md',
        'p/plan.md#step -> repo:r/c.md',
    ]);
});

test('a brain builds under its own convention, into the database it is told', async () => {
    const root = await brain({
        'README.md': '# My Brain\n',
        'CONVENTIONS.md': '## Areas\n\n- Work — `work`\n- `projects`\n- `absent`\n\n## Ignore\n\n- `_templates`\n',
        'work/standup.md': '# Standup\n',
        'projects/plan.md': [
            '# Plan',
            '',
            '```meta',
            'repos: [backlog]',
            'tasks: [t-1]',
            'related: [repo:backlog/.devbook/arc42/05.md#x, work/standup.md, work/missing.md]',
            '```',
            '',
            '[link](repo:backlog/.devbook/domain/x.md)',
        ].join('\n'),
        'loose/discovered.md': '# Discovered\n',
        '_templates/meeting.md': '# Meeting\n',
        '.obsidian/workspace.md': '# Workspace\n',
    });
    const target = join(scratch, 'brain.db');

    const generator = await loadBrainGenerator(root, await loadGenerator(REPO));
    await buildDatabase(root, target, generator);

    const db = new DatabaseSync(target);
    try {
        const all = (sql) => db.prepare(sql).all().map((row) => Object.values(row).join(' '));

        assert.deepEqual(all("SELECT name FROM outline_entry WHERE scope = '.' AND parent_id IS NULL ORDER BY ordinal"), ['work', 'projects', 'loose']);
        assert.deepEqual(all('SELECT id, folder FROM node ORDER BY id'), ['loose/discovered.md loose', 'projects/plan.md projects', 'work/standup.md work']);
        assert.deepEqual(all('SELECT node_id, name, value FROM node_attribute ORDER BY node_id, name, value'), [
            'loose/discovered.md para loose',
            'projects/plan.md para projects',
            'projects/plan.md repos backlog',
            'projects/plan.md tasks t-1',
            'work/standup.md para work',
        ]);
        assert.deepEqual(all('SELECT type, source, target FROM edge ORDER BY type, target'), [
            'related projects/plan.md repo:backlog/.devbook/arc42/05.md#x',
            'related projects/plan.md work/standup.md',
            'repo-link projects/plan.md repo:backlog/.devbook/domain/x.md',
        ]);
        assert.equal(all("SELECT count(*) FROM problem WHERE message LIKE '%no metadata rules apply%'")[0], '0');
    } finally {
        db.close();
    }

    // Nothing derived was written into the Brain.
    assert.deepEqual((await readdir(root)).sort(), ['.obsidian', 'CONVENTIONS.md', 'README.md', '_templates', 'loose', 'projects', 'work']);
});
