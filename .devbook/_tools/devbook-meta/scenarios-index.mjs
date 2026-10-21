// scenarios-index.mjs — derives the scenario register from the graph
// `buildGraph` already built.
//
// A journey is one end-to-end walk: a `##` typed `scenario` in a scenarios
// file, whose `###` parts owe no block and so are no nodes in the graph. A
// reader that wants the journeys — spec-manager's tree, Backlog's acceptance
// checklist — would otherwise parse every scenarios file, and every
// requirements file for the `Proved by:` lines pointing at a part. This is
// that list, built by the scenario pass the check already runs
// (`scenario.mjs`), so it costs no second read of the corpus.
//
// Its `schemaVersion` is the register's own, starting at 1, not the contract
// version graph.json carries: its readers live outside this repository and
// pin it, and a contract bump that leaves this shape alone must not break
// them. The same reasoning as `naming.json`, per the checks-and-indexes
// decision in the repository's devbook. Version 2 lists journeys, keyed by
// their address `<scenarios file>#<journey>`, where version 1 listed pages by
// stem.

import { REPO_SCOPE, DEVBOOK_ROOT, generatorPath } from "./graph.mjs";

export const SCENARIOS_SCHEMA_VERSION = 2;

/**
 * Build the serializable scenario register for one scope, following the
 * derived-artifacts convention. Journeys live in `domain/` alone, so every
 * other folder writes an empty register and every `_meta/` folder holds the
 * same files.
 *
 * `problems` carries the graph's problems on the files the listed journeys sit
 * in — a setup field that does not resolve, an unclaimed part, a journey in the
 * wrong context — so a reader can show why a journey reads oddly without
 * running the check.
 */
export function buildScenariosDocument(repoRoot, scope, graph, folders) {
    const roots = scope === REPO_SCOPE ? folders : [scope];
    const inScope = (relPath) => roots.some((root) => relPath.startsWith(`${root}/`));
    const journeys = (graph.scenarios ?? []).filter((journey) => inScope(journey.path));
    const listed = new Set(journeys.map((journey) => journey.path));

    return {
        schemaVersion: SCENARIOS_SCHEMA_VERSION,
        generatedBy: generatorPath(repoRoot),
        scope,
        sources: roots,
        // Deliberately no timestamp, like the others: re-running the generator
        // reproduces the file byte for byte.
        stats: {
            journeys: journeys.length,
            parts: journeys.reduce((total, journey) => total + journey.parts.length, 0),
            labels: journeys.reduce((total, journey) => total + journey.labels.length, 0),
            unclaimed: journeys.reduce(
                (total, journey) => total + journey.parts.filter((part) => !part.provedBy.length).length,
                0
            ),
        },
        problems: (graph.problems ?? []).filter((problem) => problem.path && listed.has(problem.path)),
        scenarios: journeys,
    };
}

/** Repo-relative output path for a scope, per the derived-artifacts convention. */
export function scenariosPathFor(scope) {
    return scope === REPO_SCOPE ? `${DEVBOOK_ROOT}/_meta/scenarios.json` : `${scope}/_meta/scenarios.json`;
}
