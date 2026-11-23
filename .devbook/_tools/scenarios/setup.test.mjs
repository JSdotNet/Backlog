// A journey's setup as the reporter records it: the effective configuration
// (profile, then the journey's flags and settings), portals for start and
// actor, and env: values.
//
// Run: `node --test setup.test.mjs`
import { test } from "node:test";
import assert from "node:assert/strict";
import { parseScenariosFile } from "./parse.mjs";
import { effectiveConfig, recordedConfig, resolveEnv } from "./setup.mjs";

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

test("a setting value stays a string unless it reads back the same as a number", () => {
    const config = effectiveConfig(journey("settings: [c.md#a=5, c.md#b=-0.5, c.md#c=1.10, c.md#d=007, c.md#e=0x10, c.md#f=1e3]"), PROFILES);
    assert.deepEqual(config.settings, { a: 5, b: -0.5, c: "1.10", d: "007", e: "0x10", f: "1e3" });
});

test("two actors on one portal, or an actor with no portal to sign in on, is an error", () => {
    assert.throws(() => effectiveConfig(journey("actor: [actors.md#a, app:actors.md#b]"), PROFILES), /two actors on portal "app"/);
    assert.throws(() => effectiveConfig(journey("profile: none\nactor: actors.md#a"), { none: {} }), /without a portal/);
});
