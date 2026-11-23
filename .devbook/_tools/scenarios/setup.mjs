// setup.mjs — a journey's setup as data: the effective configuration (the
// profile, then the journey's `flags` and `settings` on top) the reporter
// records in run.json. Applying it is the repository's: its specs set up the
// tenant, data, flags, settings, and sign-in however its suite does.

import { existsSync } from "node:fs";
import path from "node:path";
import { resolveInside, splitPortal } from "./parse.mjs";

/** The reference's last segment: `context.md#new-board` → `new-board`. */
export function keyOf(reference) {
    const text = String(reference).trim();
    const at = text.lastIndexOf("#");
    return at >= 0 ? text.slice(at + 1) : text;
}

/**
 * A setting value as written: `true`/`false`, a plain decimal that reads back
 * the same as a number (`5`, `-2`, `0.5`), or else the string untouched —
 * `1.10`, `007`, and `0x10` stay strings, because a number would change them.
 */
export function settingValue(raw) {
    const text = String(raw).trim();
    if (text === "true") return true;
    if (text === "false") return false;
    if (/^-?(?:0|[1-9]\d*)(?:\.\d+)?$/.test(text) && String(Number(text)) === text) return Number(text);
    return text;
}

/**
 * A value that may name an environment variable: `env:<NAME>` reads it from
 * `env`, and an unset one is an error rather than an empty URL or secret.
 */
export function resolveEnv(value, env = process.env) {
    if (typeof value !== "string" || !value.startsWith("env:")) return value;
    const name = value.slice(4);
    if (env[name] === undefined || env[name] === "") throw new Error(`environment variable ${name} is not set (named as "${value}")`);
    return env[name];
}

/**
 * The effective configuration a journey runs under.
 *
 * The profile is `override`, else the journey's `profile`, else `default`; it must
 * exist in `profiles`. Its tenant, portals, flags, and settings come first; the
 * journey's `flags` switch a flag on (`-` switches it off) and its `settings` set
 * `<chapter>#<key>=<value>` on top. `start` and each `actor` name a portal, or
 * the profile's first one.
 */
export function effectiveConfig(journey, profiles, { override } = {}) {
    const name = override || journey.setup.profile || "default";
    const profile = profiles?.[name];
    if (!profile || typeof profile !== "object") {
        throw new Error(`profile "${name}" is not defined — profiles: ${Object.keys(profiles ?? {}).join(", ") || "none"}`);
    }
    const portals = { ...(profile.portals ?? {}) };
    const defaultPortal = Object.keys(portals)[0] ?? null;
    const portalOf = (entry) => {
        const { portal, rest } = splitPortal(entry);
        if (!portal && !defaultPortal) throw new Error(`${journey.id} names "${entry}" without a portal, and profile "${name}" defines none`);
        return { portal: portal ?? defaultPortal, rest };
    };

    const flags = { ...(profile.flags ?? {}) };
    for (const entry of journey.setup.flags) {
        const off = entry.trim().startsWith("-");
        flags[keyOf(entry.trim().replace(/^-/, ""))] = !off;
    }
    const settings = { ...(profile.settings ?? {}) };
    for (const entry of journey.setup.settings) {
        const at = entry.indexOf("=");
        if (at <= 0) throw new Error(`setting "${entry}" on ${journey.id} is not <chapter>=<value>`);
        settings[keyOf(entry.slice(0, at))] = settingValue(entry.slice(at + 1));
    }

    let start = null;
    if (journey.setup.start) {
        const { portal, rest } = portalOf(journey.setup.start);
        start = { portal, route: rest };
    }
    const actors = {};
    for (const entry of journey.setup.actor) {
        const { portal, rest } = portalOf(entry);
        if (portal in actors) throw new Error(`${journey.id} names two actors on portal "${portal}"; each portal signs in one actor`);
        actors[portal] = keyOf(rest);
    }
    for (const portal of [start?.portal, ...Object.keys(actors)]) {
        if (portal && !(portal in portals)) {
            throw new Error(`${journey.id} names portal "${portal}", which profile "${name}" does not define — portals: ${Object.keys(portals).join(", ") || "none"}`);
        }
    }

    return {
        profile: name,
        tenant: profile.tenant ?? null,
        portals,
        defaultPortal,
        flags,
        settings,
        data: [...journey.setup.data],
        start,
        actors,
    };
}

/** The part of the effective configuration `run.json` records: no URLs, nothing resolved from the environment. */
export function recordedConfig(config) {
    return { tenant: config.tenant, flags: config.flags, settings: config.settings, data: config.data };
}

/** The repository root: the nearest folder from `start` upward that holds `.devbook/`. */
export function findRepoRoot(start = process.cwd()) {
    let current = path.resolve(start);
    for (;;) {
        if (existsSync(resolveInside(current, ".devbook"))) return current;
        const parent = path.dirname(current);
        if (parent === current) throw new Error(`no .devbook/ folder at or above ${start}`);
        current = parent;
    }
}
