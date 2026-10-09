// setup.ts — the scenario runner a derived spec imports.
//
//   // scenario: .devbook/domain/work/set-up-and-fill-the-backlog.md
//   // signature: 9c41e2a0
//   import { scenario, shot } from '../../.devbook/_tools/scenarios/setup';
//
//   scenario('set-up-and-fill-the-backlog', ({ part }) => {
//     part('Statuses are set up', async ({ page, step }) => { … await shot(page, 'statuses-set-up'); });
//   });
//
// One serial group per page, one test per part. Before the first part it reads
// the page, resolves the effective configuration, calls the repository's setup
// hook — tenant, data sets, flags, settings, then sign-in per portal — and
// opens `start`. It keeps one browser context per portal for the whole
// journey, so moving between portals keeps each session. A part that fails
// leaves a screenshot of the page it was on.
//
// Options, set under `use` in the Playwright config:
//   scenarioHook    the setup hook, from the repository root; default e2e/scenario-setup.ts
//   scenarioFolder  where profiles.json and data/ live; default .devbook/scenarios
// SCENARIO_PROFILE in the environment runs every page under that profile instead of its own.

import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { test as base, type Browser, type BrowserContext, type BrowserContextOptions, type Page } from '@playwright/test';
import { listScenarioPages, normalizePath, parseSpecHeader, readProfiles, SCENARIO_FOLDER } from './parse.mjs';
import { effectiveConfig, findRepoRoot, resolveEnv, runSetup } from './setup.mjs';
import { failureShot, shot } from './shot';

export { shot };

type Options = { scenarioHook: string; scenarioFolder: string };

export const test = base.extend<{}, Options>({
  scenarioHook: ['e2e/scenario-setup.ts', { option: true, scope: 'worker' }],
  scenarioFolder: [SCENARIO_FOLDER, { option: true, scope: 'worker' }],
});

export type PartArgs = {
  /** The page of the portal the journey is on now. */
  page: Page;
  /** Move to another portal's signed-in page; later parts continue there. */
  portal: (name: string) => Promise<Page>;
  step: <T>(title: string, body: () => Promise<T>) => Promise<T>;
};

type Part = (title: string, body: (args: PartArgs) => Promise<void>) => void;

// The project `use` options a browser context takes. A context made in beforeAll
// does not get them from Playwright's own `context` fixture, so they are passed on.
const CONTEXT_KEYS = [
  'acceptDownloads', 'bypassCSP', 'colorScheme', 'deviceScaleFactor', 'extraHTTPHeaders', 'geolocation',
  'hasTouch', 'httpCredentials', 'ignoreHTTPSErrors', 'isMobile', 'javaScriptEnabled', 'locale', 'offline',
  'permissions', 'proxy', 'reducedMotion', 'screen', 'storageState', 'timezoneId', 'userAgent', 'viewport',
];

function contextOptionsOf(use: Record<string, unknown>): BrowserContextOptions {
  const options: Record<string, unknown> = { ...((use.contextOptions as object) ?? {}) };
  for (const key of CONTEXT_KEYS) if (use[key] !== undefined) options[key] = use[key];
  return options as BrowserContextOptions;
}

/** Declare a scenario page's spec: `stem` is the page's file name without `.md`. */
export function scenario(stem: string, body: (api: { part: Part }) => void): void {
  test.describe.serial(stem, () => {
    const pages = new Map<string, Page>();
    const contexts: BrowserContext[] = [];
    let browser: Browser | undefined;
    let portals: Record<string, string> = {};
    let current: Page | undefined;
    let contextOptions: BrowserContextOptions = {};

    const open = async (portal: string): Promise<Page> => {
      const existing = pages.get(portal);
      if (existing) return existing;
      if (!browser) throw new Error('the scenario has not been set up');
      if (!(portal in portals)) throw new Error(`portal "${portal}" is not in the profile`);
      const context = await browser.newContext({ ...contextOptions, baseURL: resolveEnv(portals[portal]) });
      contexts.push(context);
      const page = await context.newPage();
      pages.set(portal, page);
      return page;
    };

    test.beforeAll(async ({ browser: shared, scenarioHook, scenarioFolder }, info) => {
      browser = shared;
      contextOptions = contextOptionsOf(info.project.use as Record<string, unknown>);
      const root = findRepoRoot(process.cwd());
      // The page the spec header names; the stem only when the header names none.
      const header = parseSpecHeader(await readFile(info.file, 'utf8'));
      const pages = await listScenarioPages(root);
      const page = header.page
        ? pages.find((candidate) => candidate.path === normalizePath(header.page))
        : pages.find((candidate) => candidate.stem === stem);
      if (!page) throw new Error(`no scenario page ${header.page ? `at ${header.page}` : `has the stem "${stem}"`}`);
      const profiles = await readProfiles(root, scenarioFolder);
      if (!profiles.map) throw new Error(`${path.join(scenarioFolder, 'profiles.json')} ${profiles.error ?? 'does not exist'}`);
      const config = effectiveConfig(page, profiles.map, { override: process.env.SCENARIO_PROFILE });
      portals = config.portals;

      const hookPath = path.resolve(root, scenarioHook);
      await readFile(hookPath).catch(() => {
        throw new Error(`the scenario setup hook ${scenarioHook} does not exist; set scenarioHook under use in the Playwright config`);
      });
      const loaded = await import(pathToFileURL(hookPath).href);
      const hook = loaded.default ?? loaded;

      await runSetup({ hook, config, open, dataFolder: (name: string) => path.join(root, scenarioFolder, 'data', name) });
      if (config.start) {
        current = await open(config.start.portal);
        await current.goto(config.start.route);
      } else if (config.defaultPortal) {
        current = await open(config.defaultPortal);
      }
    });

    test.afterEach(async ({}, info) => {
      await failureShot(current, info);
    });

    test.afterAll(async () => {
      for (const context of contexts) await context.close();
    });

    body({
      part: (title, run) =>
        test(title, async () => {
          if (!current) throw new Error(`scenario "${stem}" has no page open: its profile defines no portal`);
          await run({
            page: current,
            portal: async (name) => (current = await open(name)),
            step: (stepTitle, stepBody) => test.step(stepTitle, stepBody),
          });
        }),
    });
  });
}
