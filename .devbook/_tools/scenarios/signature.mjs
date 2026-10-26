// signature.mjs — the journey signature: what a derived spec records in its
// header and a run in `run.json`, so a reader can tell a run that proves
// today's journey from one that proves an older text. A journey is one `##`
// in a scenarios file; the text below is built from that journey alone.
//
// The canonical text, one line each, joined by "\n" with no trailing newline:
//
//   start: <start>                     only when set
//   actor: <entry>, <entry>            only when set, in written order
//   data: <name>@<hash>, …             only when set; <hash> is the data set's, below
//   profile: <name>                    always; absent reads as `default`
//   flags: <entry>, …                  only when set
//   settings: <entry>, …               only when set
//   shot: <label>                      a screenshot point above the journey's first part
//   part: <heading text>               each `###`, then, in document order inside it:
//   step: <Keyword> <text>               each step, keyword and text
//   shot: <label>                        each screenshot point
//
// Every value has its whitespace collapsed to single spaces. Captions, the
// journey's heading and lead, and prose between steps do not count: changing
// them changes no behaviour. The signature is sha256 over the UTF-8 text, first 8 hex.
// `scenario-page.vector.json` beside this file is the shared vector every
// implementation — this one, spec-manager's, Backlog's — is tested against.
//
// As a CLI it prints the signature a derived spec's `// signature:` line takes:
//
//   node .devbook/_tools/scenarios/signature.mjs <scenarios.md>[#<journey>]… [--root <dir>] [--scenario-folder <dir>]
//
// One `<signature>  <address>` line per journey: every journey in a file, or
// the one an address names. Exits 1 when a path is not a scenarios file or an
// address names no journey, 2 on a usage error.

import { createHash } from "node:crypto";
import { readdir, readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { collapse, dataFolder, normalizePath, OUTSIDE_ROOT, parseScenariosFile, resolveInside, SCENARIO_FOLDER, splitAddress } from "./parse.mjs";

/** What a data set hashes to when its folder does not exist. */
export const MISSING_DATA = "missing";

/**
 * The canonical text of a parsed journey. `dataHash(name)` gives each data
 * set's content hash; without it every data set reads as `missing`.
 */
export function canonicalText(journey, dataHash = () => MISSING_DATA) {
    const setup = journey.setup ?? {};
    const lines = [];
    const list = (key, values) => {
        if (values?.length) lines.push(`${key}: ${values.map(collapse).join(", ")}`);
    };
    if (setup.start) lines.push(`start: ${collapse(setup.start)}`);
    list("actor", setup.actor);
    list("data", (setup.data ?? []).map((name) => `${collapse(name)}@${dataHash(collapse(name))}`));
    lines.push(`profile: ${collapse(setup.profile ?? "default")}`);
    list("flags", setup.flags);
    list("settings", setup.settings);

    for (const shot of journey.shots.filter((s) => s.part === null)) lines.push(`shot: ${shot.label}`);
    for (const part of journey.parts) {
        lines.push(`part: ${collapse(part.title)}`);
        const entries = [
            ...part.steps.map((step) => ({ line: step.line, text: `step: ${collapse(step.keyword ? `${step.keyword} ${step.text}` : step.text)}` })),
            ...part.shots.map((shot) => ({ line: shot.line, text: `shot: ${shot.label}` })),
        ].sort((a, b) => a.line - b.line);
        for (const entry of entries) lines.push(entry.text);
    }
    return lines.join("\n");
}

/** The first 8 hex of sha256 over a string. */
export function shortHash(text) {
    return createHash("sha256").update(text, "utf8").digest("hex").slice(0, 8);
}

/** A parsed journey's signature. */
export function signatureOf(journey, dataHash) {
    return shortHash(canonicalText(journey, dataHash));
}

/**
 * A data set's content hash, from `{ relativePath: content }`: for each file in
 * sorted POSIX path order, the path, a newline, the content, and a newline,
 * through sha256, first 8 hex. Text content has CRLF read as LF, so a
 * checkout's line endings never change it; content holding a NUL byte is
 * hashed as the bytes it is.
 */
export function hashFiles(files) {
    const hash = createHash("sha256");
    for (const rel of Object.keys(files).sort()) {
        const content = files[rel];
        const bytes = Buffer.isBuffer(content) ? content : Buffer.from(String(content), "utf8");
        hash.update(`${rel}\n`, "utf8");
        hash.update(bytes.includes(0) ? bytes : Buffer.from(bytes.toString("utf8").replace(/\r\n/g, "\n"), "utf8"));
        hash.update("\n", "utf8");
    }
    return hash.digest("hex").slice(0, 8);
}

/** A data set folder's hash per `hashFiles`, or `missing` when the folder does not exist. */
export async function hashDataSet(folder) {
    const files = {};
    async function walk(rel) {
        const entries = await readdir(resolveInside(folder, rel), { withFileTypes: true });
        for (const entry of entries) {
            const child = rel ? `${rel}/${entry.name}` : entry.name;
            if (entry.isDirectory()) await walk(child);
            else if (entry.isFile()) files[child] = await readFile(resolveInside(folder, child));
        }
    }
    try {
        await walk("");
    } catch {
        return MISSING_DATA;
    }
    return hashFiles(files);
}

/**
 * A journey's signature with its data sets hashed from
 * `<repoRoot>/<scenarioFolder>/data/<name>/`. A name that resolves outside
 * that folder reads as `missing`.
 */
export async function journeySignature(journey, repoRoot, scenarioFolder) {
    const hashes = new Map();
    for (const name of journey.setup?.data ?? []) {
        const folder = dataFolder(repoRoot, scenarioFolder, collapse(name));
        hashes.set(collapse(name), folder ? await hashDataSet(folder) : MISSING_DATA);
    }
    return signatureOf(journey, (name) => hashes.get(name) ?? MISSING_DATA);
}

async function main(argv) {
    const options = { root: process.cwd(), scenarioFolder: SCENARIO_FOLDER, targets: [] };
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === "--root" && argv[i + 1]) options.root = argv[++i];
        else if (arg === "--scenario-folder" && argv[i + 1]) options.scenarioFolder = argv[++i];
        else if (arg.startsWith("--")) options.error = `unknown or incomplete argument: ${arg}`;
        else options.targets.push(arg);
    }
    if (options.error || !options.targets.length) {
        console.error(options.error ?? "usage: signature.mjs <scenarios.md>[#<journey>]… [--root <dir>] [--scenario-folder <dir>]");
        process.exitCode = 2;
        return;
    }
    const repoRoot = path.resolve(options.root);
    let failed = false;
    for (const given of options.targets) {
        const { path: givenPath, anchor } = splitAddress(given);
        let relPath = normalizePath(givenPath);
        let file;
        try {
            relPath = normalizePath(path.relative(repoRoot, resolveInside(repoRoot, givenPath)));
            file = parseScenariosFile(await readFile(resolveInside(repoRoot, givenPath), "utf8"), relPath);
        } catch (error) {
            const why = error.code === "ENOENT" ? "does not exist" : error.code === OUTSIDE_ROOT ? "resolves outside the repository" : error.message;
            console.error(`${relPath}: ${why}`);
            failed = true;
            continue;
        }
        if (!file.isScenarios) {
            console.error(`${relPath}: not a scenarios file — journeys live in scenarios.md or scenarios.<name>.md in a bounded context`);
            failed = true;
            continue;
        }
        const journeys = file.journeys.filter((journey) => journey.isScenario && (anchor === null || journey.slug === anchor));
        if (!journeys.length) {
            console.error(`${relPath}${anchor === null ? "" : `#${anchor}`}: names no journey — a journey is a \`##\` typed \`scenario\``);
            failed = true;
            continue;
        }
        for (const journey of journeys) console.log(`${await journeySignature(journey, repoRoot, options.scenarioFolder)}  ${journey.id}`);
    }
    process.exitCode = failed ? 1 : 0;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main(process.argv.slice(2));
