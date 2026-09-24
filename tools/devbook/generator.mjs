// generator.mjs — which devbook generator indexes a repository, and where the
// database it feeds goes.
//
// A repository keeps its knowledge folders in one of two layouts. The root
// layout puts each at the root as a dot-folder (`.arc42`, `.domain`, …) and its
// rollup in `_meta/`; this repository is still on it, and the generator for it
// is the installed copy under `.github/tools/knowledge-meta/`, which CLAUDE.md
// says never to edit here. The devbook layout nests the five folders under one
// parent (`.devbook/arc42`, `.devbook/domain`, …) with the rollup in
// `.devbook/_meta/`; the generator for it is the one the devbook plugin
// materializes into the repository itself, at `.devbook/_tools/devbook-meta/`.
//
// The two generators export the same seam `build-database.mjs` and
// `reading-order.mjs` import — `buildGraph`, `discoverScopes`, `REPO_SCOPE`,
// `parseDocument`, `folderKindForPath` — and each spells paths the way its own
// layout does. So the database is built by whichever one the repository's
// layout calls for, and its rows carry that repository's real paths.

import { stat } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

import * as rootGraph from '../../.github/tools/knowledge-meta/graph.mjs';
import * as rootMetadata from '../../.github/tools/knowledge-meta/metadata.mjs';
import { DATABASE_PATH, DEVBOOK_DATABASE_PATH } from './devbook-schema.mjs';

/** The parent every folder sits under in the devbook layout. */
export const DEVBOOK_ROOT = '.devbook';

/** The folders the devbook layout recognizes under `.devbook/`. */
export const DEVBOOK_FOLDER_NAMES = ['arc42', 'domain', 'tech', 'design', 'ai'];

/** Where a devbook-layout repository's generator is looked for, first match
 *  wins: where `devbook:init` materializes it, then where the repository that
 *  authors the convention keeps its source. */
export const GENERATOR_LOCATIONS = [
    `${DEVBOOK_ROOT}/_tools/devbook-meta`,
    'plugins/devbook/tools/devbook-meta',
];

async function isDirectory(absolute) {
    try {
        return (await stat(absolute)).isDirectory();
    } catch {
        return false;
    }
}

async function isFile(absolute) {
    try {
        return (await stat(absolute)).isFile();
    } catch {
        return false;
    }
}

/** Whether any knowledge folder of `repoRoot` sits under `.devbook/`. A
 *  `.devbook/` holding only configuration does not count — that is a
 *  repository wired for the tooling whose chapters have not moved yet. */
export async function usesDevbookLayout(repoRoot) {
    for (const name of DEVBOOK_FOLDER_NAMES) {
        if (await isDirectory(path.join(repoRoot, DEVBOOK_ROOT, name))) return true;
    }
    return false;
}

/** The repo-relative database path for a layout. */
export function databasePathFor(layout) {
    return layout === 'devbook' ? DEVBOOK_DATABASE_PATH : DATABASE_PATH;
}

/** The repo-relative path of the repository scope's committed reading order —
 *  the file that orders the areas themselves — beside the areas in either
 *  layout. */
export function repoReadingOrderPathFor(layout) {
    return layout === 'devbook' ? `${DEVBOOK_ROOT}/_reading-order.json` : '_reading-order.json';
}

/** The directory holding a devbook-layout repository's generator, or null. */
async function findGeneratorDirectory(repoRoot, override) {
    const candidates = override ? [path.resolve(override)] : GENERATOR_LOCATIONS.map((relative) => path.join(repoRoot, relative));
    for (const candidate of candidates) {
        if (await isFile(path.join(candidate, 'graph.mjs')) && await isFile(path.join(candidate, 'metadata.mjs'))) {
            return candidate;
        }
    }
    return null;
}

/**
 * The generator for `repoRoot`, as one object: the layout it serves, the
 * database path and repository reading-order path that go with it, and the
 * five functions and constants the database build uses.
 *
 * `generatorDir` overrides where a devbook-layout generator is looked for; it
 * has no effect on a root-layout repository, which always uses the installed
 * copy this file imports.
 */
export async function loadGenerator(repoRoot, { generatorDir = null } = {}) {
    if (!(await usesDevbookLayout(repoRoot))) {
        return {
            layout: 'root',
            source: '.github/tools/knowledge-meta',
            databasePath: databasePathFor('root'),
            repoReadingOrderPath: repoReadingOrderPathFor('root'),
            folders: rootGraph.KNOWLEDGE_FOLDERS,
            REPO_SCOPE: rootGraph.REPO_SCOPE,
            buildGraph: rootGraph.buildGraph,
            discoverScopes: rootGraph.discoverScopes,
            parseDocument: rootMetadata.parseDocument,
            folderKindForPath: rootMetadata.folderKindForPath,
        };
    }

    const directory = await findGeneratorDirectory(repoRoot, generatorDir);
    if (!directory) {
        throw new Error(
            `${repoRoot} keeps its devbook under ${DEVBOOK_ROOT}/, and no devbook generator was found at `
            + `${(generatorDir ? [generatorDir] : GENERATOR_LOCATIONS).join(' or ')}. `
            + 'Run devbook:init in that repository, or pass --generator <directory holding graph.mjs>.'
        );
    }

    const graph = await import(pathToFileURL(path.join(directory, 'graph.mjs')).href);
    const metadata = await import(pathToFileURL(path.join(directory, 'metadata.mjs')).href);

    return {
        layout: 'devbook',
        source: path.relative(repoRoot, directory).split(path.sep).join('/') || directory,
        databasePath: databasePathFor('devbook'),
        repoReadingOrderPath: repoReadingOrderPathFor('devbook'),
        folders: DEVBOOK_FOLDER_NAMES.map((name) => `${DEVBOOK_ROOT}/${name}`),
        REPO_SCOPE: graph.REPO_SCOPE,
        buildGraph: (root) => graph.buildGraph(root),
        discoverScopes: graph.discoverScopes,
        parseDocument: metadata.parseDocument,
        folderKindForPath: metadata.folderKindForPath,
    };
}
