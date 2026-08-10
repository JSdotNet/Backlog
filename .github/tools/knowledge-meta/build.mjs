#!/usr/bin/env node
// build.mjs — CLI wrapper that writes the derived knowledge metadata artifacts.
//
//   node .github/tools/knowledge-meta/build.mjs           # every adopted scope
//   node .github/tools/knowledge-meta/build.mjs --check   # CI: verify only, write nothing
//   node .github/tools/knowledge-meta/build.mjs --scope .tech
//   node .github/tools/knowledge-meta/build.mjs --root ../other-repo
//
// Writes two artifacts per scope, per the derived-artifacts convention:
//
//   _meta/graph.json          the reference graph (repository-wide rollup)
//   _meta/index.json          the ordered reading outline
//   .tech/_meta/graph.json    the same pair, scoped to .tech
//   ...one pair per knowledge folder the repository actually has
//
// Only folders present in the repository produce a scope, so a repository that
// adopts just .domain and .arc42 never grows _meta folders for the rest.
//
// Graph construction lives in graph.mjs, which the knowledge-graph canvas also
// imports, so the written indexes and the live view are always the same graph.

import { writeFile, mkdir } from "node:fs/promises";
import path from "node:path";
import {
    buildGraph,
    buildGraphDocument,
    outputPathFor,
    discoverScopes,
    KNOWLEDGE_FOLDERS,
    REPO_SCOPE,
} from "./graph.mjs";
import { buildOutlineDocument, outlinePathFor } from "./outline.mjs";

const args = process.argv.slice(2);
const checkOnly = args.includes("--check");

function optionValue(name) {
    const index = args.indexOf(name);
    return index !== -1 ? args[index + 1] : null;
}

// Defaults to the working directory, which is the repository root in CI and in
// the documented usage above. `--root` keeps the generator usable from anywhere.
const REPO_ROOT = path.resolve(optionValue("--root") ?? process.cwd());
const requestedScope = optionValue("--scope");

const availableScopes = await discoverScopes(REPO_ROOT);

if (!availableScopes.length) {
    console.error(
        `No knowledge folders found under ${REPO_ROOT}. ` +
            `Expected at least one of: ${KNOWLEDGE_FOLDERS.join(", ")}.`
    );
    process.exit(2);
}

if (requestedScope && !availableScopes.includes(requestedScope)) {
    console.error(
        `Unknown or unadopted scope "${requestedScope}". ` +
            `Available scopes: ${availableScopes.join(", ")}.`
    );
    process.exit(2);
}

const scopes = requestedScope ? [requestedScope] : availableScopes;
const folders = availableScopes.filter((scope) => scope !== REPO_SCOPE);

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
    const graphDocument = await buildGraphDocument(REPO_ROOT, scope, graph, folders);
    const { stats } = graphDocument;
    await emit(
        outputPathFor(scope),
        graphDocument,
        `${String(stats.nodes).padStart(4)} nodes, ${String(stats.edges).padStart(4)} edges`
    );

    const outlineDocument = await buildOutlineDocument(REPO_ROOT, scope, folders);
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
