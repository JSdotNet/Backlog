// generator.mjs — which devbook generator indexes a repository.
//
// A repository keeps its devbook folders under one parent (`.devbook/arc42`,
// `.devbook/domain`, …), and the generator that indexes them is the one the
// devbook plugin materializes into the repository itself, at
// `.devbook/_tools/devbook-meta/`. This file finds it and imports the seam the
// database build uses: `buildGraph`, `discoverScopes` and `REPO_SCOPE` from
// `graph.mjs`, `parseDocument` and `folderKindForPath` from `metadata.mjs`,
// `buildOutlineDocument` from `outline.mjs`, and `collectAnnotations` and
// `openCountsByAddress` from `annotations-index.mjs`. The rows carry the paths
// that generator spells, which are the repository's real ones.
//
// The older root layout — each folder a dot-folder at the root (`.arc42`,
// `.domain`, …) — is not built here any more. Its generator is the predecessor
// copy under `.github/tools/knowledge-meta/`, which derives neither the
// convention reading order (it carried the order forward from a committed
// `_meta/index.json`) nor the annotation index, so it cannot produce the
// database this schema describes. The app still serves such a repository with
// the same rules and that layout's folder names; nothing holds that to a Node
// reference, and `DevbookBuilderParityTests` builds this repository's own
// `.devbook/` only. Where the database goes is not this file's question either:
// local ADR 0015 moved it out of every repository, and `build-database.mjs`
// writes where `--out` says.

import { stat } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

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

/** The generator modules a directory has to hold to count as one. */
const GENERATOR_MODULES = ['graph.mjs', 'metadata.mjs', 'outline.mjs', 'annotations-index.mjs'];

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

/** Whether any devbook folder of `repoRoot` sits under `.devbook/`. A
 *  `.devbook/` holding only configuration does not count — that is a
 *  repository wired for the tooling whose chapters have not moved yet. */
export async function usesDevbookLayout(repoRoot) {
    for (const name of DEVBOOK_FOLDER_NAMES) {
        if (await isDirectory(path.join(repoRoot, DEVBOOK_ROOT, name))) return true;
    }
    return false;
}

/** The directory holding a devbook-layout repository's generator, or null. */
async function findGeneratorDirectory(repoRoot, override) {
    const candidates = override ? [path.resolve(override)] : GENERATOR_LOCATIONS.map((relative) => path.join(repoRoot, relative));
    for (const candidate of candidates) {
        let complete = true;
        for (const module of GENERATOR_MODULES) {
            if (!(await isFile(path.join(candidate, module)))) {
                complete = false;
                break;
            }
        }
        if (complete) return candidate;
    }
    return null;
}

/**
 * The generator for `repoRoot`, as one object: where it was found, the folders
 * it recognizes, and the functions and constants the database build uses.
 *
 * `generatorDir` overrides where the generator is looked for. A repository that
 * keeps no folder under `.devbook/` is refused: see the head of this file.
 */
export async function loadGenerator(repoRoot, { generatorDir = null } = {}) {
    if (!(await usesDevbookLayout(repoRoot))) {
        throw new Error(
            `${repoRoot} keeps no devbook folder under ${DEVBOOK_ROOT}/. The root layout (.arc42, .domain, …) is `
            + 'not built by this tool any more; move the folders with devbook:update and build again.'
        );
    }

    const directory = await findGeneratorDirectory(repoRoot, generatorDir);
    if (!directory) {
        throw new Error(
            `${repoRoot} keeps its devbook under ${DEVBOOK_ROOT}/, and no devbook generator was found at `
            + `${(generatorDir ? [generatorDir] : GENERATOR_LOCATIONS).join(' or ')}. `
            + `Run devbook:init in that repository, or pass --generator <directory holding ${GENERATOR_MODULES.join(', ')}>.`
        );
    }

    const load = (module) => import(pathToFileURL(path.join(directory, module)).href);
    const graph = await load('graph.mjs');
    const metadata = await load('metadata.mjs');
    const outline = await load('outline.mjs');
    const annotations = await load('annotations-index.mjs');

    return {
        layout: 'devbook',
        source: path.relative(repoRoot, directory).split(path.sep).join('/') || directory,
        folders: DEVBOOK_FOLDER_NAMES.map((name) => `${DEVBOOK_ROOT}/${name}`),
        REPO_SCOPE: graph.REPO_SCOPE,
        buildGraph: (root) => graph.buildGraph(root),
        discoverScopes: graph.discoverScopes,
        parseDocument: metadata.parseDocument,
        folderKindForPath: metadata.folderKindForPath,
        buildOutlineDocument: outline.buildOutlineDocument,
        collectAnnotations: annotations.collectAnnotations,
        openCountsByAddress: annotations.openCountsByAddress,
    };
}
