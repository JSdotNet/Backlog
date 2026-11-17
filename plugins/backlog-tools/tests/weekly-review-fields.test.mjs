// The weekly-review skill reads the deferred item's review day and the goal's forecast off the
// backlog server's answers, and words an undated deferral as the in-app takeover does. Each
// backticked camelCase field it names must be a property of the payload record that answers it,
// each backticked snake_case name must be a tool the server has, no proxy line may come back,
// and the takeover's undated-deferral wording must be the skill's.
//   node --test plugins/backlog-tools/tests/weekly-review-fields.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const PLUGIN = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const SRC = path.join(PLUGIN, "..", "..", "src");
const MCP = path.join(SRC, "Infrastructure", "Backlog.Infrastructure.Mcp");
const BUILDER = path.join(SRC, "Modules", "Progress", "Backlog.Modules.Progress.UI", "WeeklyReview", "WeeklyReviewChecklistBuilder.cs");

const skill = fs.readFileSync(path.join(PLUGIN, "skills", "weekly-review", "SKILL.md"), "utf8");
const sources = fs
  .readdirSync(MCP)
  .filter((file) => file.endsWith(".cs"))
  .map((file) => fs.readFileSync(path.join(MCP, file), "utf8"))
  .join("\n");

/** The positional parameters of a C# record, camel-cased as the server writes them. */
function fieldsOf(record) {
  const match = sources.match(new RegExp(`record ${record}\\(([^)]*)\\)`, "s"));
  assert.ok(match, `no record ${record} in ${MCP}`);
  return match[1]
    .split(",")
    .map((parameter) => parameter.trim().split(/\s+/)[1])
    .filter(Boolean)
    .map((name) => name[0].toLowerCase() + name.slice(1));
}

test("a deferred item's review day is a field of both Inbox answers", () => {
  assert.ok(skill.includes("`deferredUntil`"));
  assert.ok(fieldsOf("InboxItemSummaryPayload").includes("deferredUntil"));
  assert.ok(fieldsOf("InboxItemPayload").includes("deferredUntil"));
});

test("a goal's forecast is a field of list_goals' answer, with every part the skill names", () => {
  assert.ok(fieldsOf("GoalPayload").includes("forecast"));
  const forecast = fieldsOf("GoalForecastPayload");
  for (const [, part] of skill.matchAll(/`forecast\.([a-zA-Z]+)`/g)) {
    assert.ok(forecast.includes(part), `GoalForecastPayload has no ${part}: ${forecast.join(", ")}`);
  }
});

test("the skill reports no proxy for either answer", () => {
  assert.doesNotMatch(skill, /proxy/i);
  assert.doesNotMatch(skill, /carries no (review day|forecast)/i);
});

test("the takeover words an undated deferral as the skill does", () => {
  const builder = fs.readFileSync(BUILDER, "utf8");
  const wording = builder.match(/NoReviewDate = "([^"]+)"/)?.[1];
  assert.ok(wording, "no NoReviewDate in the checklist builder");
  assert.ok(skill.includes(`"${wording.replace(/\.$/, "")}"`), `the skill does not say "${wording}"`);
});

const tools = new Set([...sources.matchAll(/const string \w+ = "([a-z_]+)"/g)].map((match) => match[1]));
for (const name of new Set([...skill.matchAll(/`([a-z]+(?:_[a-z]+)+)`/g)].map((match) => match[1]))) {
  test(`${name}, named by weekly-review, is a backlog tool`, () => {
    assert.ok(tools.has(name), `no const string … = "${name}" in ${MCP}`);
  });
}
