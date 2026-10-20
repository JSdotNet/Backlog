// scenario.mjs — the corpus-wide rules of journeys, which no single document
// can check on its own: the setup fields resolved against the chapters and the
// profile file, `scenario:` image references, the `Proved by:` pointers from
// requirement cases, the parts nothing points at, the journey that sits
// outside the context of the requirements it proves, the inline cases a
// context with journeys could move into one, and the `e2e` test entry each
// pointer derives.
//
// A journey is a `##` chapter typed `scenario` in a scenarios file —
// `scenarios.md` or `scenarios.<name>.md` in a bounded context — and its `###`
// headings are its parts. The per-document rules — where the type may sit, the
// status, the step keywords, the field shapes, the anchors unique in a file —
// are validateDocument's, in metadata.mjs. This module runs once per graph
// build, over documents that build already read.
//
// The same pass yields the register `scenarios.json` is written from: every
// journey with its parts, its screenshot labels, its setup fields, and the
// requirement cases that point at each part — so a reader never parses the
// corpus to find them.

import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import {
    DEVBOOK_PREFIX,
    folderKindForPath,
    isScenariosFile,
    proseLinks,
    provedByCases,
    resolveType,
    splitPortal,
    SCENARIO_SETUP_FIELDS,
    SCENARIO_TYPE,
} from "./metadata.mjs";
import { resolveInside } from "./paths.mjs";

/** Where a repository keeps the profiles its journeys name. */
export const SCENARIO_PROFILES = ".devbook/scenarios/profiles.json";

const DOMAIN_ROOT = `${DEVBOOK_PREFIX}domain`;

// The chapter kinds each referencing setup field must resolve to.
const ACTOR_KINDS = ["user", "organisation", "technical"];

// A spec that implements a journey says so in its first lines, and that header
// is what derives a requirement's `e2e` entry. The runner is the one the
// derived specs are written for.
const SPEC_FILE = /\.(?:spec|test)\.[cm]?[jt]sx?$/;
const SPEC_HEADER = /^\s*\/\/\s*scenario:\s*(\S+)\s*$/m;
const SPEC_HEADER_LINES = 10;
const SPEC_SKIP_DIRS = new Set(["node_modules", "bin", "obj", "dist", "build", "out", "coverage"]);
const DERIVED_RUNNER = "playwright";

/**
 * The journeys of one scenarios file, from the chapters the graph build read:
 * each `##` typed `scenario` with the `###` parts under it and the `shot:`
 * labels inside it. A journey is `{ id, path, anchor, title, meta, parts,
 * labels, provedBy, contexts }`, a part `[anchor, title]` in `parts`.
 */
function journeysOf(doc) {
    const journeys = [];
    let journey = null;
    for (const chapter of doc.chapters) {
        if (chapter.level === 2) {
            const typed = chapter.meta && resolveType("domain", chapter.meta) === SCENARIO_TYPE;
            journey = !typed
                ? null
                : {
                      id: `${doc.relPath}#${chapter.slug}`,
                      path: doc.relPath,
                      anchor: chapter.slug,
                      title: chapter.text,
                      line: chapter.line,
                      end: Infinity,
                      meta: chapter.meta,
                      parts: new Map(),
                      partLines: [],
                      labels: [],
                      provedBy: new Map(),
                      contexts: new Map(),
                  };
            if (journeys.length) journeys[journeys.length - 1].end = Math.min(journeys[journeys.length - 1].end, chapter.line);
            if (journey) journeys.push(journey);
            continue;
        }
        if (chapter.level === 3 && journey && !journey.parts.has(chapter.slug)) {
            journey.parts.set(chapter.slug, chapter.text);
            journey.partLines.push({ line: chapter.line, slug: chapter.slug });
        }
    }
    // Each screenshot point, in file order, with the journey and part it sits in.
    for (const link of proseLinks(doc.raw)) {
        const shot = /^shot:(.+)$/i.exec(link.target);
        if (!shot) continue;
        const owner = journeys.find((candidate) => candidate.line < link.line && link.line < candidate.end);
        if (!owner) continue;
        const part = owner.partLines.filter((entry) => entry.line < link.line).pop()?.slug ?? null;
        owner.labels.push({ label: shot[1], part });
    }
    return journeys;
}

/**
 * Every corpus-wide journey problem, and the `tests` entries derived for the
 * requirements a `Proved by:` line connects to a part with a spec.
 *
 * `docs` is every indexed document as `{ relPath, raw, chapters, fileMeta }`;
 * `nodes` the graph's node map, which already holds every chapter with its
 * `kind`. Returns `{ problems, derivedTests, register }`, `derivedTests` a
 * map from a requirement's id to the entries derived for it, `register` every
 * journey as `scenarios.json` lists it — see `registerEntry`.
 */
export async function scenarioProblems(repoRoot, docs, nodes) {
    const problems = [];
    const report = (severity, at, message) => problems.push({ severity, path: at, message: `${at} ${message}` });

    // ── The register: every journey, grouped by its scenarios file ─────────
    const files = new Map();
    for (const doc of docs) {
        if (!isScenariosFile(doc.relPath)) continue;
        files.set(doc.relPath, { journeys: journeysOf(doc), labels: new Set() });
    }
    for (const file of files.values()) for (const journey of file.journeys) for (const { label } of journey.labels) file.labels.add(label);
    const journeys = [...files.values()].flatMap((file) => file.journeys);

    // ── The setup fields, resolved ─────────────────────────────────────────
    const profiles = await readProfiles(repoRoot);
    if (profiles.error) problems.push({ severity: "error", path: SCENARIO_PROFILES, message: `${SCENARIO_PROFILES} ${profiles.error}` });
    for (const journey of journeys) {
        const meta = journey.meta;
        const at = `${journey.path}:${journey.line}`;
        const where = (message) => problems.push({ severity: "error", path: journey.path, message: `${at} journey "#${journey.anchor}" ${message}` });
        const chapterOf = (entry, field, kinds) => {
            const ref = resolveSetupRef(journey.path, entry, nodes);
            const node = nodes.get(ref);
            if (!node) {
                where(`has \`${field}\` entry "${entry}" that does not resolve to a chapter — written as a path from the scenarios file, from its bounded context, or from the repository root.`);
            } else if (!kinds.includes(node.kind)) {
                // A switch modelled as a setting is set through the hook's setSetting, never setFlag.
                const hint = field === "flags" && node.kind === "setting" ? ` A switch modelled as a setting goes under \`settings\` as \`${entry}=true\` or \`=false\`.` : "";
                where(`has \`${field}\` entry "${entry}" that resolves to ${node.kind ? `a \`${node.kind}\` chapter` : "a heading or file"}, not ${kinds.map((kind) => `a \`${kind}\``).join(" or ")} chapter.${hint}`);
            }
        };
        const portals = [];
        for (const entry of asList(meta.actor)) {
            const { portal, rest } = splitPortal(entry);
            if (portal) portals.push({ field: "actor", portal });
            if (rest.includes("#")) chapterOf(rest, "actor", ACTOR_KINDS);
        }
        for (const entry of asList(meta.flags)) {
            const ref = String(entry).replace(/^-/, "");
            if (ref.includes("#")) chapterOf(ref, "flags", ["feature-flag"]);
        }
        for (const entry of asList(meta.settings)) {
            const eq = String(entry).indexOf("=");
            const ref = eq > 0 ? String(entry).slice(0, eq) : String(entry);
            if (ref.includes("#")) chapterOf(ref, "settings", ["setting"]);
        }
        for (const entry of asList(meta.start)) {
            const { portal } = splitPortal(entry);
            if (portal) portals.push({ field: "start", portal });
        }

        if (!profiles.map) continue;
        const named = typeof meta.profile === "string" ? meta.profile : null;
        if (named && !(named in profiles.map)) {
            where(`has \`profile\` "${named}", which ${SCENARIO_PROFILES} does not define — profiles: ${Object.keys(profiles.map).join(", ") || "none"}.`);
            continue;
        }
        const profileName = named ?? "default";
        const profile = profiles.map[profileName];
        if (!profile) continue;
        const defined = Object.keys(profile?.portals ?? {});
        for (const { field, portal } of portals) {
            if (defined.includes(portal)) continue;
            where(`names portal "${portal}" in \`${field}\`, which profile "${profileName}" does not define — portals: ${defined.join(", ") || "none"}.`);
        }
    }

    // ── `scenario:` references, from any page ──────────────────────────────
    for (const doc of docs) {
        for (const link of proseLinks(doc.raw)) {
            const match = /^scenario:(.*)$/i.exec(link.target);
            if (!match) continue;
            const hash = match[1].indexOf("#");
            const at = `${doc.relPath}:${link.line}`;
            if (hash <= 0 || hash === match[1].length - 1) {
                problems.push({ severity: "error", path: doc.relPath, message: `${at} shows "${link.target}" — a screenshot elsewhere is \`scenario:<scenarios file>#<label>\`, the file as a path from .devbook/domain/.` });
                continue;
            }
            const target = scenarioReferenceFile(match[1].slice(0, hash));
            const label = match[1].slice(hash + 1);
            const file = files.get(target);
            if (!file) {
                problems.push({ severity: "error", path: doc.relPath, message: `${at} shows "${link.target}", but ${target} is no scenarios file.` });
            } else if (!file.labels.has(label)) {
                problems.push({ severity: "warning", path: doc.relPath, message: `${at} shows "${link.target}", but ${target} has no screenshot point \`shot:${label}\`.` });
            }
        }
    }

    // ── `Proved by:` pointers, and the parts nobody claims ─────────────────
    const claimed = new Set();
    const derivedTests = new Map();
    let specs = null;
    for (const doc of docs) {
        for (const scenarioCase of provedByCases(doc.relPath, doc.raw)) {
            for (const pointer of scenarioCase.pointers) {
                const hash = pointer.target.indexOf("#");
                if (hash <= 0) continue; // malformed — validateDocument reports it
                const filePart = pointer.target.slice(0, hash);
                const part = pointer.target.slice(hash + 1);
                const at = `${doc.relPath}:${pointer.line}`;
                const resolved = resolveFile(doc.relPath, filePart, files);
                if (resolved.error) {
                    problems.push({ severity: "error", path: doc.relPath, message: `${at} has \`Proved by: ${pointer.target}\`, ${resolved.error}` });
                    continue;
                }
                const fileJourneys = files.get(resolved.path).journeys;
                const journey = fileJourneys.find((candidate) => candidate.parts.has(part));
                if (!journey) {
                    const named = fileJourneys.find((candidate) => candidate.anchor === part);
                    const parts = fileJourneys.flatMap((candidate) => [...candidate.parts.keys()]).join(", ") || "none";
                    problems.push({
                        severity: "error",
                        path: doc.relPath,
                        message: named
                            ? `${at} has \`Proved by: ${pointer.target}\`, which names the journey "#${part}" — a case points at one of its parts: ${[...named.parts.keys()].join(", ") || "none"}.`
                            : `${at} has \`Proved by: ${pointer.target}\`, but ${resolved.path} has no part "#${part}" — parts: ${parts}.`,
                    });
                    continue;
                }
                claimed.add(`${journey.id}#${part}`);
                const requirementContext = contextRoot(doc.relPath);
                if (requirementContext) {
                    const proven = journey.contexts.get(requirementContext) ?? new Set();
                    proven.add(scenarioCase.requirement);
                    journey.contexts.set(requirementContext, proven);
                }
                // The case by its own name, without the `Scenario:` keyword.
                const caseName = scenarioCase.heading.replace(/^\s*Scenario:\s*/i, "");
                const cases = journey.provedBy.get(part) ?? [];
                if (!cases.some((entry) => entry.requirement === scenarioCase.requirement && entry.case === caseName)) {
                    cases.push({ requirement: scenarioCase.requirement, case: caseName });
                }
                journey.provedBy.set(part, cases);
                specs ??= await specIndex(repoRoot);
                const spec = specs.get(journey.id);
                if (spec) {
                    const entry = `e2e:${DERIVED_RUNNER}:${spec}#${journey.parts.get(part)}`;
                    const list = derivedTests.get(scenarioCase.requirement) ?? [];
                    if (!list.includes(entry)) list.push(entry);
                    derivedTests.set(scenarioCase.requirement, list);
                }
            }
        }
    }
    // An inline case in a context that already writes journeys is a candidate
    // to move into one. Informational: inline stays valid, so this never adds
    // to a corpus's warnings.
    const journeyContexts = new Set(journeys.map((journey) => contextRoot(journey.path)).filter(Boolean));
    for (const doc of docs) {
        const context = contextRoot(doc.relPath);
        if (!context || !journeyContexts.has(context)) continue;
        for (const scenarioCase of provedByCases(doc.relPath, doc.raw)) {
            if (!scenarioCase.gwt || scenarioCase.pointers.length) continue;
            problems.push({
                severity: "info",
                path: doc.relPath,
                message: `${doc.relPath}:${scenarioCase.line} \`#### ${scenarioCase.heading}\` is written inline, and ${context} has journeys — a candidate to move into a part and point at with \`Proved by:\`.`,
            });
        }
    }

    // A journey lives in the context of the requirements it proves; one across
    // contexts in the context holding most of them, a tie in any of those.
    for (const journey of journeys) {
        if (!journey.contexts.size) continue;
        const counts = [...journey.contexts].map(([context, proven]) => [context, proven.size]);
        const most = Math.max(...counts.map(([, count]) => count));
        const home = contextRoot(journey.path);
        if ((journey.contexts.get(home)?.size ?? 0) === most) continue;
        const total = counts.reduce((sum, [, count]) => sum + count, 0);
        const leaders = counts.filter(([, count]) => count === most).map(([context]) => context).sort();
        report(
            "warning",
            journey.path,
            `journey "#${journey.anchor}" sits in ${home}, but ${most} of the ${total} requirements it proves are in ${leaders.join(" or ")} — a journey lives in the context holding most of the requirements it proves; move it to a scenarios file there.`
        );
    }

    for (const journey of journeys) {
        for (const slug of journey.parts.keys()) {
            if (claimed.has(`${journey.id}#${slug}`)) continue;
            report(
                "warning",
                journey.path,
                `part "#${slug}" of journey "#${journey.anchor}" is unclaimed — no requirement's \`#### Scenario:\` points at it with \`Proved by: ${path.posix.basename(journey.path)}#${slug}\`.`
            );
        }
    }

    const register = journeys.map(registerEntry);
    register.sort((a, b) => a.path.localeCompare(b.path, "en") || a.line - b.line);
    for (const entry of register) delete entry.line;
    return { problems, derivedTests, register };
}

/**
 * One journey as the register lists it: its address, file, and anchor, its
 * title and the status it declares, the six setup fields as written — `start`
 * and `profile` a string or null, the other four lists — its screenshot labels
 * in order with the part each sits in, and its parts in order, each with the
 * labels under it and every requirement case whose `Proved by:` names it.
 */
function registerEntry(journey) {
    const meta = journey.meta;
    const setup = {};
    for (const field of SCENARIO_SETUP_FIELDS) {
        const single = field === "start" || field === "profile";
        const value = meta[field];
        setup[field] = single ? (value == null || Array.isArray(value) ? null : String(value)) : asList(value).map(String);
    }
    return {
        id: journey.id,
        path: journey.path,
        anchor: journey.anchor,
        line: journey.line,
        title: journey.title,
        status: typeof meta.status === "string" ? meta.status : null,
        setup,
        labels: journey.labels.map((entry) => ({ ...entry })),
        parts: [...journey.parts].map(([anchor, title]) => ({
            title,
            anchor,
            id: `${journey.path}#${anchor}`,
            labels: journey.labels.filter((entry) => entry.part === anchor).map((entry) => entry.label),
            provedBy: [...(journey.provedBy.get(anchor) ?? [])].sort(
                (a, b) => a.requirement.localeCompare(b.requirement, "en") || a.case.localeCompare(b.case, "en")
            ),
        })),
    };
}

function asList(value) {
    if (value === null || value === undefined) return [];
    return Array.isArray(value) ? value : [value];
}

/** The bounded context folder a domain path sits in, or null at the folder root. */
function contextRoot(relPath) {
    const segments = String(relPath).split("/");
    const domain = DOMAIN_ROOT.split("/").length;
    return segments.length > domain + 1 ? segments.slice(0, domain + 1).join("/") : null;
}

/**
 * The scenarios file a `scenario:` image names: a path from `.devbook/domain/`
 * — `shipping/scenarios.md` — or a repository path.
 */
export function scenarioReferenceFile(given) {
    const text = String(given).replace(/\\/g, "/");
    return text.startsWith(DEVBOOK_PREFIX) ? text : `${DOMAIN_ROOT}/${text}`;
}

/**
 * A setup field's chapter reference as a node id: a repository path as
 * written, else the path from the scenarios file's own folder, which is its
 * bounded context — the first whose file the graph holds.
 */
function resolveSetupRef(filePath, entry, nodes) {
    const ref = String(entry).trim();
    if (ref.startsWith(DEVBOOK_PREFIX)) return ref;
    return path.posix.join(path.posix.dirname(filePath), ref);
}

/**
 * The scenarios file a `Proved by:` names: a repository path, or a path from
 * the requirement's folder — `scenarios.md` in its own context,
 * `../shipping/scenarios.md` in another.
 */
function resolveFile(fromPath, filePart, files) {
    if (!filePart.endsWith(".md")) return { error: "which names no `.md` file." };
    const target = filePart.startsWith(DEVBOOK_PREFIX) ? filePart : path.posix.join(path.posix.dirname(fromPath), filePart);
    if (!files.has(target)) {
        return { error: folderKindForPath(target) ? `but ${target} is not a scenarios file.` : `but ${target} is not in the devbook.` };
    }
    return { path: target };
}

/** The repository's profiles, `{ map }` when the file exists and parses, `{}` when it does not exist. */
async function readProfiles(repoRoot) {
    let text;
    try {
        text = await readFile(resolveInside(repoRoot, SCENARIO_PROFILES), "utf8");
    } catch {
        return {};
    }
    try {
        const map = JSON.parse(text);
        if (!map || typeof map !== "object" || Array.isArray(map)) return { error: "is not a JSON object of profiles by name." };
        return { map };
    } catch (error) {
        return { error: `does not parse: ${error.message}` };
    }
}

/**
 * Every spec in the repository that names the journey it implements, as
 * a map from the journey's address to the spec's. Read only when a pointer resolves,
 * so a corpus without one never walks the tree.
 */
async function specIndex(repoRoot) {
    const index = new Map();
    const found = [];
    async function walk(current) {
        let entries;
        try {
            entries = await readdir(resolveInside(repoRoot, current), { withFileTypes: true });
        } catch {
            return;
        }
        for (const entry of entries) {
            const child = current ? `${current}/${entry.name}` : entry.name;
            if (entry.isDirectory()) {
                if (entry.name.startsWith(".") || SPEC_SKIP_DIRS.has(entry.name)) continue;
                await walk(child);
            } else if (entry.isFile() && SPEC_FILE.test(entry.name)) {
                found.push(child);
            }
        }
    }
    await walk("");
    for (const spec of found.sort()) {
        let head;
        try {
            head = (await readFile(resolveInside(repoRoot, spec), "utf8")).split(/\r?\n/).slice(0, SPEC_HEADER_LINES).join("\n");
        } catch {
            continue;
        }
        const match = SPEC_HEADER.exec(head);
        if (match && !index.has(match[1])) index.set(match[1], spec);
    }
    return index;
}
