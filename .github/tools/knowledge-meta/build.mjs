#!/usr/bin/env node
// build.mjs — CLI wrapper that writes the derived knowledge metadata artifacts.
//
//   node .github/tools/knowledge-meta/build.mjs           # all scopes
//   node .github/tools/knowledge-meta/build.mjs --check   # CI: verify only, write nothing
//   node .github/tools/knowledge-meta/build.mjs --scope .tech
//
// Writes two artifacts per scope, per
// `.github/instructions/derived-artifacts.instructions.md`:
//
//   _meta/graph.json          the reference graph (repository-wide rollup)
//   _meta/index.json          the ordered reading outline
//   .tech/_meta/graph.json    the same pair, scoped to .tech
//   ...one pair per knowledge folder
//
// Graph construction lives in graph.mjs, which the knowledge-graph canvas also
// imports, so the written indexes and the live view are always the same graph.

import { writeFile, mkdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { buildGraph, buildGraphDocument, outputPathFor, SCOPES } from "./graph.mjs";
import { buildOutlineDocument, outlinePathFor } from "./outline.mjs";

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

async function emit(outPath, document, summary) {
    if (!checkOnly) {
        const absoluteOut = path.resolve(REPO_ROOT, outPath);
        await mkdir(path.dirname(absoluteOut), { recursive: true });
        await writeFile(absoluteOut, `${JSON.stringify(document, null, 2)}\n`, "utf8");
    }
    console.log(`${checkOnly ? "checked" : "wrote  "} ${outPath.padEnd(26)} ${summary}`);
    for (const problem of document.problems) {
        console.log(`  [${problem.severity}] ${problem.message}`);
        if (problem.severity === "error") errorCount++;
    }
}

function countFiles(entries) {
    return entries.reduce(
        (acc, entry) => acc + (entry.type === "file" ? 1 : countFiles(entry.children ?? [])),
        0
    );
}

for (const scope of scopes) {
    const graphDocument = await buildGraphDocument(REPO_ROOT, scope, graph);
    const { stats } = graphDocument;
    await emit(
        outputPathFor(scope),
        graphDocument,
        `${String(stats.nodes).padStart(4)} nodes, ${String(stats.edges).padStart(4)} edges`
    );

    const outlineDocument = await buildOutlineDocument(REPO_ROOT, scope);
    await emit(
        outlinePathFor(scope),
        outlineDocument,
        `${String(countFiles(outlineDocument.entries)).padStart(4)} files ordered`
    );
}

if (errorCount) {
    console.error(`\n${errorCount} problem(s) at error severity.`);
    process.exit(1);
}
