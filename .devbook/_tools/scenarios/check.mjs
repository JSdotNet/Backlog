#!/usr/bin/env node
// check.mjs — the coverage check: do the specs still match the journeys?
//
// Each finding names what drifted, where, and how to fix it:
//   journey-without-spec   a journey no spec header names: it is never run
//   spec-without-journey   a spec header names an address that is no journey
//   signature-mismatch     the journey changed since its spec was derived
//   label-drift            the spec's shot() labels and the journey's shot: labels differ
//   image-in-steps         a shot: image sits inside a step list
//   bad-label              a label that is not [a-z0-9-]+, or used twice in a file
//   shot-outside-journey   a shot: image above a file's first journey
//   duplicate-anchor       two journey or part headings in one file share an anchor
//   dangling-reference     a scenario:<file>#<label> image names no file or no label
//   unknown-profile        the profile a journey runs under is not in profiles.json
//   legacy-page            a whole file typed scenario, the shape before contract 32
//
// Reads files only — no browser, no application, no build. The journey is the
// source: fix a finding by bringing the spec to the journey, never the reverse.
//
//   node .devbook/_tools/scenarios/check.mjs [--root <dir>] [--specs <dir>]… [--scenario-folder <dir>] [--json]
//
// Exits 0 when nothing is found, 1 when something is, 2 on a usage error.

import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
    DOMAIN_FOLDER,
    labelsOf,
    listDevbookMarkdown,
    listLegacyPages,
    listScenarioFiles,
    listSpecs,
    normalizePath,
    PROFILES_FILE,
    readProfiles,
    resolveInside,
    SCENARIO_FOLDER,
    specLabels,
} from "./parse.mjs";
import { journeySignature } from "./signature.mjs";

const FIXES = {
    "image-in-steps": "leave a blank line between the last step and the image",
    "bad-label": "give the screenshot point a [a-z0-9-]+ label unique in its file",
    "shot-outside-journey": "move the image into the journey it shows",
    "duplicate-anchor": "retitle one heading: an address names exactly one journey or part",
};

/**
 * The scenarios file a `scenario:` image names: a path from `.devbook/domain/`
 * — `shipping/scenarios.md` — or a repository path.
 */
export function scenarioReferenceFile(given) {
    const text = normalizePath(given);
    return text.startsWith(".devbook/") ? text : `${DOMAIN_FOLDER}/${text}`;
}

/** Every coverage finding in a repository, each `{ check, where, what, fix }`. */
export async function coverage({ repoRoot, scenarioFolder = SCENARIO_FOLDER, specRoots } = {}) {
    const findings = [];
    const add = (check, where, what, fix) => findings.push({ check, where, what, fix });

    const files = await listScenarioFiles(repoRoot);
    const journeys = files.flatMap((file) => file.journeys.filter((journey) => journey.isScenario));
    const specs = await listSpecs(repoRoot, specRoots?.length ? specRoots : undefined);
    const byId = new Map(journeys.map((journey) => [journey.id, journey]));
    const byFile = new Map(files.map((file) => [file.path, file]));

    for (const relPath of await listLegacyPages(repoRoot)) {
        add("legacy-page", relPath, "is a whole file typed `scenario`, the shape before contract 32", "run the 032-scenarios-md migration, which folds it into its context's scenarios.md as one journey");
    }

    for (const file of files) {
        for (const issue of file.issues) add(issue.kind, `${file.path}:${issue.line}`, issue.message.replace(/^line \d+: /, ""), FIXES[issue.kind]);
    }

    const specsOf = new Map();
    for (const spec of specs) {
        const journey = byId.get(spec.page);
        if (!journey) {
            add("spec-without-journey", spec.path, `names ${spec.page}, which is no journey`, "point the header at `<scenarios file>#<journey>`, or remove the spec");
            continue;
        }
        specsOf.set(journey.id, [...(specsOf.get(journey.id) ?? []), spec]);
    }

    for (const journey of journeys) {
        const own = specsOf.get(journey.id) ?? [];
        if (!own.length) {
            add("journey-without-spec", `${journey.path}:${journey.line}`, `no spec names ${journey.id} in its \`// scenario:\` header`, "derive the spec from the journey with devbook:scenario-derive");
            continue;
        }
        const expected = await journeySignature(journey, repoRoot, scenarioFolder);
        const labels = labelsOf(journey);
        for (const spec of own) {
            if (spec.signature !== expected) {
                add(
                    "signature-mismatch",
                    spec.path,
                    spec.signature ? `has signature ${spec.signature}, but ${journey.id} signs as ${expected} — the journey changed since the spec was derived` : "has no `// signature:` header line",
                    "re-derive the spec from the journey with devbook:scenario-derive"
                );
            }
            const captured = specLabels(spec.source);
            for (const label of labels.filter((label) => !captured.includes(label))) {
                add("label-drift", spec.path, `never calls shot(page, '${label}'), a screenshot point of ${journey.id}`, "add the shot() call where the journey puts the point");
            }
            for (const label of [...new Set(captured)].filter((label) => !labels.includes(label))) {
                add("label-drift", spec.path, `calls shot(page, '${label}'), which ${journey.id} has no shot:${label} for`, "rename or remove the call, or add the point to the journey");
            }
        }
    }

    for (const relPath of await listDevbookMarkdown(repoRoot)) {
        const lines = (await readFile(resolveInside(repoRoot, relPath), "utf8")).split(/\r?\n/);
        let fence = null;
        lines.forEach((line, index) => {
            const marker = /^\s*(`{3,}|~{3,})/.exec(line);
            if (fence) {
                if (marker && marker[1][0] === fence[0] && marker[1].length >= fence.length) fence = null;
                return;
            }
            if (marker) {
                fence = marker[1];
                return;
            }
            const prose = line.replace(/(`+)[^`]*?\1/g, (span) => " ".repeat(span.length));
            for (const match of prose.matchAll(/!\[[^\]]*\]\(\s*<?scenario:([^)\s>]*)>?/g)) {
                const hash = match[1].indexOf("#");
                const label = hash < 0 ? "" : match[1].slice(hash + 1);
                const target = scenarioReferenceFile(hash < 0 ? match[1] : match[1].slice(0, hash));
                const where = `${relPath}:${index + 1}`;
                const file = byFile.get(target);
                if (!file) add("dangling-reference", where, `shows scenario:${match[1]}, but ${target} is no scenarios file`, "name a scenarios file as a path from .devbook/domain/");
                else if (!label || !file.shots.some((shot) => shot.label === label)) add("dangling-reference", where, `shows scenario:${match[1]}, but ${target} has no shot:${label}`, "name a label the file has");
            }
        });
    }

    const profiles = await readProfiles(repoRoot, scenarioFolder);
    const profileFile = `${scenarioFolder}/${PROFILES_FILE}`;
    for (const journey of journeys) {
        const name = journey.setup.profile ?? "default";
        if (profiles.map && name in profiles.map) continue;
        const why = profiles.missing ? `${profileFile} does not exist` : profiles.error ? `${profileFile} ${profiles.error}` : `${profileFile} defines ${Object.keys(profiles.map).join(", ") || "no profile"}`;
        add("unknown-profile", journey.id, `runs under profile "${name}", but ${why}`, "define the profile, or name one that exists");
    }

    return findings;
}

function parseArgs(argv) {
    const options = { root: process.cwd(), specs: [], scenarioFolder: SCENARIO_FOLDER, json: false };
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === "--json") options.json = true;
        else if (arg === "--root" && argv[i + 1]) options.root = argv[++i];
        else if (arg === "--specs" && argv[i + 1]) options.specs.push(argv[++i]);
        else if (arg === "--scenario-folder" && argv[i + 1]) options.scenarioFolder = argv[++i];
        else return { error: `unknown or incomplete argument: ${arg}` };
    }
    return options;
}

async function main() {
    const options = parseArgs(process.argv.slice(2));
    if (options.error) {
        console.error(options.error);
        process.exitCode = 2;
        return;
    }
    const repoRoot = path.resolve(options.root);
    const findings = await coverage({ repoRoot, scenarioFolder: options.scenarioFolder, specRoots: options.specs });
    if (options.json) {
        console.log(JSON.stringify({ findings }, null, 2));
    } else if (!findings.length) {
        console.log("Scenario coverage in order: every journey has a spec with a matching signature and labels.");
    } else {
        console.error(`Scenario coverage: ${findings.length} finding(s)\n`);
        for (const finding of findings) console.error(`  ${finding.check}  ${finding.where}\n      ${finding.what}\n      -> ${finding.fix}\n`);
        console.error("The journey is the source: bring the spec to the journey, never the journey to the spec.");
    }
    process.exitCode = findings.length ? 1 : 0;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
