// The setup runner's logic: the effective configuration (profile, then the
// page's flags and settings), portals for start and actor, env: values, and
// the order the repository's setup hook is called in.
//
// Run: `node --test setup.test.mjs`
import { test } from "node:test";
import assert from "node:assert/strict";
import { parseScenarioPage } from "./parse.mjs";
import { effectiveConfig, recordedConfig, resolveEnv, runSetup } from "./setup.mjs";

const F = "```";
const page = (meta) => parseScenarioPage(`# Journey\n\n${F}meta\ntype: scenario\n${meta}\n${F}\n\n## Done\n- **Then** done\n`, ".devbook/domain/shop/journey.md");
const PROFILES = {
    default: { portals: { app: "env:APP_URL" }, tenant: "demo", flags: {}, settings: {} },
    "tenant-acme": { portals: { customer: "env:SHOP_URL", admin: "env:ADMIN_URL" }, tenant: "acme", flags: { "new-board": true, "legacy-filters": true }, settings: { "backlog-max-columns": 8 } },
};

test("the profile comes first, the page's flags and settings on top", () => {
    const config = effectiveConfig(
        page("profile: tenant-acme\nflags: [context.md#beta, -context.md#legacy-filters]\nsettings: [context.md#backlog-max-columns=5, context.md#currency=EUR, context.md#strict=true]\ndata: [a, b]"),
        PROFILES
    );
    assert.equal(config.profile, "tenant-acme");
    assert.equal(config.tenant, "acme");
    assert.deepEqual(config.flags, { "new-board": true, "legacy-filters": false, beta: true });
    assert.deepEqual(config.settings, { "backlog-max-columns": 5, currency: "EUR", strict: true });
    assert.deepEqual(recordedConfig(config), { tenant: "acme", flags: config.flags, settings: config.settings, data: ["a", "b"] });
});

test("no profile is default; an override wins; an unknown one is an error", () => {
    assert.equal(effectiveConfig(page(""), PROFILES).profile, "default");
    assert.equal(effectiveConfig(page("profile: default"), PROFILES, { override: "tenant-acme" }).profile, "tenant-acme");
    assert.throws(() => effectiveConfig(page("profile: nope"), PROFILES), /profile "nope" is not defined/);
});

test("start and actor name a portal, or the profile's first", () => {
    const config = effectiveConfig(page("profile: tenant-acme\nstart: admin:/products\nactor: [admin:actors.md#product-owner, actors.md#shopper]"), PROFILES);
    assert.equal(config.defaultPortal, "customer");
    assert.deepEqual(config.start, { portal: "admin", route: "/products" });
    assert.deepEqual(config.actors, { admin: "product-owner", customer: "shopper" });
    assert.deepEqual(effectiveConfig(page("start: /home"), PROFILES).start, { portal: "app", route: "/home" });
    assert.throws(() => effectiveConfig(page("profile: tenant-acme\nstart: back:/x"), PROFILES), /portal "back"/);
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
    const config = effectiveConfig(page("profile: tenant-acme\ndata: [catalogue]\nsettings: [context.md#currency=EUR]\nactor: [admin:actors.md#product-owner, customer:actors.md#shopper]"), PROFILES);
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

test("a hook method the page needs and the hook lacks is an error; one it does not need may be absent", async () => {
    const bare = { ...PROFILES, bare: { portals: { app: "env:APP_URL" } } };
    await assert.rejects(runSetup({ hook: {}, config: effectiveConfig(page("profile: bare\ndata: [catalogue]"), bare), open: async () => null }), /no importData\(\)/);
    await assert.rejects(runSetup({ hook: {}, config: effectiveConfig(page(""), PROFILES), open: async () => null }), /no applyTenant\(\)/);
    await runSetup({ hook: {}, config: effectiveConfig(page("profile: bare"), bare), open: async () => null });
});

test("a setting value stays a string unless it reads back the same as a number", () => {
    const config = effectiveConfig(page("settings: [c.md#a=5, c.md#b=-0.5, c.md#c=1.10, c.md#d=007, c.md#e=0x10, c.md#f=1e3]"), PROFILES);
    assert.deepEqual(config.settings, { a: 5, b: -0.5, c: "1.10", d: "007", e: "0x10", f: "1e3" });
});

test("two actors on one portal, or an actor with no portal to sign in on, is an error", () => {
    assert.throws(() => effectiveConfig(page("actor: [actors.md#a, app:actors.md#b]"), PROFILES), /two actors on portal "app"/);
    assert.throws(() => effectiveConfig(page("profile: none\nactor: actors.md#a"), { none: {} }), /without a portal/);
});
