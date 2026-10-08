// shot.ts — the screenshot at a page's `shot:<label>` point, and the one taken
// when a part fails.
//
// Neither writes into the scenario folder. Each is attached to the result as
// bytes — so `preserveOutput` cannot delete it first — and the run reporter
// (`report.mjs`) writes it to `<scenario folder>/<stem>/` only when the run
// covered the whole page. That is what keeps a filtered run from writing anything.

import { test, type Page, type TestInfo } from '@playwright/test';
import type { Buffer } from 'node:buffer';

// The same values as LABEL, SHOT_ATTACHMENT, and FAILURE_ATTACHMENT in
// parse.mjs. Written twice so this file needs nothing but Playwright.
const LABEL = /^[a-z0-9-]+$/;
const SHOT_ATTACHMENT = 'scenario-shot:';
const FAILURE_ATTACHMENT = 'scenario-failure';

/**
 * Capture the screenshot point `label`. `animations: 'disabled'` puts running
 * transitions at their end state, so the picture is what a user sees a moment
 * later rather than a frame halfway through.
 */
export async function shot(page: Page, label: string): Promise<Buffer> {
  if (!LABEL.test(label)) throw new Error(`shot label "${label}" is not [a-z0-9-]+`);
  const body = await page.screenshot({ fullPage: true, animations: 'disabled' });
  await test.info().attach(`${SHOT_ATTACHMENT}${label}`, { body, contentType: 'image/png' });
  return body;
}

/** In `afterEach`: when the part did not end as expected, capture the page it was on. */
export async function failureShot(page: Page | undefined, info: TestInfo): Promise<void> {
  if (!page || page.isClosed() || info.status === info.expectedStatus) return;
  try {
    const body = await page.screenshot({ fullPage: true, animations: 'disabled' });
    await info.attach(FAILURE_ATTACHMENT, { body, contentType: 'image/png' });
  } catch {
    // A page that cannot be captured any more leaves the failure without a picture, not a second error.
  }
}
