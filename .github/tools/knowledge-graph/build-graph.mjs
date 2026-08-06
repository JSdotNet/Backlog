#!/usr/bin/env node
// build-graph.mjs — CLI wrapper that writes the derived knowledge graph index.
//
//   node .github/tools/knowledge-graph/build-graph.mjs
//   node .github/tools/knowledge-graph/build-graph.mjs --check       # CI: fail on broken refs, write nothing
//   node .github/tools/knowledge-graph/build-graph.mjs --out <path>
//
// Graph construction lives in graph.mjs, which the knowledge-graph canvas also
// imports, so the written index and the live view are always the same graph.

import { writeFile, mkdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { buildGraphDocument, KNOWLEDGE_FOLDERS } from "./graph.mjs";

const REPO_ROOT = path.resolve(fileURLToPath(new URL("../../../", import.meta.url)));
const DEFAULT_OUT = ".index/knowledge-graph.json";

const args = process.argv.slice(2);
const checkOnly = args.includes("--check");
const outIndex = args.indexOf("--out");
const outPath = outIndex !== -1 ? args[outIndex + 1] : DEFAULT_OUT;

const document = await buildGraphDocument(REPO_ROOT);
const { stats, problems } = document;

if (!checkOnly) {
    const absoluteOut = path.resolve(REPO_ROOT, outPath);
    await mkdir(path.dirname(absoluteOut), { recursive: true });
    await writeFile(absoluteOut, `${JSON.stringify(document, null, 2)}\n`, "utf8");
    console.log(`Wrote ${outPath}`);
}

console.log(`${stats.nodes} nodes, ${stats.edges} edges across ${KNOWLEDGE_FOLDERS.join(", ")}`);
for (const problem of problems) {
    console.log(`  [${problem.severity}] ${problem.message}`);
}

const errors = problems.filter((p) => p.severity === "error");
if (errors.length) {
    console.error(`\n${errors.length} broken reference(s).`);
    process.exit(1);
}
