// Tests for generator.mjs and the generator it hands build-database.mjs, run
// with `node --test "tools/devbook/*.test.mjs"`.
//
// The fixture carries its own generator under `.devbook/_tools/devbook-meta/`,
// the way `devbook:init` materializes one: a copy of the modules this
// repository has installed there. What is under test is the plumbing — which
// generator is chosen, where the database goes, that its rows carry the
// repository's real paths — and that the outline is the convention's, whatever
// a stray `_reading-order.json` says.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { copyFile, mkdtemp, mkdir, readdir, rm, stat, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { fileURLToPath } from 'node:url';

import { buildDatabase } from './build-database.mjs';
import { loadGenerator, usesDevbookLayout } from './generator.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const INSTALLED_GENERATOR = resolve(HERE, '..', '..', '.devbook', '_tools', 'devbook-meta');

const FIXTURE = {
    '.devbook/config.json': '{}',
    '.devbook/domain/context-map.md': '# Context Map\n',
    '.devbook/domain/inbox/context.md': '# Inbox\n\n```meta\ntype: context\n```\n',
    '.devbook/domain/inbox/domain.md': '# Inbox\n\nQuick capture never blocks on a decision.\n',
    // Ignored: were it read, `domain.md` would be the context's root and first.
    '.devbook/domain/_reading-order.json': JSON.stringify({
        version: 1,
        scope: '.devbook/domain',
        directories: { '.devbook/domain/inbox': { root: 'domain.md', order: ['context.md'] } },
    }),
};

async function writeTree(files, { withGenerator = false } = {}) {
    const root = await mkdtemp(join(tmpdir(), 'devbook-layout-'));
    for (const [relPath, content] of Object.entries(files)) {
        const file = join(root, ...relPath.split('/'));
        await mkdir(dirname(file), { recursive: true });
        await writeFile(file, content, 'utf8');
    }
    if (withGenerator) {
        const target = join(root, '.devbook', '_tools', 'devbook-meta');
        await mkdir(target, { recursive: true });
        for (const name of await readdir(INSTALLED_GENERATOR)) {
            if (name.endsWith('.mjs') && !name.endsWith('.test.mjs')) {
                await copyFile(join(INSTALLED_GENERATOR, name), join(target, name));
            }
        }
    }
    return root;
}

const exists = async (target) => {
    try {
        await stat(target);
        return true;
    } catch {
        return false;
    }
};

test('a .devbook/ holding only configuration is the root layout, which is not built here', async () => {
    const root = await writeTree({ '.devbook/config.json': '{}', '.domain/domain.md': '# Domain\n' });
    try {
        assert.equal(await usesDevbookLayout(root), false);
        await assert.rejects(loadGenerator(root), /root layout/);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});

test('a devbook-layout repository uses its own generator, outline and annotation index included', async () => {
    const root = await writeTree(FIXTURE, { withGenerator: true });
    try {
        const generator = await loadGenerator(root);

        assert.equal(generator.layout, 'devbook');
        assert.equal(generator.source, '.devbook/_tools/devbook-meta');
        assert.equal(generator.repoReadingOrderPath, undefined, 'nothing names a reading-order file any more');
        for (const name of ['buildGraph', 'discoverScopes', 'parseDocument', 'folderKindForPath',
            'buildOutlineDocument', 'collectAnnotations', 'openCountsByAddress']) {
            assert.equal(typeof generator[name], 'function', `${name} is not loaded`);
        }
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});

test('a devbook-layout repository without a generator says how to get one', async () => {
    const root = await writeTree({ '.devbook/domain/domain.md': '# Domain\n' });
    try {
        await assert.rejects(loadGenerator(root), /devbook:init/);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});

test('a generator directory missing the outline module is not a generator', async () => {
    const root = await writeTree({ '.devbook/domain/domain.md': '# Domain\n' });
    const partial = await mkdtemp(join(tmpdir(), 'devbook-partial-'));
    try {
        for (const name of ['graph.mjs', 'metadata.mjs']) {
            await copyFile(join(INSTALLED_GENERATOR, name), join(partial, name));
        }
        await assert.rejects(loadGenerator(root, { generatorDir: partial }), /outline\.mjs/);
    } finally {
        await rm(root, { recursive: true, force: true });
        await rm(partial, { recursive: true, force: true });
    }
});

test('the devbook-layout database carries the repository\'s real paths and the convention order', async () => {
    const root = await writeTree(FIXTURE, { withGenerator: true });
    // Local ADR 0015: the database lives outside the repository, wherever the
    // caller says, and the build writes nothing into the tree it read.
    const outside = await mkdtemp(join(tmpdir(), 'devbook-out-'));
    const target = join(outside, 'devbook.db');
    try {
        await buildDatabase(root, target);

        assert.equal(await exists(target), true);
        assert.equal(await exists(join(root, '_meta')), false, 'nothing is written at the root');
        assert.equal(await exists(join(root, '.devbook', '_meta')), false, 'nothing is written under .devbook/');

        const db = new DatabaseSync(target, { readOnly: true });
        try {
            const scopes = db.prepare('SELECT DISTINCT scope FROM outline_entry ORDER BY scope').all().map((row) => row.scope);
            assert.deepEqual(scopes, ['.', '.devbook/domain']);

            const chapter = db.prepare("SELECT path, folder FROM chapter WHERE path LIKE '%/inbox/domain.md'").get();
            assert.equal(chapter.path, '.devbook/domain/inbox/domain.md');
            assert.equal(chapter.folder, 'domain');

            const inbox = db.prepare(`
                SELECT e.name, e.is_root, e.kind FROM outline_entry e JOIN outline_entry p ON p.id = e.parent_id
                WHERE e.scope = '.devbook/domain' AND p.name = 'inbox' ORDER BY e.ordinal
            `).all().map((row) => ({ ...row }));
            assert.deepEqual(inbox.map((row) => row.name), ['context.md', 'domain.md']);
            assert.deepEqual(inbox.map((row) => row.is_root), [1, 0]);
            assert.equal(inbox[0].kind, 'context');

            const area = db.prepare("SELECT name, kind FROM outline_entry WHERE scope = '.' AND type = 'area'").get();
            assert.deepEqual({ ...area }, { name: '.devbook/domain', kind: 'domain' });
        } finally {
            db.close();
        }
    } finally {
        await rm(root, { recursive: true, force: true });
        await rm(outside, { recursive: true, force: true });
    }
});
