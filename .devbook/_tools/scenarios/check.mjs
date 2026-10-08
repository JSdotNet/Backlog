#!/usr/bin/env node
// check.mjs — the coverage check: do the specs still match the scenario pages?
//
// Each finding names what drifted, where, and how to fix it:
//   page-without-spec    a page no spec header names: the journey is never run
//   spec-without-page    a spec header names a page that is not a scenario page
//   signature-mismatch   the page changed since its spec was derived
//   label-drift          the spec's shot() labels and the page's shot: labels differ
//   image-in-steps       a shot: image sits inside a step list
//   bad-label            a label that is not [a-z0-9-]+, or used twice in a page
//   duplicate-stem       two pages share a file name, so no tool can tell them apart
//   dangling-reference   a scenario:<stem>#<label> image names no page or no label
//   unknown-profile      the profile a page runs under is not in profiles.json
//
// Reads files only — no browser, no application, no build. The page is the
// source: fix a finding by bringing the spec to the page, never the reverse.
//
//   node .devbook/_tools/scenarios/check.mjs [--root <dir>] [--specs <dir>]… [--scenario-folder <dir>] [--json]
//
// Exits 0 when nothing is found, 1 when something is, 2 on a usage error.

import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { labelsOf, listDevbookMarkdown, listScenarioPages, listSpecs, PROFILES_FILE, readProfiles, SCENARIO_FOLDER, specLabels } from "./parse.mjs";
import { pageSignature } from "./signature.mjs";

/** Every coverage finding in a repository, each `{ check, where, what, fix }`. */
export async function coverage({ repoRoot, scenarioFolder = SCENARIO_FOLDER, specRoots } = {}) {
    const findings = [];
    const add = (check, where, what, fix) => findings.push({ check, where, what, fix });

    const pages = await listScenarioPages(repoRoot);
    const specs = await listSpecs(repoRoot, specRoots?.length ? specRoots : undefined);
    const byPath = new Map(pages.map((page) => [page.path, page]));

    const byStem = new Map();
    for (const page of pages) byStem.set(page.stem, [...(byStem.get(page.stem) ?? []), page]);
    for (const [stem, same] of byStem) {
        for (const page of same.slice(1)) {
            add("duplicate-stem", page.path, `has the stem "${stem}" of ${same[0].path}`, "rename one: a stem is unique across every bounded context");
        }
    }

    for (const page of pages) {
        for (const issue of page.issues) {
            add(issue.kind, `${page.path}:${issue.line}`, issue.message.replace(/^line \d+: /, ""), issue.kind === "image-in-steps" ? "leave a blank line between the last step and the image" : "give the screenshot point a unique [a-z0-9-]+ label");
        }
    }

    const specsOf = new Map();
    for (const spec of specs) {
        const page = byPath.get(spec.page);
        if (!page) {
            add("spec-without-page", spec.path, `names ${spec.page}, which is not a scenario page`, "point the header at the page, or remove the spec");
            continue;
        }
        specsOf.set(page.path, [...(specsOf.get(page.path) ?? []), spec]);
    }

    for (const page of pages) {
        const own = specsOf.get(page.path) ?? [];
        if (!own.length) {
            add("page-without-spec", page.path, "no spec names this page in its `// scenario:` header", "derive the spec from the page with devbook:scenario-derive");
            continue;
        }
        const expected = await pageSignature(page, repoRoot, scenarioFolder);
        const labels = labelsOf(page);
        for (const spec of own) {
            if (spec.signature !== expected) {
                add(
                    "signature-mismatch",
                    spec.path,
                    spec.signature ? `has signature ${spec.signature}, but ${page.path} signs as ${expected} — the page changed since the spec was derived` : "has no `// signature:` header line",
                    "re-derive the spec from the page with devbook:scenario-derive"
                );
            }
            const captured = specLabels(spec.source);
            for (const label of labels.filter((label) => !captured.includes(label))) {
                add("label-drift", spec.path, `never calls shot(page, '${label}'), a screenshot point of ${page.path}`, "add the shot() call where the page puts the point");
            }
            for (const label of [...new Set(captured)].filter((label) => !labels.includes(label))) {
                add("label-drift", spec.path, `calls shot(page, '${label}'), which ${page.path} has no shot:${label} for`, "rename or remove the call, or add the point to the page");
            }
        }
    }

    for (const relPath of await listDevbookMarkdown(repoRoot)) {
        const lines = (await readFile(path.join(repoRoot, relPath), "utf8")).split(/\r?\n/);
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
                const [stem, label] = match[1].split("#");
                const where = `${relPath}:${index + 1}`;
                const found = byStem.get(stem) ?? [];
                if (!found.length) add("dangling-reference", where, `shows scenario:${match[1]}, but no scenario page has the stem "${stem}"`, "point it at an existing page");
                else if (!label || !labelsOf(found[0]).includes(label)) add("dangling-reference", where, `shows scenario:${match[1]}, but ${found[0].path} has no shot:${label ?? ""}`, "name a label the page has");
            }
        });
    }

    const profiles = await readProfiles(repoRoot, scenarioFolder);
    const profileFile = `${scenarioFolder}/${PROFILES_FILE}`;
    for (const page of pages) {
        const name = page.setup.profile ?? "default";
        if (profiles.map && name in profiles.map) continue;
        const why = profiles.missing ? `${profileFile} does not exist` : profiles.error ? `${profileFile} ${profiles.error}` : `${profileFile} defines ${Object.keys(profiles.map).join(", ") || "no profile"}`;
        add("unknown-profile", page.path, `runs under profile "${name}", but ${why}`, "define the profile, or name one that exists");
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
        console.log("Scenario coverage in order: every page has a spec with a matching signature and labels.");
    } else {
        console.error(`Scenario coverage: ${findings.length} finding(s)\n`);
        for (const finding of findings) console.error(`  ${finding.check}  ${finding.where}\n      ${finding.what}\n      -> ${finding.fix}\n`);
        console.error("The page is the source: bring the spec to the page, never the page to the spec.");
    }
    process.exitCode = findings.length ? 1 : 0;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
