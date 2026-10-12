// scenarios-index.mjs — derives the scenario register from the graph
// `buildGraph` already built.
//
// A scenario page is one end-to-end journey: a `domain/` file typed
// `scenario`, whose `##` parts owe no block and so are no nodes in the graph.
// A reader that wants the journeys — spec-manager's tree, Backlog's
// acceptance checklist — would otherwise parse every page, and every
// requirements file for the `Proved by:` lines pointing at a part. This is
// that list, built by the scenario pass the check already runs
// (`scenario.mjs`), so it costs no second read of the corpus.
//
// Its `schemaVersion` is the register's own, starting at 1, not the contract
// version graph.json carries: its readers live outside this repository and
// pin it, and a contract bump that leaves this shape alone must not break
// them. The same reasoning as `naming.json`, per the checks-and-indexes
// decision in the repository's devbook.

import { REPO_SCOPE, DEVBOOK_ROOT, generatorPath } from "./graph.mjs";

export const SCENARIOS_SCHEMA_VERSION = 1;

/**
 * Build the serializable scenario register for one scope, following the
 * derived-artifacts convention. Scenario pages live in `domain/` alone, so
 * every other folder writes an empty register and every `_meta/` folder holds
 * the same files.
 *
 * `problems` carries the graph's problems on the listed pages — a duplicate
 * stem, a setup field that does not resolve, an unclaimed part — so a reader
 * can show why a page reads oddly without running the check.
 */
export function buildScenariosDocument(repoRoot, scope, graph, folders) {
    const roots = scope === REPO_SCOPE ? folders : [scope];
    const inScope = (relPath) => roots.some((root) => relPath.startsWith(`${root}/`));
    const pages = (graph.scenarios ?? []).filter((page) => inScope(page.path));
    const listed = new Set(pages.map((page) => page.path));

    return {
        schemaVersion: SCENARIOS_SCHEMA_VERSION,
        generatedBy: generatorPath(repoRoot),
        scope,
        sources: roots,
        // Deliberately no timestamp, like the others: re-running the generator
        // reproduces the file byte for byte.
        stats: {
            pages: pages.length,
            parts: pages.reduce((total, page) => total + page.parts.length, 0),
            labels: pages.reduce((total, page) => total + page.labels.length, 0),
            unclaimed: pages.reduce(
                (total, page) => total + page.parts.filter((part) => !part.provedBy.length).length,
                0
            ),
        },
        problems: (graph.problems ?? []).filter((problem) => problem.path && listed.has(problem.path)),
        scenarios: pages,
    };
}

/** Repo-relative output path for a scope, per the derived-artifacts convention. */
export function scenariosPathFor(scope) {
    return scope === REPO_SCOPE ? `${DEVBOOK_ROOT}/_meta/scenarios.json` : `${scope}/_meta/scenarios.json`;
}
