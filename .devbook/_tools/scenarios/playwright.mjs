// playwright.mjs — finds the `@playwright/test` that is running the suite, for
// `setup.ts` and `shot.ts`, which sit under `.devbook/_tools/scenarios/` and
// not inside the e2e suite.
//
// A static `import … from '@playwright/test'` resolves from this folder upward,
// which misses a suite that is its own npm package — `.test/` with its own
// `node_modules` — and can find a second copy at the root, which Playwright
// refuses to load beside its own. So the package is resolved from where
// Playwright runs: the script the process started (its CLI, or its worker),
// then the working folder, then this folder.
//
// Plain ESM with no dependencies, so the resolution is testable with `node --test`.

import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";

export const PACKAGE = "@playwright/test";

/** Where to resolve the package from, first match wins: the running script, the working folder, this folder. */
export function playwrightBases({ script = process.argv[1], cwd = process.cwd(), here = fileURLToPath(import.meta.url) } = {}) {
    const bases = [];
    if (script) bases.push(path.resolve(script));
    if (cwd) bases.push(path.join(path.resolve(cwd), "noop.js"));
    bases.push(here);
    return bases;
}

/**
 * The file `@playwright/test` resolves to from the first base that has it.
 * `resolve(base)` is injectable for tests; it throws when the base has none.
 */
export function resolvePlaywright(bases = playwrightBases(), resolve = (base) => createRequire(base).resolve(PACKAGE)) {
    for (const base of bases) {
        try {
            return resolve(base);
        } catch {
            // Not installed above this base; try the next.
        }
    }
    throw new Error(`cannot find ${PACKAGE} from ${bases.join(", ")} — install it in the e2e suite's package and run playwright from there`);
}

/** The `@playwright/test` module the suite is running under. */
export function playwrightTest() {
    const file = resolvePlaywright();
    return createRequire(file)(file);
}
