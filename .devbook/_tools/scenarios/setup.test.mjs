// The setup runner's logic: the effective configuration (profile, then the
// journey's flags and settings), portals for start and actor, env: values, and
// the order the repository's setup hook is called in, and the parameter type
// a consumer's tsc reads through setup.ts.
//
// Run: `node --test setup.test.mjs`
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";
import { parseScenariosFile } from "./parse.mjs";
import { effectiveConfig, recordedConfig, resolveEnv, runSetup, specFile } from "./setup.mjs";

const F = "```";
const journey = (meta) => parseScenariosFile(`# Scenarios

${F}meta
type: scenarios
${F}

## Journey

${F}meta
type: scenario
${meta}
${F}

### Done
- **Then** done
`, ".devbook/domain/shop/scenarios.md").journeys[0];
const PROFILES = {
    default: { portals: { app: "env:APP_URL" }, tenant: "demo", flags: {}, settings: {} },
    "tenant-acme": { portals: { customer: "env:SHOP_URL", admin: "env:ADMIN_URL" }, tenant: "acme", flags: { "new-board": true, "legacy-filters": true }, settings: { "backlog-max-columns": 8 } },
};

test("the profile comes first, the journey's flags and settings on top", () => {
    const config = effectiveConfig(
        journey("profile: tenant-acme\nflags: [context.md#beta, -context.md#legacy-filters]\nsettings: [context.md#backlog-max-columns=5, context.md#currency=EUR, context.md#strict=true]\ndata: [a, b]"),
        PROFILES
    );
    assert.equal(config.profile, "tenant-acme");
    assert.equal(config.tenant, "acme");
    assert.deepEqual(config.flags, { "new-board": true, "legacy-filters": false, beta: true });
    assert.deepEqual(config.settings, { "backlog-max-columns": 5, currency: "EUR", strict: true });
    assert.deepEqual(recordedConfig(config), { tenant: "acme", flags: config.flags, settings: config.settings, data: ["a", "b"] });
});

test("no profile is default; an override wins; an unknown one is an error", () => {
    assert.equal(effectiveConfig(journey(""), PROFILES).profile, "default");
    assert.equal(effectiveConfig(journey("profile: default"), PROFILES, { override: "tenant-acme" }).profile, "tenant-acme");
    assert.throws(() => effectiveConfig(journey("profile: nope"), PROFILES), /profile "nope" is not defined/);
});

test("start and actor name a portal, or the profile's first", () => {
    const config = effectiveConfig(journey("profile: tenant-acme\nstart: admin:/products\nactor: [admin:actors.md#product-owner, actors.md#shopper]"), PROFILES);
    assert.equal(config.defaultPortal, "customer");
    assert.deepEqual(config.start, { portal: "admin", route: "/products" });
    assert.deepEqual(config.actors, { admin: "product-owner", customer: "shopper" });
    assert.deepEqual(effectiveConfig(journey("start: /home"), PROFILES).start, { portal: "app", route: "/home" });
    assert.throws(() => effectiveConfig(journey("profile: tenant-acme\nstart: back:/x"), PROFILES), /portal "back"/);
});

test("env: values resolve from the environment, and an unset one is an error", () => {
    assert.equal(resolveEnv("env:APP_URL", { APP_URL: "http://localhost:5000" }), "http://localhost:5000");
    assert.equal(resolveEnv("acme", {}), "acme");
    assert.throws(() => resolveEnv("env:NOPE", {}), /NOPE is not set/);
});

test("the hook runs tenant, data, flags, settings, then sign-in per portal", async () => {
    const calls = [];
    const hook = {
        applyTenant: async (tenant) => calls.push(["applyTenant", tenant]),
        importData: async (name, tenant, folder) => calls.push(["importData", name, tenant, folder]),
        setFlag: async (tenant, flag, on) => calls.push(["setFlag", flag, on]),
        setSetting: async (tenant, setting, value) => calls.push(["setSetting", setting, value]),
        signIn: async (pg, portal, actor) => calls.push(["signIn", pg, portal, actor]),
    };
    const config = effectiveConfig(journey("profile: tenant-acme\ndata: [catalogue]\nsettings: [context.md#currency=EUR]\nactor: [admin:actors.md#product-owner, customer:actors.md#shopper]"), PROFILES);
    const opened = [];
    await runSetup({ hook, config, open: async (portal) => (opened.push(portal), `page:${portal}`), dataFolder: (name) => `data/${name}` });
    assert.deepEqual(calls, [
        ["applyTenant", "acme"],
        ["importData", "catalogue", "acme", "data/catalogue"],
        ["setFlag", "new-board", true],
        ["setFlag", "legacy-filters", true],
        ["setSetting", "backlog-max-columns", 8],
        ["setSetting", "currency", "EUR"],
        ["signIn", "page:admin", "admin", "product-owner"],
        ["signIn", "page:customer", "customer", "shopper"],
    ]);
    assert.deepEqual(opened, ["admin", "customer"]);
});

test("a hook method the journey needs and the hook lacks is an error; one it does not need may be absent", async () => {
    const bare = { ...PROFILES, bare: { portals: { app: "env:APP_URL" } } };
    await assert.rejects(runSetup({ hook: {}, config: effectiveConfig(journey("profile: bare\ndata: [catalogue]"), bare), open: async () => null }), /no importData\(\)/);
    await assert.rejects(runSetup({ hook: {}, config: effectiveConfig(journey(""), PROFILES), open: async () => null }), /no applyTenant\(\)/);
    await runSetup({ hook: {}, config: effectiveConfig(journey("profile: bare"), bare), open: async () => null });
});

test("a setting value stays a string unless it reads back the same as a number", () => {
    const config = effectiveConfig(journey("settings: [c.md#a=5, c.md#b=-0.5, c.md#c=1.10, c.md#d=007, c.md#e=0x10, c.md#f=1e3]"), PROFILES);
    assert.deepEqual(config.settings, { a: 5, b: -0.5, c: "1.10", d: "007", e: "0x10", f: "1e3" });
});

test("two actors on one portal, or an actor with no portal to sign in on, is an error", () => {
    assert.throws(() => effectiveConfig(journey("actor: [actors.md#a, app:actors.md#b]"), PROFILES), /two actors on portal "app"/);
    assert.throws(() => effectiveConfig(journey("profile: none\nactor: actors.md#a"), { none: {} }), /without a portal/);
});

// tsc infers a JS default parameter's type from its value, so without a declared
// type `dataFolder = () => null` takes no argument, and setup.ts — which passes
// `(name) => …` — fails a consumer's `tsc --noEmit` with TS2322.
test("runSetup declares dataFolder as taking the data set's name", async () => {
    const source = await readFile(new URL("./setup.mjs", import.meta.url), "utf8");
    const doc = /\/\*\*((?:(?!\*\/)[\s\S])*)\*\/\s*export async function runSetup\(/.exec(source)?.[1] ?? "";
    assert.match(doc, /@param \{\{[\s\S]*dataFolder\?: \(name: string\) => string \| null[\s\S]*\}\} options/);
    const folders = [];
    await runSetup({ hook: { importData: async (_name, _tenant, folder) => folders.push(folder) }, config: { tenant: null, data: ["a"], flags: {}, settings: {}, actors: {} }, open: async () => null });
    assert.deepEqual(folders, [null], "the default still answers null");
});

// A spec built by scenario() declares its tests and hooks inside setup.ts, so
// Playwright's info.file names setup.ts and the spec header is never found.
test("the spec file comes from the title path, never from info.file", () => {
    const testDir = path.resolve("repo", "e2e");
    const info = { file: path.resolve("repo", ".devbook", "_tools", "scenarios", "setup.ts"), titlePath: [path.join("work", "journey.spec.ts"), "journey", "part"], config: { rootDir: testDir }, project: { testDir } };
    assert.equal(specFile(info), path.join(testDir, "work", "journey.spec.ts"));
    assert.throws(() => specFile({ titlePath: [], config: { rootDir: testDir }, project: { testDir } }), /no title path/);
});

// Playwright names the file suite relative to config.rootDir. With no top-level
// testDir that is the config's folder, so a project testDir of ./e2e puts e2e/
// in titlePath[0]; resolving it against the project's testDir doubles it.
test("the spec file resolves against config.rootDir when the project testDir lies below it", () => {
    const rootDir = path.resolve("repo");
    const info = { titlePath: [path.join("e2e", "journey.spec.ts"), "journey", "part"], config: { rootDir }, project: { testDir: path.join(rootDir, "e2e") } };
    assert.equal(specFile(info), path.join(rootDir, "e2e", "journey.spec.ts"));
});
