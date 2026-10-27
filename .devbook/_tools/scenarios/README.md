# Scenario tools

Connect a repository's e2e suite to its journeys — each `##` typed `scenario` in a
`scenarios.md` or `scenarios.<name>.md` under `.devbook/domain/<context>/` — through a file
contract: each spec names the journey it implements, as `<scenarios file>#<journey>`, and the
signature it was derived from, and each run writes `run.json` and one PNG per screenshot label
into the journey's run folder beside its scenarios file. `devbook:init` and `devbook:update` materialize this folder
to `.devbook/_tools/scenarios/` where `domain/` is adopted. Edit the plugin, never the copy.

| File | Does |
| --- | --- |
| `parse.mjs` | Reads a scenarios file's journeys with their setup fields, parts, steps, and `shot:` labels; a spec's header, `shot()` calls, and part titles; `profiles.json`. Its `resolveInside` refuses a path that resolves outside the repository, and every tool here reads through it |
| `signature.mjs` | The journey signature: sha256 over the normalized setup fields, part headings, steps, and labels, first 8 hex. The canonical form is at the top of the file. As a CLI, `node signature.mjs <scenarios.md>[#<journey>]…` prints the signature a spec's header takes |
| `scenario-page.vector.json` | The shared vector every implementation of the signature is tested against |
| `setup.mjs`, `setup.ts` | The runner a derived spec imports: effective configuration, the setup hook, one browser context per portal, `start` |
| `shot.ts` | `shot(page, label)` and the failure screenshot taken in `afterEach` |
| `playwright.mjs` | Finds the `@playwright/test` the suite runs under, for the two `.ts` files |
| `package.json` | `"type": "module"`, so Playwright loads the two `.ts` files and the `.mjs` they import as ES modules |
| `report.mjs` | The Playwright reporter that writes `run.json` version 2 and the PNGs |
| `check.mjs` | The coverage check |
| `demo-sync.mjs` | Re-copies each linked walkthrough's and playthrough's step `text` and `signature` from its journey into a demo |

Everything ending in `.mjs` is plain Node with no dependencies; `demo-sync.mjs` also imports
`../devbook-meta/demo.mjs`, the checker installed beside this folder. The two `.ts` files use
`@playwright/test`, which the repository's e2e suite already has.

## Where the suite lives

The suite is an ES module package — its `package.json` says `"type": "module"` — either the
repository root or a folder of its own, such as `.test/` with its own `node_modules`. Both work
with no mapping in a tsconfig and no file added under `.devbook/_tools/`:

- `playwright.mjs` resolves `@playwright/test` from the script Playwright runs, then the
  working folder, then this folder — so the runner and the suite share one Playwright, and a
  copy at the root never shadows the suite's own.
- The `package.json` here makes this folder an ES module scope, so when an ES module suite
  imports `setup.ts`, it and the `.mjs` modules it loads run as ES modules.

A CommonJS suite loads `setup.ts` through `require`, which compiles it to CommonJS whatever this
folder says; it then needs Node 22.12 or later to load the `.mjs` modules beside it.

`tsc` over the specs follows the import into this folder, and looks for the Playwright types
from here. Where the suite is its own package, map them in the tsconfig `tsc` runs with —
`"paths": { "@playwright/test": ["./node_modules/@playwright/test"] }` — and leave the Playwright
config's `tsconfig` without it: Playwright needs no mapping, and one there changes which entry
of the package every spec loads.

## The contract

```ts
// scenario: .devbook/domain/work/scenarios.md#set-up-and-fill-the-backlog
// signature: 9c41e2a0
import { scenario, shot } from '../../../../.devbook/_tools/scenarios/setup';

scenario('set-up-and-fill-the-backlog', ({ part }) => {
  part('Statuses are set up', async ({ page, step }) => {
    await step('Given a product "Webshop" with no backlog statuses', async () => { /* … */ });
    await shot(page, 'statuses-set-up');
  });
});
```

One serial group per journey, one `part` per `###` titled exactly as the heading, one `step`
per step, `shot` at each screenshot point. A spec sits at
`<e2e folder>/<context>/<scenarios file without .md>/<journey>.spec.ts`. The setup hook is one
file the repository writes; every method is optional until a journey needs it:

```ts
export default {
  applyTenant: async (tenant) => {},
  importData: async (name, tenant, folder) => {},   // folder: <scenario folder>/data/<name>/
  setFlag: async (tenant, flag, on) => {},
  setSetting: async (tenant, setting, value) => {},
  signIn: async (page, portal, actor) => {},
};
```

Before the first part the runner applies the tenant, imports the data sets in order, sets
every flag and setting of the effective configuration — the profile, then the journey's
`flags` and `settings` — signs in once per portal an `actor` names, and opens `start`. It
finds the journey by the spec's `// scenario:` header. A hook written as `.js` or `.mjs` always loads; a
`.ts` hook relies on Playwright compiling a dynamic import, which depends on its version.

`<scenario folder>/profiles.json` holds the profiles by name; the first portal is the default:

```json
{
  "default": { "portals": { "app": "env:APP_URL" }, "tenant": "demo", "flags": {}, "settings": {} },
  "tenant-acme": { "portals": { "customer": "env:SHOP_URL", "admin": "env:ADMIN_URL" }, "tenant": "acme",
                   "flags": { "new-board": true }, "settings": { "backlog-max-columns": 8 } }
}
```

A portal URL or a tenant written `env:<NAME>` is read from the environment, and an unset one
is an error. Flag and setting values are passed to the hook as written. A journey's `flags` name
`feature-flag` chapters only, and reach `setFlag`; a switch the product models as a `setting`
chapter goes under `settings` as `<chapter>=true` or `=false`, and reaches `setSetting`.

## Wiring it in

```ts
// playwright.config.ts
export default defineConfig({
  reporter: [['list'], ['../.devbook/_tools/scenarios/report.mjs']],
  use: { scenarioHook: 'e2e/scenario-setup.ts' },
});
```

| Option | Where | Default |
| --- | --- | --- |
| `scenarioHook` | `use` | `e2e/scenario-setup.ts`, from the repository root |
| `scenarioFolder` | `use`, and the reporter's options: where `profiles.json` and `data/` live | `.devbook/scenarios` |
| `repoRoot` | the reporter's options | the nearest folder upward holding `.devbook/` |
| `project` | the reporter's options | the first project in the config that ran the spec; one run records one project |
| `SCENARIO_PROFILE` | environment | the journey's `profile`, else `default` |

Keep `scenarioFolder` at its default in a repository the devbook check covers. The check's
profile rule and its stale-walkthrough rule read `.devbook/scenarios/` and take no option, so
a profile or data set kept elsewhere is invisible to them; `check.mjs --scenario-folder` and `demo-sync.mjs
--scenario-folder` exist for fixtures and tests.

## The run

`<scenarios file without .md>/<journey>/run.json` — `.devbook/domain/work/scenarios/set-up-and-fill-the-backlog/run.json` —
committed to the target branch with its PNGs:

| Field | Holds |
| --- | --- |
| `version` | `3` |
| `journey`, `signature` | The journey's address, and the signature from the spec header the run executed |
| `ranAt`, `profile`, `effectiveConfig` | When, under which profile, and the tenant, flags, settings, and data sets it resolved to |
| `parts[]` | `title`, `anchor`, `outcome` — `passed`, `failed`, or `not-run` — `durationMs`, and for a failed part `failureShot` and `failure` |
| `parts[].failure` | `step`, the number of the journey step that threw, counted across the part from 1, or `null` when the error came from outside every step; `message`, the error without terminal colours |
| `shots` | Per label: `file`, the `part` anchor, and the `ranAt` and `signature` of the run that captured it |

A filtered run writes nothing: a journey is written only when every part its spec declares was
scheduled, so `--grep`, `.only`, and a line filter all count. A run that executed none of a
journey's parts writes nothing for it either, so `playwright test --list` leaves every run alone.
A label the run did not reach keeps its file and its earlier entry, signature included: a
reader holds each shot against the journey through its own `signature`, not the run's. A PNG
whose label the journey dropped is pruned. A part that needed a retry is `failed`. `failure` and
a shot's `signature` came after the first version-2 reporter, so a reader treats both as
optional. Version 3 renamed `page` to `journey` and moved the run beside its scenarios file.

## The coverage check

```bash
node .devbook/_tools/scenarios/check.mjs [--root <dir>] [--specs <dir>]… [--scenario-folder <dir>] [--json]
```

Without `--specs` it walks the whole repository but build output and dot-folders; name a
spec folder under a dot-folder, such as `.test/tests`, with `--specs`. Reports a journey
without a spec, a spec naming no journey, a signature mismatch, label drift, an image inside a
step list, a bad or repeated label, a shot outside a journey, a repeated anchor, a dangling
`scenario:` reference, an unknown profile, and a page of the shape before contract 32. Exits
`1` on any finding. The journey is the source: bring the spec to the journey, never the
journey to the spec.

## The demo sync

```bash
node .devbook/_tools/scenarios/demo-sync.mjs [<demo.html>…] [--root <dir>] [--scenario-folder <dir>] [--check]
```

Without a file it takes every `*.demo.html` under `.devbook/domain/`. For each linked
walkthrough — one whose `part` names a journey's part — it replaces each step's `text` with
the text of the part step its `step` names, keeping a leading keyword when the copy had one,
and `signature` with the journey's current one. A playthrough — one whose `journey` names a
whole journey — is synced the same way, each step from the part its `part` names. Nothing
else in the file changes. `--check`
writes nothing and exits `1` while any linked walkthrough is stale or drifted; both forms exit
`1` on a `part` that does not resolve or a `step` past the part, which they report and leave
alone. The journey is the source here too: the demo takes its words, never the reverse.
