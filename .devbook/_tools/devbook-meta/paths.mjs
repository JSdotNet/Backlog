// paths.mjs — the one containment check every read in this folder goes
// through: a path taken from an argument, a chapter, or a scanned tree is
// resolved against the repository root and refused when it lands outside it.
//
// `../scenarios/parse.mjs` carries the same function for the scenario tools,
// which import nothing from this folder but `demo.mjs`.

import path from "node:path";

/** The `code` of the error `resolveInside` throws. */
export const OUTSIDE_ROOT = "EOUTSIDE";

/**
 * `given` resolved against `root`, or a thrown error with code `OUTSIDE_ROOT`
 * when it resolves outside `root`. `root` itself counts as inside.
 */
export function resolveInside(root, given) {
    const base = path.resolve(root);
    const file = path.resolve(base, given);
    const rel = path.relative(base, file);
    if (rel === ".." || rel.startsWith(`..${path.sep}`) || path.isAbsolute(rel)) {
        const error = new Error(`${String(given).replace(/\\/g, "/")} resolves outside the repository`);
        error.code = OUTSIDE_ROOT;
        throw error;
    }
    return file;
}
