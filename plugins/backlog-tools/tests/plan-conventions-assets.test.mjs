import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

const read = (path) => readFileSync(new URL(`../skills/${path}`, import.meta.url), 'utf8');
const executePlan = [
  'execute-plan/SKILL.md',
  'execute-plan/assets/landing-modes.md',
  'execute-plan/assets/item-brief.md',
  'execute-plan/assets/handoff.md',
  'final-check/SKILL.md',
];

test('the defaults stay today\'s branch names', () => {
  const modes = read('execute-plan/assets/landing-modes.md');
  assert.match(modes, /`entryBranch` \| `\{slug\}\/\{id\}`/);
  assert.match(modes, /`integrationBranch` \| `plan\/\{slug\}`/);
});

test('branch names are resolved, never hard-coded, outside the conventions', () => {
  for (const path of executePlan) {
    const text = read(path).replace(/## Repository conventions[\s\S]*?(?=\n## |\n### |$)/, '');
    assert.doesNotMatch(text, /--head (plan\/|<slug>)/, path);
    assert.doesNotMatch(text, /`plan\/<slug>`/, path);
  }
});

test('a build is the repository\'s fact, and no tracker is assumed', () => {
  assert.doesNotMatch(read('execute-plan/assets/landing-modes.md'), /pull-request\.yml/);
  for (const path of executePlan) assert.doesNotMatch(read(path), /jira/i, path);
});

test('entry briefs say how to title the pull request', () => {
  assert.match(read('execute-plan/assets/item-brief.md'), /--title "<title>"/);
});
