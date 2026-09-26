// Tests for generator.mjs and the devbook-layout path through
// build-database.mjs, run with `node --test tools/devbook`.
//
// The devbook-layout fixture carries its own generator under
// `.devbook/_tools/devbook-meta/`, the way `devbook:init` materializes one. It
// is a stand-in rather than the devbook plugin's real generator, which this
// repository does not vendor: it re-exports the installed parser and spells
// every path under `.devbook/`, which is all the database build asks of it. What
// is under test is the plumbing — which generator is chosen, where the database
// goes, and that its rows carry the repository's real paths.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, rm, stat, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { fileURLToPath, pathToFileURL } from 'node:url';

import { buildDatabase } from './build-database.mjs';
import { loadGenerator, usesDevbookLayout } from './generator.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const INSTALLED_METADATA = pathToFileURL(resolve(HERE, '..', '..', '.github', 'tools', 'knowledge-meta', 'metadata.mjs')).href;

const STAND_IN_METADATA = `
export { parseDocument } from ${JSON.stringify(INSTALLED_METADATA)};
export function folderKindForPath(relPath) {
    const match = /^\\.devbook\\/(arc42|domain|tech|design|ai)\\//.exec(String(relPath).replace(/\\\\/g, '/'));
    return match ? match[1] : null;
}
`;

const STAND_IN_GRAPH = `
import { stat } from 'node:fs/promises';
import path from 'node:path';
export const REPO_SCOPE = '.';
const NAMES = ['arc42', 'domain', 'tech', 'design', 'ai'];
export async function discoverScopes(repoRoot) {
    const folders = [];
    for (const name of NAMES) {
        try {
            if ((await stat(path.join(repoRoot, '.devbook', name))).isDirectory()) folders.push('.devbook/' + name);
        } catch {}
    }
    return folders.length ? ['.', ...folders] : [];
}
export async function buildGraph() {
    const id = '.devbook/domain/inbox/domain.md';
    return { nodes: [{ id, type: 'file', label: 'Inbox', folder: 'domain', path: id }], edges: [], problems: [] };
}
`;

const FIXTURE = {
    '.devbook/config.json': '{}',
    '.devbook/_tools/devbook-meta/graph.mjs': STAND_IN_GRAPH,
    '.devbook/_tools/devbook-meta/metadata.mjs': STAND_IN_METADATA,
    '.devbook/domain/inbox/domain.md': '# Inbox\n\nQuick capture never blocks on a decision.\n',
};

async function writeTree(files) {
    const root = await mkdtemp(join(tmpdir(), 'devbook-layout-'));
    for (const [relPath, content] of Object.entries(files)) {
        const file = join(root, ...relPath.split('/'));
        await mkdir(dirname(file), { recursive: true });
        await writeFile(file, content, 'utf8');
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

test('a .devbook/ holding only configuration is still the root layout', async () => {
    const root = await writeTree({ '.devbook/config.json': '{}', '.domain/domain.md': '# Domain\n' });
    try {
        assert.equal(await usesDevbookLayout(root), false);

        const generator = await loadGenerator(root);
        assert.equal(generator.layout, 'root');
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});

test('a devbook-layout repository uses its own generator', async () => {
    const root = await writeTree(FIXTURE);
    try {
        const generator = await loadGenerator(root);

        assert.equal(generator.layout, 'devbook');
        assert.equal(generator.source, '.devbook/_tools/devbook-meta');
        assert.equal(generator.repoReadingOrderPath, '.devbook/_reading-order.json');
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

test('the devbook-layout database carries the repository\'s real paths', async () => {
    const root = await writeTree(FIXTURE);
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

            const chapter = db.prepare('SELECT path, folder FROM chapter').get();
            assert.equal(chapter.path, '.devbook/domain/inbox/domain.md');
            assert.equal(chapter.folder, 'domain');

            const file = db.prepare("SELECT path, kind FROM outline_entry WHERE scope = '.devbook/domain' AND type = 'file'").get();
            assert.equal(file.path, '.devbook/domain/inbox/domain.md');
            assert.equal(file.kind, 'domain');

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
