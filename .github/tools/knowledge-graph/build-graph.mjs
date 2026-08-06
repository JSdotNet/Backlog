#!/usr/bin/env node
// build-graph.mjs — CLI wrapper that writes the derived knowledge graph indexes.
//
//   node .github/tools/knowledge-graph/build-graph.mjs           # all scopes
//   node .github/tools/knowledge-graph/build-graph.mjs --check   # CI: verify only, write nothing
//   node .github/tools/knowledge-graph/build-graph.mjs --scope .tech
//
// Writes one artifact per scope, per
// `.github/instructions/derived-index.instructions.md`:
//
//   .index/graph.json          (repository-wide rollup)
//   .tech/.index/graph.json    (scoped to .tech)
//   ...one per knowledge folder
//
// Graph construction lives in graph.mjs, which the knowledge-graph canvas also
// imports, so the written indexes and the live view are always the same graph.

import { writeFile, mkdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { buildGraph, buildGraphDocument, outputPathFor, SCOPES } from "./graph.mjs";

const REPO_ROOT = path.resolve(fileURLToPath(new URL("../../../", import.meta.url)));

const args = process.argv.slice(2);
const checkOnly = args.includes("--check");
const scopeIndex = args.indexOf("--scope");
const requestedScope = scopeIndex !== -1 ? args[scopeIndex + 1] : null;

if (requestedScope && !SCOPES.includes(requestedScope)) {
    console.error(`Unknown scope "${requestedScope}". Known scopes: ${SCOPES.join(", ")}`);
    process.exit(2);
}

const scopes = requestedScope ? [requestedScope] : SCOPES;

// Parse the corpus once and project it per scope.
const graph = await buildGraph(REPO_ROOT);
let errorCount = 0;

for (const scope of scopes) {
    const document = await buildGraphDocument(REPO_ROOT, scope, graph);
    const outPath = outputPathFor(scope);

    if (!checkOnly) {
        const absoluteOut = path.resolve(REPO_ROOT, outPath);
        await mkdir(path.dirname(absoluteOut), { recursive: true });
        await writeFile(absoluteOut, `${JSON.stringify(document, null, 2)}\n`, "utf8");
    }

    const { stats, problems } = document;
    console.log(
        `${checkOnly ? "checked" : "wrote  "} ${outPath.padEnd(26)} ${String(stats.nodes).padStart(4)} nodes, ${String(stats.edges).padStart(4)} edges`
    );
    for (const problem of problems) {
        console.log(`  [${problem.severity}] ${problem.message}`);
        if (problem.severity === "error") errorCount++;
    }
}

if (errorCount) {
    console.error(`\n${errorCount} broken reference(s).`);
    process.exit(1);
}
