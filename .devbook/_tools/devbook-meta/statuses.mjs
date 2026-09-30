// statuses.mjs — a repository's own `status` ladder, read from
// `.devbook/statuses.json`, over the built-in ladders metadata.mjs defines.
//
// Which transitional rungs a folder has is a team's policy: one team reviews a
// domain chapter before it is agreed and wants that visible as a rung, another
// does not. What stays devbook's is the mechanism — the resting value written by
// omission, the two decision rungs and their records, a rating that is always
// stated — so the file may choose rungs and never touches any of those.
//
// The file is shared with whatever else reads it — a viewer's status picker
// keeps its own keys beside these — so only the keys below are read, and every
// other key, and every folder that is not a devbook folder, is left alone.
//
//   { "folders": { "<folder>": { "rules": [
//       { "files": ["<glob>", …], "scope": "file" | "chapter" | "any", "statuses": ["<rung>", …] }
//   ] } } }
//
// Rules are tried in order and the first whose glob and scope match a block
// decides it; a block no rule matches, or a folder the file does not name,
// takes the built-in ladder. The rule is `devbook-chapter-metadata.md`'s.
import { readFile } from "node:fs/promises";
import path from "node:path";
import {
    builtInStatuses,
    restingStatusFor,
    folderKindForPath,
    DECISION_STATUSES,
    DEVBOOK_FOLDER_NAMES,
    DEVBOOK_PREFIX,
} from "./metadata.mjs";

export const STATUSES_FILE = `${DEVBOOK_PREFIX}statuses.json`;

const SCOPES = ["file", "chapter", "any"];
const RUNG_PATTERN = /^[a-z][a-z0-9]*(-[a-z0-9]+)*$/;

const isObject = (value) => value !== null && typeof value === "object" && !Array.isArray(value);

/**
 * A glob relative to the folder root, to a RegExp: `*` and `?` stay inside one
 * path segment, `**` crosses them, and `**\/` also matches no directory at all,
 * so `**\/actors.md` covers an `actors.md` at the root as well.
 */
export function globToRegExp(glob) {
    const source = String(glob).replace(/\\/g, "/").replace(/^\.\//, "");
    let re = "";
    for (let i = 0; i < source.length; i++) {
        const c = source[i];
        if (c === "*" && source[i + 1] === "*") {
            if (source[i + 2] === "/") {
                re += "(?:.*/)?";
                i += 2;
            } else {
                re += ".*";
                i += 1;
            }
        } else if (c === "*") re += "[^/]*";
        else if (c === "?") re += "[^/]";
        else re += c.replace(/[.+^${}()|[\]\\]/g, "\\$&");
    }
    return new RegExp(`^${re}$`);
}

/**
 * Compile a parsed `statuses.json` into a ladder `validateDocument` accepts,
 * with every configuration error reported once, here, rather than on each block
 * it would otherwise fail. An offending value is dropped from its rule and the
 * rest of the rule stands; a rule whose shape is wrong is skipped whole.
 */
export function compileStatusLadder(json) {
    const issues = [];
    const report = (message) =>
        issues.push({ severity: "error", path: STATUSES_FILE, message: `${STATUSES_FILE} ${message}` });
    const rulesByFolder = new Map();
    const ladder = { statusesFor: (relPath, blockLevel) => statusesFor(rulesByFolder, relPath, blockLevel) };

    if (!isObject(json)) {
        report("is not a JSON object — the built-in ladders apply.");
        return { ladder, issues };
    }
    if (json.folders === undefined) return { ladder, issues };
    if (!isObject(json.folders)) {
        report("has a `folders` that is not an object — the built-in ladders apply.");
        return { ladder, issues };
    }

    for (const folder of DEVBOOK_FOLDER_NAMES) {
        const entry = json.folders[folder];
        if (entry === undefined) continue;
        if (!isObject(entry) || !Array.isArray(entry.rules)) {
            report(`\`folders.${folder}\` has no \`rules\` list — the built-in ${folder}/ ladder applies.`);
            continue;
        }
        const compiled = [];
        entry.rules.forEach((rule, index) => {
            const at = `\`folders.${folder}.rules[${index}]\`${typeof rule?.id === "string" ? ` ("${rule.id}")` : ""}`;
            const result = compileRule(folder, rule, at, report);
            if (result) compiled.push(result);
        });
        rulesByFolder.set(folder, compiled);
    }
    return { ladder, issues };
}

function compileRule(folder, rule, at, report) {
    if (!isObject(rule)) {
        report(`${at} is not an object and is skipped.`);
        return null;
    }
    const { files, statuses } = rule;
    const scope = rule.scope ?? "any";
    if (!Array.isArray(files) || !files.length || !files.every((f) => typeof f === "string" && f.trim())) {
        report(`${at} needs \`files\`, a non-empty list of globs relative to .devbook/${folder}/, and is skipped.`);
        return null;
    }
    if (!SCOPES.includes(scope)) {
        report(`${at} has scope "${scope}", expected one of: ${SCOPES.join(", ")}. The rule is skipped.`);
        return null;
    }
    if (!Array.isArray(statuses) || !statuses.every((s) => typeof s === "string" && RUNG_PATTERN.test(s))) {
        report(`${at} needs \`statuses\`, a list of lowercase kebab-case rungs, and is skipped.`);
        return null;
    }

    const resting = restingStatusFor(folder);
    const kept = [];
    for (const value of new Set(statuses)) {
        if (resting !== null && value === resting) {
            report(
                `${at} lists \`${resting}\`, the resting value in ${folder}/, which is written by omitting the field and is never a rung to choose. Take it out of the list.`
            );
        } else if (DECISION_STATUSES.includes(value)) {
            report(
                folder === "domain"
                    ? `${at} lists \`${value}\`, which devbook adds to every domain/ rule — the approval gate writes it with its record. Take it out of the list.`
                    : `${at} lists \`${value}\`, a decision rung, which only domain/ and a change's proposal carry. Take it out of the list.`
            );
        } else if (resting === null && !builtInStatuses(folder).includes(value)) {
            report(
                `${at} lists \`${value}\`, which is not on the ${folder}/ rating ladder (${builtInStatuses(folder).join(", ")}). A rule there narrows that ladder and never adds to it.`
            );
        } else {
            kept.push(value);
        }
    }
    // A rating is always stated, so a rule leaving a rating folder nothing to
    // write would fail every block it matched; it is skipped instead.
    if (resting === null && !kept.length) {
        if (!statuses.length) {
            report(`${at} is empty, and \`status\` is required in ${folder}/ — a rating is always stated. The rule is skipped.`);
        }
        return null;
    }
    return { at, scope, matchers: files.map(globToRegExp), statuses: kept };
}

function statusesFor(rulesByFolder, relPath, blockLevel) {
    const folder = folderKindForPath(relPath);
    const rules = folder ? rulesByFolder.get(folder) : null;
    if (!rules?.length) return null;
    const subject = String(relPath).replace(/\\/g, "/").slice(DEVBOOK_PREFIX.length + folder.length + 1);
    for (const rule of rules) {
        if (rule.scope !== "any" && rule.scope !== blockLevel) continue;
        if (!rule.matchers.some((re) => re.test(subject))) continue;
        const statuses = folder === "domain" ? [...rule.statuses, ...DECISION_STATUSES] : [...rule.statuses];
        return { statuses, source: `${STATUSES_FILE} ${rule.at}` };
    }
    return null;
}

/**
 * Read the repository's ladder. No file is the ordinary case and yields
 * `ladder: null` — the built-in ladders, exactly as before the file existed.
 */
export async function loadStatusLadder(repoRoot) {
    let raw;
    try {
        raw = await readFile(path.join(repoRoot, STATUSES_FILE), "utf8");
    } catch (error) {
        if (error.code === "ENOENT") return { ladder: null, issues: [] };
        throw error;
    }
    let json;
    try {
        json = JSON.parse(raw.replace(/^﻿/, ""));
    } catch (error) {
        return {
            ladder: null,
            issues: [
                {
                    severity: "error",
                    path: STATUSES_FILE,
                    message: `${STATUSES_FILE} is not valid JSON (${error.message}) — the built-in ladders apply until it parses.`,
                },
            ],
        };
    }
    return compileStatusLadder(json);
}
