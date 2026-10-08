// report.mjs — the run reporter. After a run it writes, per scenario page the
// run covered whole, `<scenario folder>/<stem>/run.json` (version 2) and one
// PNG per screenshot label, into the working tree: runs are committed to the
// target branch, so every reader sees the same one.
//
//   - A filtered run writes nothing: a page counts only when every part its
//     spec declares was scheduled — a grep, `.only`, or a line filter each
//     leave one out. A spec run in several projects records one of them.
//   - A label the run did not reach keeps its file and its earlier entry.
//   - A file whose label the page no longer has is pruned, and so is a failure
//     screenshot of a part that did not fail this time.
//
// Use it from the Playwright config, beside any other reporter:
//
//   reporter: [['list'], ['../.devbook/_tools/scenarios/report.mjs', { scenarioFolder: '.devbook/scenarios' }]]
//
// Plain ESM with no Playwright import: the class only receives Playwright's
// objects, so `writeRun` and the class are both testable with `node --test`.

import { copyFile, mkdir, readdir, readFile, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import { FAILURE_ATTACHMENT, labelsOf, normalizePath, parseScenarioPage, parseSpecHeader, readProfiles, SCENARIO_FOLDER, SHOT_ATTACHMENT, specParts } from "./parse.mjs";
import { effectiveConfig, findRepoRoot, recordedConfig } from "./setup.mjs";

export const RUN_VERSION = 2;
export const RUN_FILE = "run.json";

/** The failure screenshot's file name for a part: the dot keeps it apart from every label. */
export function failureFile(anchor) {
    return `fail.${anchor}.png`;
}

/**
 * A part's outcome from its attempts' Playwright statuses, oldest first.
 * `passed` only for a clean pass: a part that needed a retry is `failed`,
 * because its screenshots are not those of one clean walk. `skipped`, or no
 * attempt at all, is `not-run`.
 */
export function outcomeOf(statuses) {
    if (!statuses.length) return "not-run";
    const last = statuses[statuses.length - 1];
    if (last === "skipped") return statuses.some((status) => status !== "skipped" && status !== "passed") ? "failed" : "not-run";
    if (last === "passed") return statuses.every((status) => status === "passed" || status === "skipped") ? "passed" : "failed";
    return "failed";
}

async function readJson(file) {
    try {
        return JSON.parse(await readFile(file, "utf8"));
    } catch {
        return null;
    }
}

/**
 * Write one page's run.
 *
 * `page` is the parsed page; `parts` maps a part's anchor to `{ outcome,
 * durationMs }` (a part absent from it is `not-run`); `captured` maps a label
 * to the PNG this run took for it and `failures` a part's anchor to its
 * failure screenshot, each an image: `{ body }` holding the bytes, or
 * `{ path }` naming a file. Returns `{ folder, written, pruned }`, file names
 * relative to the page's run folder.
 */
/** Write an image — `{ body }` or `{ path }` — to `target`. */
async function putImage(image, target) {
    if (image?.body) await writeFile(target, image.body);
    else await copyFile(image.path, target);
}

const isImage = (image) => Boolean(image?.body || image?.path);

export async function writeRun({ repoRoot, scenarioFolder = SCENARIO_FOLDER, page, signature, ranAt, profile, effectiveConfig: config, parts = {}, captured = {}, failures = {} }) {
    const folder = path.join(repoRoot, scenarioFolder, page.stem);
    await mkdir(folder, { recursive: true });
    const previous = await readJson(path.join(folder, RUN_FILE));
    let existing = [];
    try {
        existing = await readdir(folder);
    } catch {
        existing = [];
    }

    const written = [];
    const keep = new Set([RUN_FILE]);
    const runParts = page.parts.map((part) => {
        const result = parts[part.anchor] ?? { outcome: "not-run" };
        const entry = { title: part.title, anchor: part.anchor, outcome: result.outcome };
        if (typeof result.durationMs === "number") entry.durationMs = result.durationMs;
        if (result.outcome === "failed" && isImage(failures[part.anchor])) entry.failureShot = failureFile(part.anchor);
        return entry;
    });

    for (const entry of runParts) {
        if (!entry.failureShot) continue;
        await putImage(failures[entry.anchor], path.join(folder, entry.failureShot));
        written.push(entry.failureShot);
        keep.add(entry.failureShot);
    }

    const shots = {};
    const partOf = new Map(page.shots.map((shot) => [shot.label, shot.part]));
    for (const label of labelsOf(page)) {
        const file = `${label}.png`;
        if (isImage(captured[label])) {
            await putImage(captured[label], path.join(folder, file));
            written.push(file);
            keep.add(file);
            shots[label] = { file, part: partOf.get(label), ranAt };
        } else if (existing.includes(file)) {
            keep.add(file);
            const before = previous?.shots?.[label];
            shots[label] = { file, part: partOf.get(label), ranAt: before?.ranAt ?? null };
        }
    }

    const pruned = [];
    for (const name of existing) {
        if (keep.has(name) || !name.toLowerCase().endsWith(".png")) continue;
        await rm(path.join(folder, name), { force: true });
        pruned.push(name);
    }

    const run = { version: RUN_VERSION, page: page.path, signature, ranAt, profile, effectiveConfig: config, parts: runParts, shots };
    await writeFile(path.join(folder, RUN_FILE), `${JSON.stringify(run, null, 2)}\n`, "utf8");
    written.push(RUN_FILE);
    return { folder, written, pruned };
}

/**
 * The Playwright reporter. Options: `repoRoot` (default: found upward from the
 * working folder), `scenarioFolder`, and `project` — the one project whose
 * results a run records when a spec runs in several; by default the first in
 * the config that ran it, so two browsers never blend into one run.
 */
export default class ScenarioReporter {
    constructor(options = {}) {
        this.options = options;
        this.results = new Map(); // "<project>\u0000<spec file>" → title → [{ status, duration, attachments }]
        this.scheduled = new Map(); // spec file → project → Set of titles
        this.projectOrder = [];
        this.ranAt = null;
    }

    printsToStdio() {
        return false;
    }

    onBegin(config, suite) {
        this.ranAt = new Date().toISOString();
        this.projectOrder = (config?.projects ?? []).map((project) => project.name ?? "");
        for (const test of suite.allTests()) {
            const file = test.location.file;
            const project = projectOf(test);
            if (!this.scheduled.has(file)) this.scheduled.set(file, new Map());
            const byProject = this.scheduled.get(file);
            if (!byProject.has(project)) byProject.set(project, new Set());
            byProject.get(project).add(test.title);
        }
    }

    onTestEnd(test, result) {
        const key = `${projectOf(test)}\u0000${test.location.file}`;
        if (!this.results.has(key)) this.results.set(key, new Map());
        const byTitle = this.results.get(key);
        if (!byTitle.has(test.title)) byTitle.set(test.title, []);
        byTitle.get(test.title).push({ status: result.status, duration: result.duration, attachments: result.attachments ?? [] });
    }

    /** The project whose results stand for a spec file. */
    projectFor(byProject) {
        if (this.options.project !== undefined) return byProject.has(this.options.project) ? this.options.project : null;
        return this.projectOrder.find((name) => byProject.has(name)) ?? [...byProject.keys()][0] ?? null;
    }

    async onEnd() {
        const repoRoot = this.options.repoRoot ?? findRepoRoot(process.cwd());
        const scenarioFolder = this.options.scenarioFolder ?? SCENARIO_FOLDER;
        const profiles = await readProfiles(repoRoot, scenarioFolder);

        for (const [file, byProject] of this.scheduled) {
            try {
                await this.writePage({ repoRoot, scenarioFolder, profiles, file, byProject });
            } catch (error) {
                console.error(`scenario reporter: no run written for ${file}: ${error.message}`);
            }
        }
    }

    async writePage({ repoRoot, scenarioFolder, profiles, file, byProject }) {
        const project = this.projectFor(byProject);
        if (project === null) return;
        const titles = byProject.get(project);
        let source;
        try {
            source = await readFile(file, "utf8");
        } catch {
            return;
        }
        const header = parseSpecHeader(source);
        if (!header.page) return;
        const pagePath = normalizePath(header.page);
        let page;
        try {
            page = parseScenarioPage(await readFile(path.join(repoRoot, pagePath), "utf8"), pagePath);
        } catch {
            return;
        }
        if (!page.isScenario) return;

        // Filtered: a part the spec declares — by its call, or by the page's title in its
        // source — that this run did not schedule. Then the run says nothing about the page.
        const declared = new Set(specParts(source));
        for (const part of page.parts) if (source.includes(part.title)) declared.add(part.title);
        if ([...declared].some((title) => !titles.has(title))) return;

        let config = null;
        let profile = process.env.SCENARIO_PROFILE || page.setup.profile || "default";
        try {
            const resolved = effectiveConfig(page, profiles.map, { override: process.env.SCENARIO_PROFILE });
            profile = resolved.profile;
            config = recordedConfig(resolved);
        } catch {
            config = null;
        }

        const byTitle = this.results.get(`${project}\u0000${file}`) ?? new Map();
        const parts = {};
        const captured = {};
        const failures = {};
        for (const part of page.parts) {
            const attempts = byTitle.get(part.title) ?? [];
            const outcome = outcomeOf(attempts.map((attempt) => attempt.status));
            parts[part.anchor] = { outcome, durationMs: attempts.reduce((sum, attempt) => sum + (attempt.duration ?? 0), 0) };
            const last = attempts[attempts.length - 1];
            for (const attachment of last?.attachments ?? []) {
                const image = attachment.body ? { body: attachment.body } : attachment.path ? { path: attachment.path } : null;
                if (!image) continue;
                if (attachment.name?.startsWith(SHOT_ATTACHMENT)) captured[attachment.name.slice(SHOT_ATTACHMENT.length)] = image;
                if (attachment.name === FAILURE_ATTACHMENT && outcome === "failed") failures[part.anchor] = image;
            }
        }

        await writeRun({ repoRoot, scenarioFolder, page, signature: header.signature, ranAt: this.ranAt, profile, effectiveConfig: config, parts, captured, failures });
    }
}

function projectOf(test) {
    return test.parent?.project?.()?.name ?? "";
}
