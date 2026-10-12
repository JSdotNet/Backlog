# Scenario tools

Connect a repository's e2e suite to its scenario pages — the `type: scenario` pages under
`.devbook/domain/` — through a file contract: each spec names the page it implements and
the signature it was derived from, and each run writes `run.json` and one PNG per screenshot
label into the scenario folder. `devbook:init` and `devbook:update` materialize this folder
to `.devbook/_tools/scenarios/` where `domain/` is adopted. Edit the plugin, never the copy.

| File | Does |
| --- | --- |
| `parse.mjs` | Reads a page's setup fields, parts, steps, and `shot:` labels; a spec's header, `shot()` calls, and part titles; `profiles.json` |
| `signature.mjs` | The page signature: sha256 over the normalized setup fields, part headings, steps, and labels, first 8 hex. The canonical form is at the top of the file. As a CLI, `node signature.mjs <page.md>…` prints the signature a spec's header takes |
| `scenario-page.vector.json` | The shared vector every implementation of the signature is tested against |
| `setup.mjs`, `setup.ts` | The runner a derived spec imports: effective configuration, the setup hook, one browser context per portal, `start` |
| `shot.ts` | `shot(page, label)` and the failure screenshot taken in `afterEach` |
| `report.mjs` | The Playwright reporter that writes `run.json` version 2 and the PNGs |
| `check.mjs` | The coverage check |

Everything ending in `.mjs` is plain Node with no dependencies. The two `.ts` files import
`@playwright/test`, which the repository's e2e suite already has, and load the `.mjs` modules
beside them — which needs Node 22.12 or later, or an ESM test package.

## The contract

```ts
// scenario: .devbook/domain/work/set-up-and-fill-the-backlog.md
// signature: 9c41e2a0
import { scenario, shot } from '../../.devbook/_tools/scenarios/setup';

scenario('set-up-and-fill-the-backlog', ({ part }) => {
  part('Statuses are set up', async ({ page, step }) => {
    await step('Given a product "Webshop" with no backlog statuses', async () => { /* … */ });
    await shot(page, 'statuses-set-up');
  });
});
```

One serial group per page, one `part` per `##` titled exactly as the heading, one `step` per
step, `shot` at each screenshot point. The setup hook is one file the repository writes;
every method is optional until a page needs it:

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
every flag and setting of the effective configuration — the profile, then the page's `flags`
and `settings` — signs in once per portal an `actor` names, and opens `start`. It finds the
page by the spec's `// scenario:` header. A hook written as `.js` or `.mjs` always loads; a
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
is an error. Flag and setting values are passed to the hook as written.

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
| `scenarioFolder` | `use`, and the reporter's options | `.devbook/scenarios` |
| `repoRoot` | the reporter's options | the nearest folder upward holding `.devbook/` |
| `project` | the reporter's options | the first project in the config that ran the spec; one run records one project |
| `SCENARIO_PROFILE` | environment | the page's `profile`, else `default` |

## The run

`<scenario folder>/<stem>/run.json`, committed to the target branch with its PNGs:

| Field | Holds |
| --- | --- |
| `version` | `2` |
| `page`, `signature` | The page's path, and the signature from the spec header the run executed |
| `ranAt`, `profile`, `effectiveConfig` | When, under which profile, and the tenant, flags, settings, and data sets it resolved to |
| `parts[]` | `title`, `anchor`, `outcome` — `passed`, `failed`, or `not-run` — `durationMs`, and `failureShot` for a failed part |
| `shots` | Per label: `file`, the `part` anchor, and the `ranAt` of the run that captured it |

A filtered run writes nothing: a page is written only when every part its spec declares was
scheduled, so `--grep`, `.only`, and a line filter all count. A label the run did not reach keeps its file and its earlier
entry; a PNG whose label the page dropped is pruned. A part that needed a retry is `failed`.

## The coverage check

```bash
node .devbook/_tools/scenarios/check.mjs [--root <dir>] [--specs <dir>]… [--scenario-folder <dir>] [--json]
```

Without `--specs` it walks the whole repository but build output and dot-folders; name a
spec folder under a dot-folder, such as `.test/tests`, with `--specs`. Reports a page without a spec, a spec naming no page, a signature mismatch, label drift,
an image inside a step list, a bad or repeated label, a duplicate stem, a dangling
`scenario:` reference, and an unknown profile. Exits `1` on any finding. The page is the
source: bring the spec to the page, never the page to the spec.
