// Which sync groups one sweep picks up, with the directions Backlog keeps.
//
// units.mjs groups the chapters into sync units and groups; which way a unit is levelled
// is no longer the chapter's to say but Backlog's direction map, read through the
// backlog MCP server's list_sync_directions. This joins the two, deterministically, so
// the pickup never rests on a model's reading of two JSON documents:
//
//   node select-groups.mjs --direction pull --groups groups.json --directions directions.json
//
// groups.json      what `units.mjs --groups --json` printed
// directions.json  what `list_sync_directions` answered for every unit id in it
//
// Prints { version, direction, groups, setAside, orphans } as JSON: every unit carries
// the map's sync, syncFrom and syncLevel; a group's direction is rolled up the way
// units.mjs rolls it up; a group set aside as mixed is decided again here, since the
// directions it was mixed by were the Markdown's. An oversized group stays set aside.

import fs from "node:fs";

/** The directions each sweep picks up: `sync` goes with both. */
export const PICKED_UP = { pull: ["pull", "sync"], push: ["push", "sync"] };

/**
 * The direction a set of unit directions rolls up to: the one they share, a `sync`
 * unit going with whichever single other direction it is grouped with, and null —
 * mixed — when two others meet. units.mjs's own rule.
 */
export function rollUp(directions) {
    const others = new Set(directions.filter((d) => d !== "sync"));
    if (others.size === 0) return directions.length ? "sync" : null;
    return others.size === 1 ? [...others][0] : null;
}

/** Whether a sweep in `direction` picks up a value. */
export const picksUp = (direction, value) => PICKED_UP[direction]?.includes(value) ?? false;

/**
 * Join the groups units.mjs printed with the directions Backlog answered and keep what
 * the sweep in `direction` picks up.
 */
export function selectGroups(listing, answer, direction) {
    if (!PICKED_UP[direction]) throw new Error(`direction takes pull or push; got "${direction}".`);

    const byUnit = new Map((answer?.units ?? []).map((row) => [row.unit, row]));
    const resolve = (unit) => {
        const row = byUnit.get(unit.id);
        // A unit the map did not answer for is the default, never what the Markdown said.
        return row
            ? { ...unit, sync: row.sync, syncFrom: row.syncFrom ?? null, syncLevel: row.syncLevel ?? "default" }
            : { ...unit, sync: "report", syncFrom: null, syncLevel: "default" };
    };

    // units.mjs names a group both mixed and oversized `mixed`, so its size is read from
    // the count rather than from the reason it printed.
    const maxGroupChapters = Number.isInteger(listing?.maxGroupChapters) ? listing.maxGroupChapters : 40;
    const all = [...(listing?.groups ?? []), ...(listing?.setAside ?? [])].map((group) => {
        const units = (group.units ?? []).map(resolve);
        const rolled = rollUp(units.map((unit) => unit.sync));
        const oversized = group.setAside === "oversized" || (group.chapters ?? 0) > maxGroupChapters;
        return { ...group, units, direction: rolled, setAside: rolled === null ? "mixed" : oversized ? "oversized" : null };
    });

    const relevant = (group) =>
        group.setAside === "mixed" ? group.units.some((unit) => picksUp(direction, unit.sync)) : picksUp(direction, group.direction);

    const byId = (a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0);
    return {
        version: 1,
        direction,
        groups: all.filter((group) => !group.setAside && relevant(group)).sort(byId),
        setAside: all.filter((group) => group.setAside && relevant(group)).sort(byId),
        orphans: listing?.orphans ?? [],
    };
}

/** Every unit id in a units.mjs listing, groups and set-asides both — what to ask for. */
export function unitIds(listing) {
    return [...new Set([...(listing?.groups ?? []), ...(listing?.setAside ?? [])].flatMap((group) => (group.units ?? []).map((unit) => unit.id)))].sort();
}

function main(argv) {
    const options = {};
    for (let i = 0; i < argv.length; i++) {
        if (argv[i] === "--direction") options.direction = argv[++i];
        else if (argv[i] === "--groups") options.groups = argv[++i];
        else if (argv[i] === "--directions") options.directions = argv[++i];
        else if (argv[i] === "--unit-ids") options.unitIds = argv[++i];
    }

    if (options.unitIds) {
        console.log(JSON.stringify(unitIds(JSON.parse(fs.readFileSync(options.unitIds, "utf8")))));
        return;
    }

    if (!options.direction || !options.groups || !options.directions) {
        console.error("usage: select-groups.mjs --direction pull|push --groups <units.mjs --groups --json> --directions <list_sync_directions answer>\n       select-groups.mjs --unit-ids <units.mjs --groups --json>");
        process.exit(2);
    }

    const listing = JSON.parse(fs.readFileSync(options.groups, "utf8"));
    const answer = JSON.parse(fs.readFileSync(options.directions, "utf8"));
    console.log(JSON.stringify(selectGroups(listing, answer, options.direction), null, 2));
}

// Run, not imported: a test imports the functions above without a command line.
if (process.argv[1]?.replace(/\\/g, "/").endsWith("/select-groups.mjs")) {
    main(process.argv.slice(2));
}
